using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;

namespace OpenDeepWiki.Chat.Config;

/// <summary>
/// Chat 配置服务实现
/// 支持数据库配置存储、敏感配置加密和热重载
/// </summary>
public class ChatConfigService : IChatConfigService
{
    private readonly IContext _context;
    private readonly IConfigEncryption _encryption;
    private readonly IConfigChangeNotifier _changeNotifier;
    private readonly ILogger<ChatConfigService> _logger;
    private readonly List<Action<string>> _changeCallbacks = new();
    private readonly object _callbackLock = new();

    // 必需的配置字段（按平台）
    private static readonly Dictionary<string, string[]> RequiredFields = new()
    {
        ["feishu"] = new[] { "AppId", "AppSecret" },
        ["qq"] = new[] { "AppId", "Token" },
        ["wechat"] = new[] { "AppId", "AppSecret", "Token", "EncodingAesKey" },
        ["slack"] = new[] { "BotToken", "SigningSecret" }
    };
    
    public ChatConfigService(
        IContext context,
        IConfigEncryption encryption,
        IConfigChangeNotifier changeNotifier,
        ILogger<ChatConfigService> logger)
    {
        _context = context;
        _encryption = encryption;
        _changeNotifier = changeNotifier;
        _logger = logger;
    }
    
    /// <inheritdoc />
    public async Task<ProviderConfigDto?> GetConfigAsync(string platform, CancellationToken cancellationToken = default)
    {
        var entity = await _context.ChatProviderConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Platform == platform, cancellationToken);
            
        if (entity == null)
            return null;
            
        return MapToDto(entity);
    }
    
    /// <inheritdoc />
    public async Task<IEnumerable<ProviderConfigDto>> GetAllConfigsAsync(CancellationToken cancellationToken = default)
    {
        var entities = await _context.ChatProviderConfigs
            .AsNoTracking()
            .ToListAsync(cancellationToken);
            
        return entities.Select(MapToDto);
    }
    
    /// <inheritdoc />
    public async Task SaveConfigAsync(ProviderConfigDto config, CancellationToken cancellationToken = default)
    {
        var existing = await _context.ChatProviderConfigs
            .FirstOrDefaultAsync(c => c.Platform == config.Platform, cancellationToken);
            
        if (existing == null)
        {
            // 新增
            var entity = new ChatProviderConfig
            {
                Id = Guid.NewGuid(),
                Platform = config.Platform,
                DisplayName = config.DisplayName,
                IsEnabled = config.IsEnabled,
                ConfigData = _encryption.Encrypt(config.ConfigData),
                WebhookUrl = config.WebhookUrl,
                MessageInterval = config.MessageInterval,
                MaxRetryCount = config.MaxRetryCount,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.ChatProviderConfigs.Add(entity);
            _logger.LogInformation("Created new config for platform: {Platform}", config.Platform);
        }
        else
        {
            // 更新
            existing.DisplayName = config.DisplayName;
            existing.IsEnabled = config.IsEnabled;
            existing.ConfigData = _encryption.Encrypt(config.ConfigData);
            existing.WebhookUrl = config.WebhookUrl;
            existing.MessageInterval = config.MessageInterval;
            existing.MaxRetryCount = config.MaxRetryCount;
            existing.UpdatedAt = DateTime.UtcNow;
            _logger.LogInformation("Updated config for platform: {Platform}", config.Platform);
        }
        
        await _context.SaveChangesAsync(cancellationToken);
        
        // 触发变更通知
        NotifyConfigChanged(config.Platform);
    }
    
    /// <inheritdoc />
    public async Task DeleteConfigAsync(string platform, CancellationToken cancellationToken = default)
    {
        var entity = await _context.ChatProviderConfigs
            .FirstOrDefaultAsync(c => c.Platform == platform, cancellationToken);
            
        if (entity != null)
        {
            _context.ChatProviderConfigs.Remove(entity);
            await _context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Deleted config for platform: {Platform}", platform);

            // 触发变更通知
            NotifyConfigChanged(platform, ConfigChangeType.Deleted);
        }
    }
    
    /// <inheritdoc />
    public ConfigValidationResult ValidateConfig(ProviderConfigDto config)
    {
        var errors = new List<string>();
        var missingFields = new List<string>();
        
        // 验证基本字段
        if (string.IsNullOrWhiteSpace(config.Platform))
        {
            errors.Add("Platform is required");
            missingFields.Add("Platform");
        }
        
        if (string.IsNullOrWhiteSpace(config.DisplayName))
        {
            errors.Add("DisplayName is required");
            missingFields.Add("DisplayName");
        }
        
        if (config.MessageInterval < 0)
        {
            errors.Add("MessageInterval must be non-negative");
        }
        
        if (config.MaxRetryCount < 0)
        {
            errors.Add("MaxRetryCount must be non-negative");
        }
        
        // 验证平台特定的必需字段
        if (!string.IsNullOrWhiteSpace(config.Platform) && !string.IsNullOrWhiteSpace(config.ConfigData))
        {
            var requiredFields = GetRequiredFieldsForPlatform(config.Platform);
            var configJson = ParseConfigData(config.ConfigData);
            
            foreach (var field in requiredFields)
            {
                if (!configJson.ContainsKey(field) || string.IsNullOrWhiteSpace(configJson[field]?.ToString()))
                {
                    errors.Add($"Required field '{field}' is missing or empty in ConfigData");
                    missingFields.Add(field);
                }
            }
        }
        
        return new ConfigValidationResult
        {
            Platform = config.Platform,
            IsValid = errors.Count == 0,
            Errors = errors,
            MissingFields = missingFields
        };
    }
    
    /// <inheritdoc />
    public async Task<IEnumerable<ConfigValidationResult>> ValidateAllConfigsAsync(CancellationToken cancellationToken = default)
    {
        var configs = await GetAllConfigsAsync(cancellationToken);
        return configs.Select(ValidateConfig);
    }
    
    /// <inheritdoc />
    public IDisposable OnConfigChanged(Action<string> callback)
    {
        lock (_callbackLock)
        {
            _changeCallbacks.Add(callback);
        }
        
        return new CallbackDisposable(() =>
        {
            lock (_callbackLock)
            {
                _changeCallbacks.Remove(callback);
            }
        });
    }
    
    /// <inheritdoc />
    public async Task ReloadConfigAsync(string? platform = null, CancellationToken cancellationToken = default)
    {
        if (platform != null)
        {
            _logger.LogInformation("Reloading config for platform: {Platform}", platform);
            NotifyConfigChanged(platform);
        }
        else
        {
            _logger.LogInformation("Reloading all configs");
            var configs = await GetAllConfigsAsync(cancellationToken);
            foreach (var config in configs)
            {
                NotifyConfigChanged(config.Platform);
            }
        }
    }
    
    /// <summary>
    /// 将实体映射为 DTO（解密配置数据）
    /// </summary>
    private ProviderConfigDto MapToDto(ChatProviderConfig entity)
    {
        return new ProviderConfigDto
        {
            Platform = entity.Platform,
            DisplayName = entity.DisplayName,
            IsEnabled = entity.IsEnabled,
            ConfigData = _encryption.Decrypt(entity.ConfigData),
            WebhookUrl = entity.WebhookUrl,
            MessageInterval = entity.MessageInterval,
            MaxRetryCount = entity.MaxRetryCount
        };
    }
    
    /// <summary>
    /// 获取平台的必需字段
    /// </summary>
    private static string[] GetRequiredFieldsForPlatform(string platform)
    {
        var normalizedPlatform = platform.ToLowerInvariant();
        return RequiredFields.TryGetValue(normalizedPlatform, out var fields) 
            ? fields 
            : Array.Empty<string>(); // 自定义平台不要求特定字段
    }
    
    /// <summary>
    /// 解析配置数据 JSON
    /// </summary>
    private static Dictionary<string, object?> ParseConfigData(string configData)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(configData) 
                   ?? new Dictionary<string, object?>();
        }
        catch
        {
            return new Dictionary<string, object?>();
        }
    }
    
    /// <summary>
    /// 通知配置变更
    /// </summary>
    private void NotifyConfigChanged(string platform, ConfigChangeType changeType = ConfigChangeType.Updated)
    {
        // Fire local scoped callbacks (existing behavior)
        List<Action<string>> callbacks;
        lock (_callbackLock)
        {
            callbacks = _changeCallbacks.ToList();
        }

        foreach (var callback in callbacks)
        {
            try
            {
                callback(platform);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in config change callback for platform: {Platform}", platform);
            }
        }

        // Fire global singleton notifier so ProviderConfigApplicator picks up changes immediately
        try
        {
            _changeNotifier.NotifyChange(platform, changeType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error firing global config change notification for platform: {Platform}", platform);
        }
    }
    
    /// <summary>
    /// 回调取消注册辅助类
    /// </summary>
    private class CallbackDisposable : IDisposable
    {
        private readonly Action _disposeAction;
        private bool _disposed;
        
        public CallbackDisposable(Action disposeAction)
        {
            _disposeAction = disposeAction;
        }
        
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposeAction();
                _disposed = true;
            }
        }
    }
}
