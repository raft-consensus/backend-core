namespace raft_backend.DTOs;

public class DatabaseInstanceCreateDto
{
    public int UserId { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string DatabaseName { get; set; } = string.Empty;
    public string DatabaseUser { get; set; } = string.Empty;
    public string Engine { get; set; } = "SQL Server";
    public string Status { get; set; } = "Active";
    public long UsedSpaceBytes { get; set; }
    public long MaxSpaceBytes { get; set; } = 20 * 1024 * 1024;
    public DateTime? LastActivity { get; set; }
}

public class DatabaseInstanceUpdateDto
{
    public int UserId { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string DatabaseName { get; set; } = string.Empty;
    public string DatabaseUser { get; set; } = string.Empty;
    public string Engine { get; set; } = "SQL Server";
    public string Status { get; set; } = "Active";
    public long UsedSpaceBytes { get; set; }
    public long MaxSpaceBytes { get; set; } = 20 * 1024 * 1024;
    public DateTime? LastActivity { get; set; }
}

public class DatabaseInstanceReadDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string DatabaseName { get; set; } = string.Empty;
    public string DatabaseUser { get; set; } = string.Empty;
    public string Engine { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public long UsedSpaceBytes { get; set; }
    public long MaxSpaceBytes { get; set; }
    public DateTime? LastActivity { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}
