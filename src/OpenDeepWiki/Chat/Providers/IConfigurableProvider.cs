using OpenDeepWiki.Chat.Config;

namespace OpenDeepWiki.Chat.Providers;

/// <summary>
/// Interface for providers that support runtime configuration updates from database.
/// Providers implementing this interface can have their config applied/reverted
/// by the ProviderConfigApplicator hosted service.
/// </summary>
public interface IConfigurableProvider
{
    /// <summary>
    /// Apply configuration from database DTO.
    /// Thread-safe: implementations must ensure concurrent requests see a consistent state.
    /// </summary>
    void ApplyConfig(ProviderConfigDto config);

    /// <summary>
    /// Reset to environment-variable-based configuration (when DB config is deleted).
    /// </summary>
    void ResetToDefaults();

    /// <summary>
    /// Where the current config came from: "database" or "environment".
    /// </summary>
    string ConfigSource { get; }
}
