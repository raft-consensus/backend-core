using System.Text.Json;
using raft_backend.DTOs;

namespace raft_backend.Interfaces;

public interface IAiService
{
    Task<AiGenerateResponseDto?> GenerateAsync(string apiKeySecret, AiGenerateRequestDto request, CancellationToken cancellationToken = default);
    Task<JsonElement?> ProxyOpenAiChatCompletionAsync(string apiKeySecret, JsonElement requestPayload, string? preferredProvider = null, CancellationToken cancellationToken = default);
}

