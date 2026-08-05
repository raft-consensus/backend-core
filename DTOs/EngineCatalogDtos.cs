namespace raft_backend.DTOs;

public class EngineCatalogItemDto
{
    public string Name { get; set; } = string.Empty;
    public bool SupportedByThisCell { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}
