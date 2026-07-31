namespace raft_backend.Configuration;

public class SqlServerProvisioningOptions
{
    public string PublicHost { get; set; } = "49.13.85.216";
    public int PublicPort { get; set; } = 1433;
    public long DefaultMaxSpaceBytes { get; set; } = 20 * 1024 * 1024;
    public int PasswordLength { get; set; } = 24;
    public int MaxDatabasesPerUser { get; set; } = 3;
}
