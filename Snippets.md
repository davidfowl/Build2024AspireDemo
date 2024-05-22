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