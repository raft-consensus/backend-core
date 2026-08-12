namespace raft_backend.Models;

public class AiUsageLog
{
    public long Id { get; set; }
    public int AiApiKeyId { get; set; }
    public int UserId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string? Mode { get; set; }
    public long PromptTokens { get; set; }
    public long CompletionTokens { get; set; }
    public long TotalTokens { get; set; }
    public decimal ApproxCostUsd { get; set; }
    public int? DurationMs { get; set; }
    public int StatusCode { get; set; } = 200;
    public DateTime Created_at { get; set; }

    public AiApiKey? AiApiKey { get; set; }
    public User? User { get; set; }
}
