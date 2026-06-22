namespace ExportAzureWiki.Models;

public sealed class CacheRunStats
{
    public int Hits { get; set; }
    public int Misses { get; set; }
    public int Regenerated { get; set; }
    public int OfflineMisses { get; set; }
    public bool OfflineMode { get; set; }
    public int TotalRequested { get; set; }

    public string ToStatusText()
    {
        var baseText = $"Cache H:{Hits} M:{Misses} R:{Regenerated}";
        if (OfflineMode)
        {
            return $"{baseText} OfflineMiss:{OfflineMisses}";
        }

        return baseText;
    }
}

