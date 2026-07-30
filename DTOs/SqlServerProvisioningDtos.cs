namespace raft_backend.DTOs;

public class DatabaseProvisioningRequestDto
{
    public string Engine { get; set; } = "SQL Server";
}

public class SqlServerProvisioningResultDto
{
    public int DatabaseInstanceId { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string DatabaseName { get; set; } = string.Empty;
    public string DatabaseUser { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Engine { get; set; } = "SQL Server";
}
