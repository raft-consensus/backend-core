using System.Security.Claims;
using raft_backend.DTOs;

namespace raft_backend.Interfaces;

public interface IAuthService
{
    Task<AuthResponseDto?> CompleteExternalLoginAsync(string provider, ClaimsPrincipal principal, CancellationToken cancellationToken = default);
    Task<AuthResponseDto?> RegisterWithPasswordAsync(RegisterDto dto, CancellationToken cancellationToken = default);
    Task<AuthResponseDto?> LoginWithPasswordAsync(LoginDto dto, CancellationToken cancellationToken = default);
    Task<bool> RequestTemporaryPasswordAsync(RequestTemporaryPasswordDto dto, CancellationToken cancellationToken = default);
    Task<UserReadDto?> ChangePasswordAsync(int userId, ChangePasswordDto dto, CancellationToken cancellationToken = default);
    Task<UserReadDto?> SetLocalPasswordAsync(int userId, SetLocalPasswordDto dto, CancellationToken cancellationToken = default);
}
