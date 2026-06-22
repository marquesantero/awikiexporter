using Serilog.Core;
using Serilog.Events;

namespace ExportAzureWiki.Platform.Logging;

/// <summary>
/// Replaces any property whose name matches a known secret-bearing label
/// (Password, Token, PAT, ClientSecret, ApiKey, ...) with a fixed mask
/// before the log event is written to any sink.
///
/// This is the single chokepoint that protects the rolling file sink and
/// the Trace sink from ever recording a credential, even when a caller
/// writes <c>Log.Information("login {Username} {Password}", u, p)</c>
/// or attaches a token-bearing object via destructuring. It does NOT
/// rewrite the message template or the raw arguments dictionary -- both
/// are left as-is for diagnostics -- but every property the sinks see is
/// either safe or "***".
/// </summary>
public sealed class SensitivePropertyEnricher : ILogEventEnricher
{
    private const string Mask = "***";

    private static readonly HashSet<string> SensitiveNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Password",
        "Pat",
        "PersonalAccessToken",
        "Token",
        "AccessToken",
        "RefreshToken",
        "IdToken",
        "ClientSecret",
        "Secret",
        "ApiKey",
        "ApiToken",
        "Authorization",
        "EncryptedSession",
    };

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(propertyFactory);

        var sensitiveKeys = logEvent.Properties.Keys
            .Where(SensitiveNames.Contains)
            .ToList();

        foreach (var key in sensitiveKeys)
        {
            logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty(key, Mask));
        }
    }

    /// <summary>
    /// Exposed for tests so the policy stays in sync with what's actually
    /// recognized at runtime.
    /// </summary>
    internal static IReadOnlyCollection<string> RecognizedNames => SensitiveNames;
}
