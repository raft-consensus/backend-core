namespace raft_backend.DTOs;

public class DatabaseProvisioningRequestDto
{
    public string Engine { get; set; } = "SQL Server";
}

public class DatabaseProvisioningResultDto
{
    public int DatabaseInstanceId { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string DatabaseName { get; set; } = string.Empty;
    public string DatabaseUser { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Engine { get; set; } = "SQL Server";
}

public class SqlServerProvisioningResultDto : DatabaseProvisioningResultDto
{
}

public class MySqlProvisioningResultDto : DatabaseProvisioningResultDto
{
}

public class PostgresProvisioningResultDto : DatabaseProvisioningResultDto
{
}

public class SqlServerSharedProvisioningStateDto
{
    public string SharedLoginName { get; set; } = string.Empty;
    public bool HasExistingDatabases { get; set; }
    public string? EncryptedPassword { get; set; }
}

public class SqlServerSharedLoginCleanupStateDto
{
    public string SharedLoginName { get; set; } = string.Empty;
    public bool CanDropLogin { get; set; }
}
