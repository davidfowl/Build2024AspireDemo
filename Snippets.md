## Mail Dev Resource

```C#
namespace Aspire.Hosting;

public sealed class MailDevResource(string name) : ContainerResource(name), IResourceWithConnectionString
{
    internal const string SmtpEndpointName = "smtp";
    internal const string HttpEndpointName = "http";

    private EndpointReference? _smtpReference;

    public EndpointReference SmtpEndpoint =>
        _smtpReference ??= new(this, SmtpEndpointName);

    public ReferenceExpression ConnectionStringExpression =>
        ReferenceExpression.Create(
            $"smtp://{SmtpEndpoint.Property(EndpointProperty.Host)}:{SmtpEndpoint.Property(EndpointProperty.Port)}"
        );
}

public static class MailDevResourceBuilderExtensions
{
    public static IResourceBuilder<MailDevResource> AddMailDev(
        this IDistributedApplicationBuilder builder,
        string name,
        int? httpPort = null,
        int? smtpPort = null)
    {
        var resource = new MailDevResource(name);

        return builder.AddResource(resource)
                      .WithImage(MailDevContainerImageTags.Image)
                      .WithImageRegistry(MailDevContainerImageTags.Registry)
                      .WithImageTag(MailDevContainerImageTags.Tag)
                      .WithHttpEndpoint(
                          targetPort: 1080,
                          port: httpPort,
                          name: MailDevResource.HttpEndpointName)
                      .WithEndpoint(
                          targetPort: 1025,
                          port: smtpPort,
                          name: MailDevResource.SmtpEndpointName);
    }
}

internal static class MailDevContainerImageTags
{
    internal const string Registry = "docker.io";

    internal const string Image = "maildev/maildev";

    internal const string Tag = "2.0.2";
}
```

## ISmtpClient

```C#
using System.Diagnostics;
using System.Net;
using System.Net.Mail;

internal static class SmtpExtensions
{
    public static IHostApplicationBuilder AddSmtpClient(this IHostApplicationBuilder builder, string connectionName)
    {
        builder.Services.AddSingleton<ISmtpClient, SmtpClientWithTelemetry>();

        builder.Services.AddSingleton(_ =>
        {
            var smtpUri = new UriBuilder(builder.Configuration.GetConnectionString(connectionName) ?? throw new InvalidOperationException($"Connection string '{connectionName}' not found."));
            var smtpClient = new SmtpClient(smtpUri.Host, smtpUri.Port);
            if (smtpUri.UserName != null)
            {
                smtpClient.Credentials = new NetworkCredential(smtpUri.UserName, smtpUri.Password);
            }
            return smtpClient;
        });

        builder.Services.AddOpenTelemetry()
            .WithTracing(t => t.AddSource(SmtpTelemetry.ActivitySourceName));

        builder.Services.AddSingleton<SmtpTelemetry>();

        return builder;
    }
}

internal class SmtpTelemetry
{
    public const string ActivitySourceName = "Smtp";
    public ActivitySource ActivitySource { get; } = new(ActivitySourceName);
}

public interface ISmtpClient
{
    Task SendMailAsync(MailMessage message);
}

class SmtpClientWithTelemetry(SmtpClient client, SmtpTelemetry smtpTelemetry) : ISmtpClient
{
    public async Task SendMailAsync(MailMessage message)
    {
        var activity = smtpTelemetry.ActivitySource.StartActivity("SendMail", ActivityKind.Client);

        if (activity is not null)
        {
            activity.AddTag("mail.from", message.From);
            activity.AddTag("mail.to", message.To);
            activity.AddTag("mail.subject", message.Subject);
            activity.AddTag("peer.service", $"{client.Host}:{client.Port}");
        }

        try
        {
            await client.SendMailAsync(message);
        }
        catch (Exception ex)
        {
            if (activity is not null)
            {
                activity.AddTag("exception.message", ex.Message);
                activity.AddTag("exception.stacktrace", ex.ToString());
                activity.AddTag("exception.type", ex.GetType().FullName);
                activity.SetStatus(ActivityStatusCode.Error);
            }
            
            throw;
        }
        finally
        {
            activity?.Stop();
        }
    }
}
```

## Smtp Email Sender

```C#
internal sealed class SmtpEmailSender(ISmtpClient client) : IEmailSender<ApplicationUser>
{
    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink)
    {
        var message = new MailMessage("builddemo@example.com", email, "Confirmation email", $"Please confirm your account by <a href='{confirmationLink}'>clicking here</a>.")
        {
            IsBodyHtml = true
        };
        return client.SendMailAsync(message);
    }

    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode)
    {
        var message = new MailMessage("builddemo@example.com", email, "Reset your password", $"Please reset your password using the following code: {resetCode}");
        return client.SendMailAsync(message);
    }

    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink)
    {
        var message = new MailMessage("builddemo@example.com", email, "Reset your password", $"Please reset your password by <a href='{resetLink}'>clicking here</a>.")
        {
            IsBodyHtml = true
        };
        return client.SendMailAsync(message);
    }
}
```

## Service Bus Tracing

```C#
internal static class ServiceBusTracingExtensions
{
    public static IResourceBuilder<T> WithAzureTracing<T>(this IResourceBuilder<T> builder)
        where T : IResourceWithEnvironment
    {
        return builder.WithEnvironment("AZURE_EXPERIMENTAL_ENABLE_ACTIVITY_SOURCE", "true");
    }
}
```


## Service Bus Email Sender

```C#
internal sealed class ServiceBusEmailSender(ServiceBusClient client) : IEmailSender<ApplicationUser>
{
    private ServiceBusSender _sender = client.CreateSender("emails");

    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink)
    {
        var emailMessage = new EmailMessage(email, "Confirmation email", $"Please confirm your account by <a href='{confirmationLink}'>clicking here</a>.");
        var message = new ServiceBusMessage(new BinaryData(emailMessage));
        return _sender.SendMessageAsync(message);
    }

    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode)
    {
        var emailMessage = new EmailMessage(email, "Reset your password", $"Please reset your password using the following code: {resetCode}");
        var message = new ServiceBusMessage(new BinaryData(emailMessage));
        return _sender.SendMessageAsync(message);
    }

    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink)
    {
        var emailMessage = new EmailMessage(email, "Reset your password", $"Please reset your password by <a href='{resetLink}'>clicking here</a>.");
        var message = new ServiceBusMessage(new BinaryData(emailMessage));
        return _sender.SendMessageAsync(message);
    }

    record EmailMessage(string To, string Subject, string Body);
}
```

## EmailWorker

```C#
using System.Net.Mail;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;

namespace EmailWorker;

public class Worker(IOptions<EmailOptions> options, ILogger<Worker> logger, ServiceBusClient client, ISmtpClient smtpClient) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var fromEmail = options.Value.From;

        logger.LogInformation("Worker starting with from email: {fromEmail}", fromEmail);

        var processor = client.CreateProcessor("emails");

        processor.ProcessMessageAsync += async args =>
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Received email message: {message}", args.Message.Body.ToString());
            }

            var message = args.Message;

            var emailMessage = message.Body.ToObjectFromJson<EmailMessage>();

            var mailMessage = new MailMessage(fromEmail, emailMessage.To, emailMessage.Subject, emailMessage.Body)
            {
                IsBodyHtml = true
            };

            await smtpClient.SendMailAsync(mailMessage);

            await args.CompleteMessageAsync(message);
        };

        processor.ProcessErrorAsync += args =>
        {
            logger.LogError(args.Exception, "Error processing message");
            return Task.CompletedTask;
        };

        return processor.StartProcessingAsync(stoppingToken);
    }

    record EmailMessage(string To, string Subject, string Body);
}
```
