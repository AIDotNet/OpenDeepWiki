using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenDeepWiki.Chat.Providers;
using OpenDeepWiki.Chat.Routing;

namespace OpenDeepWiki.Chat.Config;

/// <summary>
/// Bridges database configuration to live provider instances.
/// On startup, loads DB config for all registered IConfigurableProvider providers.
/// At runtime, subscribes to IConfigChangeNotifier to apply config changes immediately.
/// </summary>
public class ProviderConfigApplicator : IHostedService, IDisposable
{
    private readonly IMessageRouter _router;
    private readonly IConfigChangeNotifier _changeNotifier;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ProviderConfigApplicator> _logger;
    private IDisposable? _subscription;

    public ProviderConfigApplicator(
        IMessageRouter router,
        IConfigChangeNotifier changeNotifier,
        IServiceScopeFactory scopeFactory,
        ILogger<ProviderConfigApplicator> logger)
    {
        _router = router;
        _changeNotifier = changeNotifier;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("ProviderConfigApplicator starting: applying DB configs to registered providers");

        await ApplyAllDbConfigsAsync(cancellationToken);

        // Subscribe to all platform changes for runtime hot-reload
        _subscription = _changeNotifier.Subscribe(null, OnConfigChanged);

        _logger.LogInformation("ProviderConfigApplicator started: subscribed to config change notifications");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _subscription?.Dispose();
        _subscription = null;
        _logger.LogInformation("ProviderConfigApplicator stopped");
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _subscription?.Dispose();
    }

    private async Task ApplyAllDbConfigsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var configService = scope.ServiceProvider.GetRequiredService<IChatConfigService>();

        foreach (var provider in _router.GetAllProviders())
        {
            if (provider is not IConfigurableProvider configurable)
                continue;

            try
            {
                var dbConfig = await configService.GetConfigAsync(provider.PlatformId, cancellationToken);
                if (dbConfig != null)
                {
                    configurable.ApplyConfig(dbConfig);
                    _logger.LogInformation(
                        "Applied DB config to provider {Platform} at startup", provider.PlatformId);
                }
                else
                {
                    _logger.LogDebug(
                        "No DB config for provider {Platform}, using environment variable defaults",
                        provider.PlatformId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to apply DB config to provider {Platform} at startup, falling back to env vars",
                    provider.PlatformId);
            }
        }
    }

    private void OnConfigChanged(ConfigChangeEvent evt)
    {
        // Fire-and-forget with error handling (notification callback is synchronous)
        _ = Task.Run(async () =>
        {
            try
            {
                await ApplyConfigForPlatformAsync(evt.Platform, evt.ChangeType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to apply config change for platform {Platform}", evt.Platform);
            }
        });
    }

    private async Task ApplyConfigForPlatformAsync(string platform, ConfigChangeType changeType)
    {
        var provider = _router.GetProvider(platform);
        if (provider is not IConfigurableProvider configurable)
        {
            _logger.LogDebug("Provider {Platform} does not support runtime config updates", platform);
            return;
        }

        if (changeType == ConfigChangeType.Deleted)
        {
            configurable.ResetToDefaults();
            _logger.LogInformation("Reset provider {Platform} to env var defaults (DB config deleted)", platform);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var configService = scope.ServiceProvider.GetRequiredService<IChatConfigService>();
        var dbConfig = await configService.GetConfigAsync(platform);

        if (dbConfig != null)
        {
            configurable.ApplyConfig(dbConfig);
            _logger.LogInformation("Applied updated DB config to provider {Platform} (change: {ChangeType})",
                platform, changeType);
        }
        else
        {
            configurable.ResetToDefaults();
            _logger.LogInformation("Reset provider {Platform} to env var defaults (DB config not found)", platform);
        }
    }
}
