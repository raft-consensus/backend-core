namespace raft_backend.DTOs;

public class AccessCredentialCreateDto
{
    public int DatabaseInstanceId { get; set; }
    public string EncryptedPassword { get; set; } = string.Empty;
}

public class AccessCredentialUpdateDto
{
    public string EncryptedPassword { get; set; } = string.Empty;
}

public class AccessCredentialReadDto
{
    public int Id { get; set; }
    public int DatabaseInstanceId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}
