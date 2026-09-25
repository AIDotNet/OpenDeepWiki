using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;

namespace OpenDeepWiki.Services.AI;

public interface IAiProviderPresetSeeder
{
    Task EnsureBuiltInProvidersAsync(CancellationToken cancellationToken = default);
}

public sealed class AiProviderPresetSeeder : IAiProviderPresetSeeder
{
    private const string OpenCoworkIconBase = "https://unpkg.com/@lobehub/icons-static-png@1.83.0";

    private static readonly IReadOnlyDictionary<string, string> OpenCoworkProviderIconUrls =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["routin-ai"] = "https://routin.ai/icons/favicon.ico",
            ["routin-ai-plan"] = "https://routin.ai/icons/favicon.ico",
            ["copilot-oauth"] = "https://github.githubassets.com/favicons/favicon.png",
            ["orcarouter"] = "https://www.orcarouter.ai/favicon.ico",
            ["requesty"] = "https://www.requesty.ai/favicon.ico"
        };

    private static readonly IReadOnlyDictionary<string, string> OpenCoworkProviderIconSlugs =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["openai"] = "openai",
            ["anthropic"] = "anthropic",
            ["google"] = "google",
            ["deepseek"] = "deepseek",
            ["openrouter"] = "openrouter",
            ["ollama"] = "ollama",
            ["azure-openai"] = "azureai",
            ["moonshot"] = "moonshot",
            ["moonshot-coding"] = "moonshot",
            ["longcat"] = "longcat",
            ["qwen"] = "qwen",
            ["qwen-coding"] = "qwen",
            ["baidu"] = "baidu",
            ["baidu-coding"] = "baidu",
            ["minimax-coding"] = "minimax",
            ["minimax"] = "minimax",
            ["siliconflow"] = "siliconcloud",
            ["gitee-ai"] = "giteeai",
            ["codex-oauth"] = "openai",
            ["copilot-oauth"] = "github",
            ["xiaomi"] = "xiaomimimo",
            ["xiaomi-coding"] = "xiaomimimo",
            ["bigmodel-coding"] = "chatglm",
            ["bigmodel"] = "chatglm"
        };

    private static readonly HashSet<string> OpenCoworkColorIconSlugs =
    [
        "azureai",
        "baidu",
        "chatglm",
        "claude",
        "deepseek",
        "doubao",
        "gemini",
        "google",
        "hunyuan",
        "kimi",
        "meta",
        "minimax",
        "mistral",
        "nvidia",
        "qwen",
        "siliconcloud",
        "stepfun"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IContext _context;
    private readonly IAiProviderPresetCatalog _catalog;
    private readonly ILogger<AiProviderPresetSeeder> _logger;

    public AiProviderPresetSeeder(
        IContext context,
        IAiProviderPresetCatalog catalog,
        ILogger<AiProviderPresetSeeder> logger)
    {
        _context = context;
        _catalog = catalog;
        _logger = logger;
    }

    public async Task EnsureBuiltInProvidersAsync(CancellationToken cancellationToken = default)
    {
        var presets = _catalog.Presets;
        if (presets.Count == 0)
        {
            return;
        }

        var presetNames = presets.Select(p => p.BuiltinId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var providers = await _context.AiProviderConfigs
            .Where(p => presetNames.Contains(p.Name))
            .ToListAsync(cancellationToken);
        var providersByName = providers.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < presets.Count; index++)
        {
            var preset = presets[index];
            if (string.IsNullOrWhiteSpace(preset.BuiltinId))
            {
                continue;
            }

            if (!providersByName.TryGetValue(preset.BuiltinId, out var provider))
            {
                provider = CreateProvider(preset, index);
                _context.AiProviderConfigs.Add(provider);
                providersByName[preset.BuiltinId] = provider;
            }
            else
            {
                SyncProvider(provider, preset, index);
            }

            await SyncModelsAsync(provider, preset, cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Ensured {ProviderCount} OpenCowork built-in AI providers and {ModelCount} models",
            presets.Count,
            presets.Sum(p => p.DefaultModels.Count));
    }

    private static AiProviderConfig CreateProvider(AiProviderPreset preset, int index)
    {
        var provider = new AiProviderConfig
        {
            Id = Guid.NewGuid().ToString(),
            Name = preset.BuiltinId,
            CreatedAt = DateTime.UtcNow
        };
        SyncProvider(provider, preset, index, preserveOperationalFields: false);
        return provider;
    }

    private static void SyncProvider(
        AiProviderConfig provider,
        AiProviderPreset preset,
        int index,
        bool preserveOperationalFields = true)
    {
        provider.DisplayName = string.IsNullOrWhiteSpace(preset.Name) ? preset.BuiltinId : preset.Name;
        provider.ProviderType = NormalizeProviderType(preset);
        provider.IsBuiltIn = true;
        provider.IsDeleted = false;
        provider.DeletedAt = null;
        provider.AuthType = NormalizeAuthType(preset.AuthMode);
        provider.SupportsModelDiscovery = provider.AuthType.Equals("ApiKey", StringComparison.OrdinalIgnoreCase);
        provider.DefaultModelId = ResolveDefaultModelId(preset);
        provider.OAuthConfigJson = SerializeElement(preset.OAuthConfig);
        provider.ChannelConfigJson = SerializeElement(preset.ChannelConfig);
        provider.RequestOverridesJson = BuildProviderRequestOverridesJson(preset);
        provider.IconUrl = ResolveIconUrl(preset);
        provider.Description = BuildDescription(preset);
        provider.SortOrder = index * 10;

        if (!preserveOperationalFields || string.IsNullOrWhiteSpace(provider.BaseUrl))
        {
            provider.BaseUrl = (preset.DefaultBaseUrl ?? string.Empty).TrimEnd('/');
        }

        if (!preserveOperationalFields)
        {
            provider.ApiKey = null;
            provider.IsActive = preset.DefaultEnabled ?? false;
        }

        provider.UpdatedAt = DateTime.UtcNow;
    }

    private async Task SyncModelsAsync(
        AiProviderConfig provider,
        AiProviderPreset preset,
        CancellationToken cancellationToken)
    {
        var existingModels = await _context.AiModelConfigs
            .Where(m => m.ProviderId == provider.Id)
            .ToListAsync(cancellationToken);
        var modelsById = existingModels.ToDictionary(m => m.ModelId, StringComparer.OrdinalIgnoreCase);
        var presetModelIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < preset.DefaultModels.Count; index++)
        {
            var presetModel = preset.DefaultModels[index];
            if (string.IsNullOrWhiteSpace(presetModel.Id))
            {
                continue;
            }

            presetModelIds.Add(presetModel.Id);
            if (!modelsById.TryGetValue(presetModel.Id, out var model))
            {
                model = new AiModelConfig
                {
                    Id = Guid.NewGuid().ToString(),
                    ProviderId = provider.Id,
                    ModelId = presetModel.Id,
                    CreatedAt = DateTime.UtcNow
                };
                SyncModel(model, preset, presetModel, index, preserveOperationalFields: false);
                _context.AiModelConfigs.Add(model);
            }
            else
            {
                SyncModel(model, preset, presetModel, index);
            }
        }

        if (preset.DeprecatedModelIds is not { Count: > 0 })
        {
            return;
        }

        var deprecated = new HashSet<string>(preset.DeprecatedModelIds, StringComparer.OrdinalIgnoreCase);
        foreach (var model in existingModels)
        {
            if (!presetModelIds.Contains(model.ModelId) && deprecated.Contains(model.ModelId))
            {
                model.IsActive = false;
                model.UpdatedAt = DateTime.UtcNow;
            }
        }
    }

    private static void SyncModel(
        AiModelConfig model,
        AiProviderPreset preset,
        AiModelPreset presetModel,
        int index,
        bool preserveOperationalFields = true)
    {
        var modelType = NormalizeModelType(presetModel, preset);

        model.Name = string.IsNullOrWhiteSpace(presetModel.Name) ? presetModel.Id : presetModel.Name;
        model.IsDeleted = false;
        model.DeletedAt = null;
        model.DisplayName = string.IsNullOrWhiteSpace(presetModel.Name) ? presetModel.Id : presetModel.Name;
        model.ModelType = modelType;
        if (!preserveOperationalFields ||
            string.IsNullOrWhiteSpace(model.ProviderType))
        {
            model.ProviderType = NormalizeModelProviderType(presetModel, preset);
        }

        model.ContextWindow = presetModel.ContextLength;
        model.MaxOutputTokens = presetModel.MaxOutputTokens;
        model.InputTokenPrice = presetModel.InputPrice;
        model.OutputTokenPrice = presetModel.OutputPrice;
        model.CacheHitTokenPrice = presetModel.CacheHitPrice;
        model.CacheCreationTokenPrice = presetModel.CacheCreationPrice;
        model.SupportsThinking = presetModel.SupportsThinking ?? false;
        model.SupportsVision = presetModel.SupportsVision ?? modelType.Equals("image", StringComparison.OrdinalIgnoreCase);
        model.SupportsTools = presetModel.SupportsFunctionCall ?? modelType.Equals("chat", StringComparison.OrdinalIgnoreCase);
        model.SupportsJsonMode = model.SupportsTools && modelType.Equals("chat", StringComparison.OrdinalIgnoreCase);
        model.IsDefault = presetModel.Id.Equals(ResolveDefaultModelId(preset), StringComparison.OrdinalIgnoreCase);
        model.CapabilitiesJson = SerializeModelCapabilities(presetModel);
        model.ThinkingConfigJson = SerializeElement(presetModel.ThinkingConfig);
        model.RequestOverridesJson = SerializeElement(presetModel.RequestOverrides);
        model.TagsJson = Serialize(new
        {
            source = "OpenCowork",
            providerBuiltinId = preset.BuiltinId,
            providerType = preset.Type,
            modelType = presetModel.Type,
            category = presetModel.Category,
            supportsComputerUse = presetModel.SupportsComputerUse
        });
        model.Description = $"Built-in model from OpenCowork provider '{preset.Name}'.";
        model.SortOrder = index * 10;

        if (!preserveOperationalFields)
        {
            model.IsActive = presetModel.Enabled ?? true;
        }

        model.UpdatedAt = DateTime.UtcNow;
    }

    private static string NormalizeProviderType(AiProviderPreset preset)
    {
        if (preset.BuiltinId.Equals("azure-openai", StringComparison.OrdinalIgnoreCase))
        {
            return "AzureOpenAI";
        }

        return AiProviderResolver.NormalizeProviderType(preset.Type);
    }

    private static string NormalizeAuthType(string? authMode)
    {
        return authMode?.Trim().ToLowerInvariant() switch
        {
            "oauth" => "OAuth",
            "channel" => "Channel",
            _ => "ApiKey"
        };
    }

    private static string NormalizeModelType(AiModelPreset model, AiProviderPreset provider)
    {
        if (!string.IsNullOrWhiteSpace(model.Category))
        {
            return model.Category;
        }

        return (model.Type ?? provider.Type).Trim().ToLowerInvariant() switch
        {
            "openai-images" => "image",
            _ => "chat"
        };
    }

    private static string NormalizeModelProviderType(AiModelPreset model, AiProviderPreset provider)
    {
        return AiProviderResolver.NormalizeModelProviderType(
                   model.Type,
                   model.Id)
               ?? NormalizeProviderType(provider);
    }

    private static string? ResolveDefaultModelId(AiProviderPreset preset)
    {
        if (!string.IsNullOrWhiteSpace(preset.DefaultModel))
        {
            return preset.DefaultModel;
        }

        return preset.DefaultModels.FirstOrDefault(m => m.Enabled ?? true)?.Id
               ?? preset.DefaultModels.FirstOrDefault()?.Id;
    }

    private static string? ResolveIconUrl(AiProviderPreset preset)
    {
        if (preset.Ui is not { ValueKind: JsonValueKind.Object } ui ||
            !ui.TryGetProperty("iconUrl", out var iconUrl) ||
            iconUrl.ValueKind != JsonValueKind.String)
        {
            return ResolveOpenCoworkProviderIconUrl(preset.BuiltinId);
        }

        return iconUrl.GetString();
    }

    private static string? ResolveOpenCoworkProviderIconUrl(string builtinId)
    {
        if (OpenCoworkProviderIconUrls.TryGetValue(builtinId, out var customUrl))
        {
            return customUrl;
        }

        if (!OpenCoworkProviderIconSlugs.TryGetValue(builtinId, out var slug))
        {
            return null;
        }

        var fileName = OpenCoworkColorIconSlugs.Contains(slug) ? $"{slug}-color" : slug;
        return $"{OpenCoworkIconBase}/light/{fileName}.png";
    }

    private static string BuildDescription(AiProviderPreset preset)
    {
        var parts = new List<string> { "Built-in provider imported from OpenCowork." };
        if (!string.IsNullOrWhiteSpace(preset.Homepage))
        {
            parts.Add($"Homepage: {preset.Homepage}");
        }

        if (!string.IsNullOrWhiteSpace(preset.ApiKeyUrl))
        {
            parts.Add($"API key: {preset.ApiKeyUrl}");
        }

        parts.Add($"Requires API key: {preset.RequiresApiKey ?? true}");
        if (preset.UseSystemProxy.HasValue)
        {
            parts.Add($"Use system proxy: {preset.UseSystemProxy.Value}");
        }

        if (!string.IsNullOrWhiteSpace(preset.InstructionsPrompt))
        {
            parts.Add($"Instructions prompt: {preset.InstructionsPrompt}");
        }

        if (!string.IsNullOrWhiteSpace(preset.WebsocketUrl))
        {
            parts.Add($"WebSocket: {preset.WebsocketUrl}");
        }

        if (!string.IsNullOrWhiteSpace(preset.WebsocketMode))
        {
            parts.Add($"WebSocket mode: {preset.WebsocketMode}");
        }

        return string.Join(" ", parts);
    }

    private static string? BuildProviderRequestOverridesJson(AiProviderPreset preset)
    {
        var node = preset.RequestOverrides.HasValue
            ? JsonNode.Parse(preset.RequestOverrides.Value.GetRawText()) as JsonObject
            : null;

        if (string.IsNullOrWhiteSpace(preset.UserAgent))
        {
            return node?.ToJsonString(JsonOptions);
        }

        node ??= new JsonObject();
        var headers = node["headers"] as JsonObject;
        if (headers == null)
        {
            headers = new JsonObject();
            node["headers"] = headers;
        }

        headers["User-Agent"] ??= preset.UserAgent;
        return node.ToJsonString(JsonOptions);
    }

    private static string SerializeModelCapabilities(AiModelPreset model)
    {
        return Serialize(new
        {
            openCowork = model
        })!;
    }

    private static string? SerializeElement(JsonElement? element)
    {
        return element.HasValue ? element.Value.GetRawText() : null;
    }

    private static string? Serialize<T>(T value)
    {
        return value == null ? null : JsonSerializer.Serialize(value, JsonOptions);
    }
}
