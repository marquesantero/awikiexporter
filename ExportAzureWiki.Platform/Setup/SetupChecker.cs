using Microsoft.Win32;

namespace ExportAzureWiki.Platform.Setup;

public static class SetupChecker
{
    private const string RegistryKeyPath = @"Software\ExportAzureWiki";
    private const string SetupCompleteValueName = "SetupComplete";

    public static bool IsFirstRun()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
            if (key == null)
            {
                return true;
            }

            var setupComplete = key.GetValue(SetupCompleteValueName);
            if (setupComplete == null)
            {
                return true;
            }

            if (setupComplete is int intValue)
            {
                return intValue != 1;
            }

            if (setupComplete is string strValue)
            {
                return strValue != "1" && !strValue.Equals("true", StringComparison.OrdinalIgnoreCase);
            }

            return true;
        }
        catch
        {
            return true;
        }
    }

    public static bool MarkSetupComplete()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
            key?.SetValue(SetupCompleteValueName, 1, RegistryValueKind.DWord);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsDatabaseConfigured()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
            if (key == null)
            {
                return false;
            }

            var connectionString = key.GetValue("ConnectionString");
            var databaseType = key.GetValue("DatabaseType");
            return connectionString != null && databaseType != null;
        }
        catch
        {
            return false;
        }
    }
}
