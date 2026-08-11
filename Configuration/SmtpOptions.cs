namespace raft_backend.Configuration;

public class SmtpOptions
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromEmail { get; set; } = "noreply@raft.andrescortes.dev";
    public string FromName { get; set; } = "Raft DB Platform";
    public bool EnableSmtp { get; set; } = true;
}
