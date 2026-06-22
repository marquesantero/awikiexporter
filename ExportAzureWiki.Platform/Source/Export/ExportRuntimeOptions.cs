namespace ExportAzureWiki;

/// <summary>
/// Per-export runtime switches that cross the UI/Platform boundary without
/// changing the export service signatures. Set on the calling async context
/// just before an export and reset afterwards. <see cref="System.Threading.AsyncLocal{T}"/>
/// flows the value through the export's await chain.
/// </summary>
public static class ExportRuntimeOptions
{
    private static readonly System.Threading.AsyncLocal<bool> _offlineImagesOnly = new();

    /// <summary>
    /// When true, the export uses only already-cached remote images and never
    /// hits the network; missing images are skipped. Keeps exports reproducible
    /// and private/offline.
    /// </summary>
    public static bool OfflineImagesOnly
    {
        get => _offlineImagesOnly.Value;
        set => _offlineImagesOnly.Value = value;
    }
}
