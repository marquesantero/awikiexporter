using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExportAzureWiki.Models;

namespace ExportAzureWiki.Platform.Setup;

public sealed class ConnectionBootstrapTokenService
{
    private const string Purpose = "ExportAzureWiki.ConnectionBootstrap.v1";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly byte[] Key = SHA256.HashData(Encoding.UTF8.GetBytes(Purpose));

    public string CreateToken(DatabaseConfiguration configuration, DateTime? expiresAtUtc = null)
    {
        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        var payload = new ConnectionTokenPayload
        {
            Version = 1,
            IssuedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = expiresAtUtc,
            Configuration = configuration
        };

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        Span<byte> nonce = stackalloc byte[12];
        RandomNumberGenerator.Fill(nonce);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];

        using (var aes = new AesGcm(Key, 16))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag);
        }

        var packed = new byte[nonce.Length + tag.Length + ciphertext.Length];
        nonce.CopyTo(packed.AsSpan(0, nonce.Length));
        tag.CopyTo(packed.AsSpan(nonce.Length, tag.Length));
        ciphertext.CopyTo(packed.AsSpan(nonce.Length + tag.Length));

        return Convert.ToBase64String(packed)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    public bool TryReadToken(string token, out DatabaseConfiguration? configuration, out string error)
    {
        configuration = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(token))
        {
            error = "Empty token.";
            return false;
        }

        try
        {
            var normalized = token.Trim().Replace('-', '+').Replace('_', '/');
            var padding = normalized.Length % 4;
            if (padding != 0)
            {
                normalized = normalized.PadRight(normalized.Length + (4 - padding), '=');
            }

            var packed = Convert.FromBase64String(normalized);
            if (packed.Length < 28)
            {
                error = "Invalid token.";
                return false;
            }

            var nonce = packed.AsSpan(0, 12);
            var tag = packed.AsSpan(12, 16);
            var ciphertext = packed.AsSpan(28);
            var plaintext = new byte[ciphertext.Length];

            using (var aes = new AesGcm(Key, 16))
            {
                aes.Decrypt(nonce, ciphertext, tag, plaintext);
            }

            var payload = JsonSerializer.Deserialize<ConnectionTokenPayload>(plaintext, JsonOptions);
            if (payload?.Configuration == null)
            {
                error = "Token has no configuration.";
                return false;
            }

            if (payload.ExpiresAtUtc.HasValue && payload.ExpiresAtUtc.Value < DateTime.UtcNow)
            {
                error = "Token expired.";
                return false;
            }

            configuration = payload.Configuration;
            return true;
        }
        catch (Exception ex)
        {
            error = $"Invalid token: {ex.Message}";
            return false;
        }
    }

    private sealed class ConnectionTokenPayload
    {
        public int Version { get; set; }
        public DateTime IssuedAtUtc { get; set; }
        public DateTime? ExpiresAtUtc { get; set; }
        public DatabaseConfiguration? Configuration { get; set; }
    }
}
