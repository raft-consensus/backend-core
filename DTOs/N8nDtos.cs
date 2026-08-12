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
    public string? Credential { get; set; }
    public string? AccessType { get; set; }
    public int ActiveWorkflowsCount { get; set; }
    public int TotalWorkflowsCount { get; set; }
    public long TotalExecutions { get; set; }
    public long SuccessfulExecutions { get; set; }
    public long FailedExecutions { get; set; }
    public int MonthlyExecutions { get; set; }
    public int MaxMonthlyExecutions { get; set; }
    public DateTime? MonthlyResetDate { get; set; }
    public DateTime? LastExecutionAt { get; set; }
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
    public string? AccessType { get; set; }
    public string? Credential { get; set; }
}

public class N8nProvisioningResponseDto
{
    public bool Created { get; set; }
    public N8nAccountReadDto Account { get; set; } = new();
    public string? AccessType { get; set; }
    public string? Credential { get; set; }
}
