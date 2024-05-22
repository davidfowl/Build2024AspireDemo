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

## Smtp Email Sender

```C#
internal sealed class SmtpEmailSender(SmtpClient client) : IEmailSender<ApplicationUser>
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