using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using OpenDeepWiki.Agents;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Services.Wiki;

namespace OpenDeepWiki.Services.Admin;

/// <summary>
/// Helper class for initializing default system settings from environment variables.
/// </summary>
public static class SystemSettingDefaults
{
    /// <summary>
    /// Default settings for the wiki generator.
    /// </summary>
    public static readonly (string Key, string Category, string Description)[] WikiGeneratorDefaults =
    [
        ("WIKI_CATALOG_MODEL", "ai", "AI model used for catalog generation"),
        ("WIKI_CATALOG_ENDPOINT", "ai", "API endpoint for catalog generation"),
        ("WIKI_CATALOG_API_KEY", "ai", "API key for catalog generation"),
        ("WIKI_CATALOG_REQUEST_TYPE", "ai", "Request type for catalog generation"),
        ("WIKI_CONTENT_MODEL", "ai", "AI model used for content generation"),
        ("WIKI_CONTENT_ENDPOINT", "ai", "API endpoint for content generation"),
        ("WIKI_CONTENT_API_KEY", "ai", "API key for content generation"),
        ("WIKI_CONTENT_REQUEST_TYPE", "ai", "Request type for content generation"),
        ("WIKI_TRANSLATION_MODEL", "ai", "AI model used for translation"),
        ("WIKI_TRANSLATION_ENDPOINT", "ai", "API endpoint for translation"),
        ("WIKI_TRANSLATION_API_KEY", "ai", "API key for translation"),
        ("WIKI_TRANSLATION_REQUEST_TYPE", "ai", "Request type for translation"),
        ("WIKI_LANGUAGES", "ai", "Supported languages (comma-separated)"),
        ("WIKI_PARALLEL_COUNT", "ai", "Number of parallel document generation tasks"),
        ("WIKI_MAX_OUTPUT_TOKENS", "ai", "Maximum output token count"),
        ("WIKI_DOCUMENT_GENERATION_TIMEOUT_MINUTES", "ai", "Document generation timeout (minutes)"),
        ("WIKI_TRANSLATION_TIMEOUT_MINUTES", "ai", "Translation timeout (minutes)"),
        ("WIKI_TITLE_TRANSLATION_TIMEOUT_MINUTES", "ai", "Title translation timeout (minutes)"),
        ("WIKI_README_MAX_LENGTH", "ai", "Maximum README content length"),
        ("WIKI_DIRECTORY_TREE_MAX_DEPTH", "ai", "Maximum directory tree depth"),
        ("WIKI_MAX_RETRY_ATTEMPTS", "ai", "Maximum retry attempts"),
        ("WIKI_RETRY_DELAY_MS", "ai", "Retry delay (milliseconds)"),
        ("WIKI_PROMPTS_DIRECTORY", "ai", "Prompt templates directory")
    ];

    /// <summary>
    /// Initialize default system settings (only for keys not already in the database).
    /// </summary>
    public static async Task InitializeDefaultsAsync(IConfiguration configuration, IContext context)
    {
        var existingSettings = await context.SystemSettings
            .Where(s => !s.IsDeleted)
            .ToListAsync();

        var existingByKey = existingSettings.ToDictionary(s => s.Key);

        var settingsToAdd = new List<SystemSetting>();
        var hasChanges = false;

        // Prepare WikiGeneratorOptions defaults so values can be written even without env vars
        var wikiOptionDefaults = new WikiGeneratorOptions();
        var wikiSection = configuration.GetSection(WikiGeneratorOptions.SectionName);
        wikiSection.Bind(wikiOptionDefaults);

        // Apply environment variable overrides to match runtime behavior
        ApplyEnvironmentOverrides(wikiOptionDefaults, configuration);

        // Process wiki generator settings
        foreach (var (key, category, description) in WikiGeneratorDefaults)
        {
            if (existingByKey.TryGetValue(key, out var existing))
            {
                // Update description if it changed (e.g. translated from Chinese to English)
                if (existing.Description != description)
                {
                    existing.Description = description;
                    hasChanges = true;
                }
            }
            else
            {
                var envValue = GetEnvironmentOrConfigurationValue(configuration, key);
                var fallbackValue = GetOptionDefaultValue(wikiOptionDefaults, key);
                var valueToUse = envValue ?? fallbackValue ?? string.Empty;

                settingsToAdd.Add(new SystemSetting
                {
                    Id = Guid.NewGuid().ToString(),
                    Key = key,
                    Value = valueToUse,
                    Description = description,
                    Category = category,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        if (settingsToAdd.Count > 0)
        {
            context.SystemSettings.AddRange(settingsToAdd);
            hasChanges = true;
        }

        if (hasChanges)
        {
            await context.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Get value from environment variable or configuration.
    /// </summary>
    private static string? GetEnvironmentOrConfigurationValue(IConfiguration configuration, string key)
    {
        // Prefer environment variable
        var envValue = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrWhiteSpace(envValue))
        {
            return envValue;
        }

        // Fall back to configuration (appsettings.json etc.)
        return configuration[key];
    }

    /// <summary>
    /// Apply environment variable values to WikiGeneratorOptions for runtime consistency.
    /// </summary>
    private static void ApplyEnvironmentOverrides(WikiGeneratorOptions options, IConfiguration configuration)
    {
        foreach (var (key, _, _) in WikiGeneratorDefaults)
        {
            var envValue = GetEnvironmentOrConfigurationValue(configuration, key);
            if (!string.IsNullOrWhiteSpace(envValue))
            {
                ApplySettingToOption(options, key, envValue);
            }
        }
    }

    /// <summary>
    /// Apply system settings to WikiGeneratorOptions.
    /// </summary>
    public static async Task ApplyToWikiGeneratorOptions(WikiGeneratorOptions options, IAdminSettingsService settingsService)
    {
        foreach (var def in WikiGeneratorDefaults)
        {
            var setting = await settingsService.GetSettingByKeyAsync(def.Key);
            if (setting?.Value != null)
            {
                ApplySettingToOption(options, def.Key, setting.Value);
            }
        }
    }

    /// <summary>
    /// Get the default value from WikiGeneratorOptions as a string.
    /// </summary>
    private static string? GetOptionDefaultValue(WikiGeneratorOptions options, string key)
    {
        return key switch
        {
            "WIKI_CATALOG_MODEL" => options.CatalogModel,
            "WIKI_CATALOG_ENDPOINT" => options.CatalogEndpoint,
            "WIKI_CATALOG_API_KEY" => options.CatalogApiKey,
            "WIKI_CATALOG_REQUEST_TYPE" => options.CatalogRequestType?.ToString(),
            "WIKI_CONTENT_MODEL" => options.ContentModel,
            "WIKI_CONTENT_ENDPOINT" => options.ContentEndpoint,
            "WIKI_CONTENT_API_KEY" => options.ContentApiKey,
            "WIKI_CONTENT_REQUEST_TYPE" => options.ContentRequestType?.ToString(),
            "WIKI_TRANSLATION_MODEL" => options.TranslationModel,
            "WIKI_TRANSLATION_ENDPOINT" => options.TranslationEndpoint,
            "WIKI_TRANSLATION_API_KEY" => options.TranslationApiKey,
            "WIKI_TRANSLATION_REQUEST_TYPE" => options.TranslationRequestType?.ToString(),
            "WIKI_LANGUAGES" => options.Languages,
            "WIKI_PARALLEL_COUNT" => options.ParallelCount.ToString(),
            "WIKI_MAX_OUTPUT_TOKENS" => options.MaxOutputTokens.ToString(),
            "WIKI_DOCUMENT_GENERATION_TIMEOUT_MINUTES" => options.DocumentGenerationTimeoutMinutes.ToString(),
            "WIKI_TRANSLATION_TIMEOUT_MINUTES" => options.TranslationTimeoutMinutes.ToString(),
            "WIKI_TITLE_TRANSLATION_TIMEOUT_MINUTES" => options.TitleTranslationTimeoutMinutes.ToString(),
            "WIKI_README_MAX_LENGTH" => options.ReadmeMaxLength.ToString(),
            "WIKI_DIRECTORY_TREE_MAX_DEPTH" => options.DirectoryTreeMaxDepth.ToString(),
            "WIKI_MAX_RETRY_ATTEMPTS" => options.MaxRetryAttempts.ToString(),
            "WIKI_RETRY_DELAY_MS" => options.RetryDelayMs.ToString(),
            "WIKI_PROMPTS_DIRECTORY" => options.PromptsDirectory,
            _ => null
        };
    }

    /// <summary>
    /// Apply a single setting to WikiGeneratorOptions.
    /// </summary>
    public static void ApplySettingToOption(WikiGeneratorOptions options, string key, string value)
    {
        switch (key)
        {
            case "WIKI_CATALOG_MODEL":
                options.CatalogModel = value;
                break;
            case "WIKI_CATALOG_ENDPOINT":
                options.CatalogEndpoint = value;
                break;
            case "WIKI_CATALOG_API_KEY":
                options.CatalogApiKey = value;
                break;
            case "WIKI_CATALOG_REQUEST_TYPE" when Enum.TryParse<AiRequestType>(value, true, out var catalogType):
                options.CatalogRequestType = catalogType;
                break;
            case "WIKI_CONTENT_MODEL":
                options.ContentModel = value;
                break;
            case "WIKI_CONTENT_ENDPOINT":
                options.ContentEndpoint = value;
                break;
            case "WIKI_CONTENT_API_KEY":
                options.ContentApiKey = value;
                break;
            case "WIKI_CONTENT_REQUEST_TYPE" when Enum.TryParse<AiRequestType>(value, true, out var contentType):
                options.ContentRequestType = contentType;
                break;
            case "WIKI_TRANSLATION_MODEL":
                options.TranslationModel = value;
                break;
            case "WIKI_TRANSLATION_ENDPOINT":
                options.TranslationEndpoint = value;
                break;
            case "WIKI_TRANSLATION_API_KEY":
                options.TranslationApiKey = value;
                break;
            case "WIKI_TRANSLATION_REQUEST_TYPE" when Enum.TryParse<AiRequestType>(value, true, out var translationType):
                options.TranslationRequestType = translationType;
                break;
            case "WIKI_LANGUAGES":
                options.Languages = value;
                break;
            case "WIKI_PARALLEL_COUNT" when int.TryParse(value, out var parallelCount):
                options.ParallelCount = parallelCount;
                break;
            case "WIKI_MAX_OUTPUT_TOKENS" when int.TryParse(value, out var maxTokens):
                options.MaxOutputTokens = maxTokens;
                break;
            case "WIKI_DOCUMENT_GENERATION_TIMEOUT_MINUTES" when int.TryParse(value, out var docTimeout):
                options.DocumentGenerationTimeoutMinutes = docTimeout;
                break;
            case "WIKI_TRANSLATION_TIMEOUT_MINUTES" when int.TryParse(value, out var transTimeout):
                options.TranslationTimeoutMinutes = transTimeout;
                break;
            case "WIKI_TITLE_TRANSLATION_TIMEOUT_MINUTES" when int.TryParse(value, out var titleTimeout):
                options.TitleTranslationTimeoutMinutes = titleTimeout;
                break;
            case "WIKI_README_MAX_LENGTH" when int.TryParse(value, out var readmeLength):
                options.ReadmeMaxLength = readmeLength;
                break;
            case "WIKI_DIRECTORY_TREE_MAX_DEPTH" when int.TryParse(value, out var treeDepth):
                options.DirectoryTreeMaxDepth = treeDepth;
                break;
            case "WIKI_MAX_RETRY_ATTEMPTS" when int.TryParse(value, out var retryAttempts):
                options.MaxRetryAttempts = retryAttempts;
                break;
            case "WIKI_RETRY_DELAY_MS" when int.TryParse(value, out var retryDelay):
                options.RetryDelayMs = retryDelay;
                break;
            case "WIKI_PROMPTS_DIRECTORY":
                options.PromptsDirectory = value;
                break;
        }
    }
}
