using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using raft_backend.Configuration;
using raft_backend.Interfaces;

namespace raft_backend.Services;

public class AuthRecoveryEmailService : IAuthRecoveryEmailService
{
    private readonly SmtpOptions _smtpOptions;
    private readonly FrontendOptions _frontendOptions;
    private readonly ILogger<AuthRecoveryEmailService> _logger;

    public AuthRecoveryEmailService(
        IOptions<SmtpOptions> smtpOptions,
        IOptions<FrontendOptions> frontendOptions,
        ILogger<AuthRecoveryEmailService> logger)
    {
        _smtpOptions = smtpOptions.Value;
        _frontendOptions = frontendOptions.Value;
        _logger = logger;
    }

    public async Task SendPasswordResetEmailAsync(
        string email,
        string name,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        var subject = "Recuperación de Contraseña - Raft DB Platform";
        var htmlBody = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <style>
        body {{ font-family: 'Segoe UI', Arial, sans-serif; background-color: #0f172a; color: #f8fafc; padding: 20px; }}
        .card {{ max-width: 500px; margin: 0 auto; background-color: #1e293b; border-radius: 12px; padding: 32px; border: 1px solid #334155; }}
        .header {{ text-align: center; margin-bottom: 24px; }}
        .title {{ color: #38bdf8; font-size: 24px; font-weight: bold; margin: 0; }}
        .password-box {{ background-color: #0f172a; border: 1px dashed #38bdf8; border-radius: 8px; padding: 16px; text-align: center; font-size: 22px; font-weight: bold; letter-spacing: 2px; color: #38bdf8; margin: 24px 0; }}
        .btn {{ display: inline-block; background-color: #0284c7; color: #ffffff; text-decoration: none; padding: 12px 24px; border-radius: 8px; font-weight: bold; text-align: center; }}
        .footer {{ margin-top: 24px; text-align: center; font-size: 12px; color: #94a3b8; }}
    </style>
</head>
<body>
    <div class='card'>
        <div class='header'>
            <h2 class='title'>Raft DB Platform</h2>
        </div>
        <p>Hola <strong>{WebUtility.HtmlEncode(name)}</strong>,</p>
        <p>Hemos procesado tu solicitud para restablecer tu contraseña. Tu nueva contraseña de acceso es:</p>
        
        <div class='password-box'>{WebUtility.HtmlEncode(newPassword)}</div>
        
        <p>Puedes usar esta contraseña para iniciar sesión en tu cuenta inmediatamente.</p>
        <p style='text-align: center; margin-top: 24px;'>
            <a href='{_frontendOptions.Origin}' class='btn'>Iniciar Sesión</a>
        </p>
        <div class='footer'>
            <p>Si no solicitaste este cambio, puedes actualizar tu contraseña desde la sección de configuración de tu cuenta.</p>
        </div>
    </div>
</body>
</html>";

        // Si Smtp no está habilitado o no tiene Host configurado, no exponemos la contraseña
        // temporal en logs; solo dejamos trazabilidad del fallo de entrega.
        if (!_smtpOptions.EnableSmtp || string.IsNullOrWhiteSpace(_smtpOptions.Host))
        {
            _logger.LogWarning("SMTP disabled or unconfigured. Password reset email was not sent to {Email} ({Name}).", email, name);
            return;
        }

        try
        {
            using var mailMessage = new MailMessage
            {
                From = new MailAddress(_smtpOptions.FromEmail, _smtpOptions.FromName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };
            mailMessage.To.Add(new MailAddress(email, name));

            using var smtpClient = new SmtpClient(_smtpOptions.Host, _smtpOptions.Port)
            {
                EnableSsl = _smtpOptions.EnableSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false
            };

            if (!string.IsNullOrWhiteSpace(_smtpOptions.UserName))
            {
                smtpClient.Credentials = new NetworkCredential(_smtpOptions.UserName, _smtpOptions.Password);
            }

            await smtpClient.SendMailAsync(mailMessage, cancellationToken);
            _logger.LogInformation("Password reset email sent successfully to {Email}", email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send password reset email via SMTP to {Email}.", email);
            // No bloqueamos el flujo principal si el servidor SMTP falla, la clave ya fue cambiada en la BD.
        }
    }
}
