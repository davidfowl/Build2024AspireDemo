﻿using System.Diagnostics;
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

            if (smtpUri.Host != "localhost")
            {
                smtpClient.EnableSsl = true;
            }

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