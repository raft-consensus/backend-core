namespace raft_backend.Interfaces;

public interface IAuthRecoveryEmailService
{
    Task SendTemporaryPasswordAsync(
        string email,
        string name,
        string temporaryPassword,
        DateTime expiresAt,
        CancellationToken cancellationToken = default);
}
