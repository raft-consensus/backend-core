namespace raft_backend.Models;

public class AccessCredential
{
    public int Id { get; set; }
    public int DatabaseInstanceId { get; set; }
    public string EncryptedPassword { get; set; } = string.Empty; // AES, no hash (necesitas descifrarla)
    public DateTime Created_at { get; set; }
    public DateTime? Updated_at { get; set; }
    public DateTime? Deleted_at { get; set; }

    public DatabaseInstance? DatabaseInstance { get; set; }
}
