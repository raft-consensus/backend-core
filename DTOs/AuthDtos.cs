using System.ComponentModel.DataAnnotations;

namespace raft_backend.DTOs;

public class RegisterDto
{
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required, EmailAddress, MaxLength(320)]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(8)]
    public string Password { get; set; } = string.Empty;
}

public class LoginDto
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}

public class RequestTemporaryPasswordDto
{
    [Required, EmailAddress, MaxLength(320)]
    public string Email { get; set; } = string.Empty;
}

public class ChangePasswordDto
{
    [Required, MinLength(8)]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required, MinLength(8)]
    public string NewPassword { get; set; } = string.Empty;
}

public class SetLocalPasswordDto
{
    [Required, MinLength(8)]
    public string Password { get; set; } = string.Empty;
}

public class AuthResponseDto
{
    public string AccessToken { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Bearer";
    public DateTime ExpiresAt { get; set; }
    public string Provider { get; set; } = string.Empty;
    public UserReadDto User { get; set; } = new();
}
