namespace raft_backend.Models;

public class AiApiKey
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string KeyPrefix { get; set; } = string.Empty;
    public string KeyHash { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public DateTime Created_at { get; set; }
    public DateTime? Updated_at { get; set; }
    public DateTime? Revoked_at { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public long TotalRequests { get; set; }
    public long TotalPromptTokens { get; set; }
    public long TotalCompletionTokens { get; set; }
    public long TotalTokens { get; set; }
    public decimal ApproxCostUsd { get; set; }

    public User? User { get; set; }
}
