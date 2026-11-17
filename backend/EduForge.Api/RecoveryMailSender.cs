using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
namespace EduForge.Api;
public interface IRecoveryMailSender
{
    bool IsConfigured { get; }
    Task SendAsync(string recipient, string link, CancellationToken ct);
}
public sealed class RecoveryMailSender(IConfiguration config) : IRecoveryMailSender
{
    string? Setting(string name) => config["Smtp__" + name] ?? config["Smtp:" + name];
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Setting("Host"))
        && MailAddress.TryCreate(Setting("From"), out _)
        && !string.IsNullOrWhiteSpace(Setting("User"))
        && !string.IsNullOrWhiteSpace(Setting("Password"));
    public async Task SendAsync(string recipient, string link, CancellationToken ct)
    {
        if (!IsConfigured) throw new SmtpException("SMTP is not configured.");
        var port = int.TryParse(Setting("Port"), out var value) ? value : 587;
        if (port is < 1 or > 65535 || port == 465) throw new SmtpException("Use a STARTTLS SMTP port, usually 587.");
        var password = Setting("Password")!;
        if (string.Equals(Setting("Host"), "smtp.gmail.com", StringComparison.OrdinalIgnoreCase))
            password = password.Replace(" ", "");
        using var message = new MailMessage {
            From = new MailAddress(Setting("From")!, "EduForge AI"),
            Subject = "EduForge AI: восстановление пароля",
            Body = "Откройте ссылку для смены пароля (действует 30 минут): " + link
                + "\nЕсли вы не запрашивали восстановление, проигнорируйте письмо.",
            BodyEncoding = System.Text.Encoding.UTF8,
            SubjectEncoding = System.Text.Encoding.UTF8
        };
        message.To.Add(recipient);
        using var smtp = new SmtpClient(Setting("Host"), port) {
            EnableSsl = true, UseDefaultCredentials = false,
            Credentials = new NetworkCredential(Setting("User"), password)
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        await smtp.SendMailAsync(message, timeout.Token);
    }
}
