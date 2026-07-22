namespace raft_backend.Configuration;

// Never change this string: it's the Data Protection "purpose" used to derive the key that
// encrypts/decrypts every AccessCredential.EncryptedPassword row. Changing it makes all
// previously-encrypted passwords permanently unrecoverable.
public static class DataProtectionPurposes
{
    public const string AccessCredentialPassword = "Raft.AccessCredentials.MySqlPassword.v1";
}
