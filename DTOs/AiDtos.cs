using System.ComponentModel.DataAnnotations;

namespace raft_backend.DTOs;

public class AiApiKeyCreateDto
{
    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;
}

public class AiApiKeyReadDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string KeyPrefix { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public long TotalRequests { get; set; }
    public long TotalPromptTokens { get; set; }
    public long TotalCompletionTokens { get; set; }
    public long TotalTokens { get; set; }
    public decimal ApproxCostUsd { get; set; }
}

public class AiApiKeySecretDto
{
    public AiApiKeyReadDto Key { get; set; } = new();
    public string Secret { get; set; } = string.Empty;
}

public class AiGenerateRequestDto
{
    [Required, MinLength(3)]
    public string Prompt { get; set; } = string.Empty;

    [MaxLength(80)]
    public string? Provider { get; set; }

    [MaxLength(40)]
    public string? Mode { get; set; }

    [MaxLength(4000)]
    public string? Context { get; set; }
}

public class AiGenerateResponseDto
{
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public int KeyId { get; set; }
    public string KeyPrefix { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public long PromptTokens { get; set; }
    public long CompletionTokens { get; set; }
    public long TotalTokens { get; set; }
    public decimal ApproxCostUsd { get; set; }
    public DateTime CreatedAt { get; set; }
}
