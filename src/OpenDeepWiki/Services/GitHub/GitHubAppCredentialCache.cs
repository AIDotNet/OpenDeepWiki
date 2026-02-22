using OpenDeepWiki.Services.Admin;

namespace OpenDeepWiki.Services.GitHub;

/// <summary>
/// Thread-safe singleton cache for GitHub App credentials loaded from the database.
/// Checked first by GitHubAppService before falling back to env vars/config.
/// </summary>
public class GitHubAppCredentialCache
{
    private readonly object _lock = new();
    private string? _appId;
    private string? _privateKeyBase64;
    private string? _appName;

    public string? AppId
    {
        get { lock (_lock) return _appId; }
    }

    public string? PrivateKeyBase64
    {
        get { lock (_lock) return _privateKeyBase64; }
    }

    public string? AppName
    {
        get { lock (_lock) return _appName; }
    }

    /// <summary>
    /// Update all cached credentials atomically.
    /// </summary>
    public void Update(string? appId, string? privateKeyBase64, string? appName)
    {
        lock (_lock)
        {
            _appId = string.IsNullOrWhiteSpace(appId) ? null : appId;
            _privateKeyBase64 = string.IsNullOrWhiteSpace(privateKeyBase64) ? null : privateKeyBase64;
            _appName = string.IsNullOrWhiteSpace(appName) ? null : appName;
        }
    }

    /// <summary>
    /// Clear all cached credentials atomically.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _appId = null;
            _privateKeyBase64 = null;
            _appName = null;
        }
    }

    /// <summary>
    /// Load credentials from the SystemSettings database table.
    /// Called at startup and after saving new credentials via the admin UI.
    /// </summary>
    public async Task LoadFromDbAsync(IAdminSettingsService settingsService)
    {
        var appId = await settingsService.GetSettingByKeyAsync("GITHUB_APP_ID");
        var privateKey = await settingsService.GetSettingByKeyAsync("GITHUB_APP_PRIVATE_KEY");
        var appName = await settingsService.GetSettingByKeyAsync("GITHUB_APP_NAME");

        Update(appId?.Value, privateKey?.Value, appName?.Value);
    }
}
