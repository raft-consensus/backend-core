using raft_backend.DTOs;

namespace raft_backend.Interfaces;

public interface IAiService
{
    Task<AiGenerateResponseDto?> GenerateAsync(string apiKeySecret, AiGenerateRequestDto request, CancellationToken cancellationToken = default);
}
