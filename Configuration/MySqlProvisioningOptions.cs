using System.ComponentModel.DataAnnotations;

namespace raft_backend.Configuration;

public class MySqlProvisioningOptions
{
    [Required, Url]
    public string BaseUrl { get; set; } = "https://api.aba.andrescortes.dev";

    [Required]
    public string ApiKey { get; set; } = string.Empty;

    [Range(1, 120)]
    public int RequestTimeoutSeconds { get; set; } = 30;

    [Required]
    public string PublicHost { get; set; } = "db.aba.andrescortes.dev";

    [Range(1, 65535)]
    public int PublicPort { get; set; } = 3306;

    [Range(1, long.MaxValue)]
    public long DefaultMaxSpaceBytes { get; set; } = 20 * 1024 * 1024;

    [Range(16, 256)]
    public int PasswordLength { get; set; } = 24;

    [Range(1, 100)]
    public int MaxDatabasesPerUser { get; set; } = 3;
}
