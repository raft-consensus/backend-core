namespace raft_backend.Configuration;

public class MySqlProvisioningOptions
{
    public string PublicHost { get; set; } = string.Empty;
    public int PublicPort { get; set; } = 3306;
    public int DefaultMaxUserConnections { get; set; } = 5;
    public long DefaultMaxSpaceBytes { get; set; } = 20 * 1024 * 1024;
    public int PasswordLength { get; set; } = 24;

    // Ceiling for self-service creation via POST /api/me/databases. The automatic
    // provisioning on first login (AuthService) is exempt from this check — a brand-new
    // user always has zero instances, so the limit can never block that path.
    public int MaxDatabasesPerUser { get; set; } = 3;
}
