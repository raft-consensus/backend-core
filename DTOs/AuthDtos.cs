namespace raft_backend.DTOs;

public class AuthResponseDto
{
    public string AccessToken { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Bearer";
    public DateTime ExpiresAt { get; set; }
    public string Provider { get; set; } = string.Empty;
    public UserReadDto User { get; set; } = new();
}
