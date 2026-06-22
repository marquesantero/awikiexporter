using System.Security.Cryptography;
using System.Text;
using ExportAzureWiki.Core.Authentication;

namespace ExportAzureWiki.Services;

/// <summary>
/// Service for hashing and verifying passwords using PBKDF2
/// </summary>
public class PasswordHashingService
{
    private const int SaltSize = 32; // 256 bits
    private const int HashSize = 32; // 256 bits
    private const int Iterations = 100000; // OWASP recommendation

    /// <summary>
    /// Hashes a password and generates a salt
    /// </summary>
    /// <param name="password">Plain text password</param>
    /// <returns>Tuple containing the hash and salt as base64 strings</returns>
    public (string hash, string salt) HashPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Password cannot be empty", nameof(password));

        // Generate a random salt
        var salt = GenerateSalt();

        // Hash the password
        var hash = HashPasswordWithSalt(password, salt);

        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    /// <summary>
    /// Verifies a password against a stored hash and salt
    /// </summary>
    /// <param name="password">Plain text password to verify</param>
    /// <param name="storedHash">Stored password hash (base64)</param>
    /// <param name="storedSalt">Stored salt (base64)</param>
    /// <returns>True if password matches, false otherwise</returns>
    public bool VerifyPassword(string password, string storedHash, string storedSalt)
    {
        if (string.IsNullOrWhiteSpace(password))
            return false;

        if (string.IsNullOrWhiteSpace(storedHash) || string.IsNullOrWhiteSpace(storedSalt))
            return false;

        try
        {
            var salt = Convert.FromBase64String(storedSalt);
            var hash = Convert.FromBase64String(storedHash);

            var computedHash = HashPasswordWithSalt(password, salt);

            // Use constant-time comparison to prevent timing attacks
            return CryptographicOperations.FixedTimeEquals(hash, computedHash);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Validates a password against the supplied policy. When no policy is
    /// supplied, <see cref="PasswordPolicy.Default"/> is used so existing
    /// call sites stay source-compatible.
    /// </summary>
    public bool ValidatePasswordStrength(string password, PasswordPolicy? policy = null)
    {
        return (policy ?? PasswordPolicy.Default).IsSatisfiedBy(password);
    }

    /// <summary>
    /// Returns the localized rule that the candidate violates, or null
    /// when the candidate passes. Useful in the admin UI so the user
    /// sees the specific failing rule instead of a generic message.
    /// </summary>
    public string? GetPolicyViolation(string password, PasswordPolicy? policy = null)
    {
        return (policy ?? PasswordPolicy.Default).FirstViolation(password);
    }

    /// <summary>
    /// Gets password strength requirements as a user-friendly message
    /// </summary>
    /// <returns>Password requirements description</returns>
    public string GetPasswordRequirements()
    {
        return @"A senha deve conter:
- Mínimo de 8 caracteres
- Pelo menos uma letra maiúscula (A-Z)
- Pelo menos uma letra minúscula (a-z)
- Pelo menos um número (0-9)
- Pelo menos um caractere especial (!@#$%^&*()_+-=[]{}|;:,.<>?)";
    }

    private byte[] GenerateSalt()
    {
        var salt = new byte[SaltSize];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(salt);
        }
        return salt;
    }

    private byte[] HashPasswordWithSalt(string password, byte[] salt)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(
            password,
            salt,
            Iterations,
            HashAlgorithmName.SHA256);

        return pbkdf2.GetBytes(HashSize);
    }
}
