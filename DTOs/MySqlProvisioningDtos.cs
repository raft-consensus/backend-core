namespace raft_backend.DTOs;

// Plaintext password here is intentional and transient — only ever held in memory for the
// immediate response of the operation that produced it. It is never logged and never stored;
// AccessCredentials.EncryptedPassword holds the encrypted form.
public class MySqlProvisioningResultDto
{
    public int DatabaseInstanceId { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string DatabaseName { get; set; } = string.Empty;
    public string DatabaseUser { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Engine { get; set; } = "MySQL";
}

public class AccessCredentialRevealDto
{
    public int DatabaseInstanceId { get; set; }
    public string Password { get; set; } = string.Empty;
}
