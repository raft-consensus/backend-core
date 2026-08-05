namespace raft_backend.Configuration;

public class MySqlProvisioningOptions
{
    public string PublicHost { get; set; } = string.Empty;
    public int PublicPort { get; set; } = 3306;
    public long DefaultMaxSpaceBytes { get; set; } = 20 * 1024 * 1024;
    public int PasswordLength { get; set; } = 24;
}
