using System.Security.Cryptography;

namespace raft_backend.Services;

// Alphabet deliberately excludes quotes, backslash, @, :, / and ; — characters that could
// break a MySQL IDENTIFIED BY literal, a displayed connection string, or a URI, without
// needing any escaping logic downstream.
public class SecurePasswordGenerator : ISecurePasswordGenerator
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!#%+-_=";

    public string Generate(int length)
    {
        if (length < 16)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Generated passwords must be at least 16 characters long.");
        }

        var chars = new char[length];
        var randomBytes = RandomNumberGenerator.GetBytes(length);

        for (var i = 0; i < length; i++)
        {
            chars[i] = Alphabet[randomBytes[i] % Alphabet.Length];
        }

        return new string(chars);
    }
}
