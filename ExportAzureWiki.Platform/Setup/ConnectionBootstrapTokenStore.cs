using System.Security.Cryptography;
using System.Text;

namespace ExportAzureWiki.Platform.Setup;

public sealed class ConnectionBootstrapTokenStore
{
    private readonly string _tokenFilePath;

    public ConnectionBootstrapTokenStore()
    {
        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ExportAzureWiki");
        Directory.CreateDirectory(baseDir);
        _tokenFilePath = Path.Combine(baseDir, "connection.bootstrap.token");
    }

    public void Save(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("Token cannot be empty.", nameof(token));
        }

        var raw = Encoding.UTF8.GetBytes(token.Trim());
        var protectedData = ProtectedData.Protect(raw, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_tokenFilePath, protectedData);
    }

    public bool TryLoad(out string token)
    {
        token = string.Empty;

        try
        {
            if (!File.Exists(_tokenFilePath))
            {
                return false;
            }

            var protectedData = File.ReadAllBytes(_tokenFilePath);
            if (protectedData.Length == 0)
            {
                return false;
            }

            var raw = ProtectedData.Unprotect(protectedData, null, DataProtectionScope.CurrentUser);
            token = Encoding.UTF8.GetString(raw).Trim();
            return !string.IsNullOrWhiteSpace(token);
        }
        catch
        {
            return false;
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_tokenFilePath))
            {
                File.Delete(_tokenFilePath);
            }
        }
        catch
        {
            // best effort
        }
    }
}
