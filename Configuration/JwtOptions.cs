using System.ComponentModel.DataAnnotations;

namespace raft_backend.Configuration;

public class JwtOptions
{
    [Required]
    public string Issuer { get; set; } = string.Empty;

    [Required]
    public string Audience { get; set; } = string.Empty;

    [Required, MinLength(32)]
    public string SigningKey { get; set; } = string.Empty;

    [Range(1, 24 * 60)]
    public int ExpirationMinutes { get; set; } = 60;
}
