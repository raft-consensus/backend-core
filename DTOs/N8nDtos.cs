using System.ComponentModel.DataAnnotations;

namespace raft_backend.DTOs;

public class N8nAccountReadDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string ExternalUserRef { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? AccountId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? ProvisionedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public string? LastErrorMessage { get; set; }
}

public class N8nProvisioningResultDto
{
    public bool Created { get; set; }
    public N8nAccountReadDto Account { get; set; } = new();
}

public class N8nProvisioningResponseDto
{
    public bool Created { get; set; }
    public N8nAccountReadDto Account { get; set; } = new();
}
