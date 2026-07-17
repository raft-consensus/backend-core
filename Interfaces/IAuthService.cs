using System.Security.Claims;
using raft_backend.DTOs;

namespace raft_backend.Interfaces;

public interface IAuthService
{
    Task<AuthResponseDto?> CompleteExternalLoginAsync(string provider, ClaimsPrincipal principal, CancellationToken cancellationToken = default);
}
