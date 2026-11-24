using System.Net;
using System.Net.Mail;

namespace DepartmentEmployeeManagement.Api.Services;

public sealed class AppEmailSender(IConfiguration configuration, IWebHostEnvironment environment, ILogger<AppEmailSender> logger) : IAppEmailSender
{
    public Task SendEmailConfirmationAsync(string email, string confirmationLink, CancellationToken cancellationToken = default) =>
        SendAsync(email, "Подтвердите email", "Подтвердите email", confirmationLink, "email confirmation", cancellationToken);

    public async Task SendPasswordResetAsync(string email, string resetLink, CancellationToken cancellationToken = default)
        => await SendAsync(email, "Сброс пароля", "Задайте новый пароль по кнопке ниже. Ссылка действует один час.", resetLink, "password reset", cancellationToken);

    private async Task SendAsync(string email, string subject, string description, string link, string purpose, CancellationToken cancellationToken)
    {
        var host = configuration["Smtp:Host"] ?? configuration["Email:Host"];
        if (string.IsNullOrWhiteSpace(host))
        {
            if (environment.IsDevelopment())
                logger.LogWarning("SMTP is not configured. Development {Purpose} link for {Email}: {Link}", purpose, email, link);
            else
                logger.LogError("{Purpose} requested but SMTP is not configured.", purpose);
            return;
        }

        var port = configuration.GetValue<int?>("Smtp:Port") ?? configuration.GetValue<int?>("Email:Port") ?? 587;
        var username = configuration["Smtp:Username"] ?? configuration["Email:Username"];
        var password = (configuration["Smtp:Password"] ?? configuration["Email:Password"])?.Replace(" ", string.Empty, StringComparison.Ordinal);
        var from = configuration["Smtp:From"] ?? configuration["Email:From"] ?? username;
        if (string.IsNullOrWhiteSpace(from)) throw new InvalidOperationException("Configure Smtp:From or Email:Username when SMTP is enabled.");

        var safeLink = WebUtility.HtmlEncode(link);
        var actionText = subject == "Подтвердите email" ? "Подтвердить email" : "Сбросить пароль";
        var eyebrow = subject == "Подтвердите email" ? "ЗАЩИТА АККАУНТА" : "ВОССТАНОВЛЕНИЕ ДОСТУПА";
        using var message = new MailMessage
        {
            From = new MailAddress(from, "OrgBoard"),
            Subject = subject,
            Body = $"""
                <!doctype html><html lang="ru"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width"></head>
                <body style="margin:0;padding:0;background:#f1f5fa;font-family:Segoe UI,Arial,sans-serif;color:#15233b">
                <div style="display:none;max-height:0;overflow:hidden;opacity:0">{WebUtility.HtmlEncode(description)}</div>
                <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" style="background:#f1f5fa;padding:34px 12px">
                  <tr><td align="center">
                    <table role="presentation" width="600" cellspacing="0" cellpadding="0" border="0" style="width:100%;max-width:600px;border-collapse:separate;border-spacing:0;background:#ffffff;border:1px solid #dce5ef;border-radius:18px;overflow:hidden">
                      <tr><td style="padding:26px 34px;background:#1e293b;background-image:linear-gradient(120deg,#1e293b,#2c4059);color:#ffffff">
                        <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0"><tr>
                          <td style="font-size:24px;font-weight:750;letter-spacing:-.4px">OrgBoard<span style="color:#77c514">.</span></td>
                          <td align="right"><span style="display:inline-block;padding:6px 10px;border-radius:20px;background:#34452a;color:#a5e66b;font-size:10px;font-weight:700;letter-spacing:1px">КОМАНДА ПОД КОНТРОЛЕМ</span></td>
                        </tr></table>
                      </td></tr>
                      <tr><td style="height:5px;background:#63ad00;font-size:0;line-height:0">&nbsp;</td></tr>
                      <tr><td style="padding:38px 40px 16px">
                        <div style="margin-bottom:12px;color:#579800;font-size:11px;font-weight:700;letter-spacing:1.5px">{eyebrow}</div>
                        <h1 style="margin:0 0 14px;font-size:27px;line-height:1.25;color:#15233b">{WebUtility.HtmlEncode(subject)}</h1>
                        <p style="margin:0;color:#63738a;font-size:15px;line-height:1.7">{WebUtility.HtmlEncode(description)}</p>
                      </td></tr>
                      <tr><td align="left" style="padding:12px 40px 28px">
                        <a href="{safeLink}" style="display:inline-block;padding:14px 24px;border-radius:9px;background:#63ad00;color:#ffffff;text-decoration:none;font-size:15px;font-weight:700">{actionText} &nbsp; &#8594;</a>
                      </td></tr>
                      <tr><td style="padding:0 40px"><div style="height:1px;background:#e7edf4"></div></td></tr>
                      <tr><td style="padding:20px 40px 30px;color:#8290a2;font-size:12px;line-height:1.7">
                        Ссылка действует один час и может быть использована один раз.<br>
                        Если вы не запрашивали это действие, просто проигнорируйте письмо.
                      </td></tr>
                    </table>
                    <p style="margin:18px 0 0;color:#98a4b3;font-size:11px">Автоматическое письмо от OrgBoard · Управление организацией</p>
                  </td></tr>
                </table></body></html>
                """,
            IsBodyHtml = true
        };
        message.To.Add(email);
        using var client = new SmtpClient(host, port)
        {
            EnableSsl = configuration.GetValue("Smtp:EnableSsl", configuration.GetValue("Email:EnableSsl", true)),
            Credentials = string.IsNullOrWhiteSpace(username)
                ? CredentialCache.DefaultNetworkCredentials
                : new NetworkCredential(username, password)
        };
        cancellationToken.ThrowIfCancellationRequested();
        await client.SendMailAsync(message, cancellationToken);
    }
}
