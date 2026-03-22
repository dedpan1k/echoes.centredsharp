using System.Security.Cryptography;
using System.Text;

namespace CentrED.Utils;

/// <summary>
/// Provides reversible machine-local password obfuscation for persisted settings.
/// </summary>
public static class PasswordCrypter
{
    private static readonly string key = Environment.MachineName;

    // DES is used here as lightweight obfuscation tied to the local machine,
    // not as strong protection against a determined attacker.
    private static readonly DES des = DES.Create();
    
    static PasswordCrypter()
    {
        var key = Environment.MachineName;
        var newKey = new byte[8];
        Encoding.UTF8.GetBytes(key).AsSpan(0, Math.Min(key.Length, newKey.Length)).CopyTo(newKey);
        des.Key = newKey;
    }

    /// <summary>
    /// Encrypts a password string into a Base64-encoded machine-local value.
    /// </summary>
    /// <param name="password">The plaintext password to encrypt.</param>
    /// <returns>The encrypted password encoded as Base64.</returns>
    public static string Encrypt(string password)
    {
        return Convert.ToBase64String(des.EncryptEcb(Encoding.UTF8.GetBytes(password), PaddingMode.PKCS7));
    }

    /// <summary>
    /// Decrypts a Base64-encoded password produced by <see cref="Encrypt(string)"/>.
    /// </summary>
    /// <param name="password">The encrypted password text.</param>
    /// <returns>The decrypted plaintext password.</returns>
    public static string Decrypt(string password)
    {
        if (password.Length == 0)
        {
            return password;
        }
        return Encoding.UTF8.GetString(des.DecryptEcb(Convert.FromBase64String(password), PaddingMode.PKCS7));
    }
}