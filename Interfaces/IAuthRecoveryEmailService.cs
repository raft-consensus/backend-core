namespace raft_backend.Interfaces;

public interface IAuthRecoveryEmailService
{
    Task SendPasswordResetEmailAsync(
        string email,
        string name,
        string newPassword,
        CancellationToken cancellationToken = default);

    Task SendTemporaryPasswordAsync(
        string email,
        string name,
        string temporaryPassword,
        DateTime expiresAt,
        CancellationToken cancellationToken = default) => SendPasswordResetEmailAsync(email, name, temporaryPassword, cancellationToken);
}
