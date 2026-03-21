using Microsoft.Extensions.Configuration;
using OpenDeepWiki.Agents;

namespace OpenDeepWiki.Services.Wiki;

public static class WikiGeneratorOptionsConfigurator
{
    public static void Apply(WikiGeneratorOptions options, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configuration);

        var defaultAiOptions = ResolveDefaultAiOptions(configuration);

        options.CatalogModel = ResolveStringValue(
            configuration,
            "WIKI_CATALOG_MODEL",
            $"{WikiGeneratorOptions.SectionName}:CatalogModel",
            options.CatalogModel) ?? options.CatalogModel;
        options.CatalogEndpoint = ResolveStringValue(
            configuration,
            "WIKI_CATALOG_ENDPOINT",
            $"{WikiGeneratorOptions.SectionName}:CatalogEndpoint",
            defaultAiOptions.Endpoint ?? options.CatalogEndpoint);
        options.CatalogApiKey = ResolveStringValue(
            configuration,
            "WIKI_CATALOG_API_KEY",
            $"{WikiGeneratorOptions.SectionName}:CatalogApiKey",
            defaultAiOptions.ApiKey ?? options.CatalogApiKey);
        options.CatalogRequestType = ResolveRequestType(
            configuration,
            "WIKI_CATALOG_REQUEST_TYPE",
            $"{WikiGeneratorOptions.SectionName}:CatalogRequestType",
            defaultAiOptions.RequestType,
            options.CatalogRequestType);

        options.ContentModel = ResolveStringValue(
            configuration,
            "WIKI_CONTENT_MODEL",
            $"{WikiGeneratorOptions.SectionName}:ContentModel",
            options.ContentModel) ?? options.ContentModel;
        options.ContentEndpoint = ResolveStringValue(
            configuration,
            "WIKI_CONTENT_ENDPOINT",
            $"{WikiGeneratorOptions.SectionName}:ContentEndpoint",
            defaultAiOptions.Endpoint ?? options.ContentEndpoint);
        options.ContentApiKey = ResolveStringValue(
            configuration,
            "WIKI_CONTENT_API_KEY",
            $"{WikiGeneratorOptions.SectionName}:ContentApiKey",
            defaultAiOptions.ApiKey ?? options.ContentApiKey);
        options.ContentRequestType = ResolveRequestType(
            configuration,
            "WIKI_CONTENT_REQUEST_TYPE",
            $"{WikiGeneratorOptions.SectionName}:ContentRequestType",
            defaultAiOptions.RequestType,
            options.ContentRequestType);

        options.TranslationModel = ResolveStringValue(
            configuration,
            "WIKI_TRANSLATION_MODEL",
            $"{WikiGeneratorOptions.SectionName}:TranslationModel",
            options.TranslationModel);
        options.TranslationEndpoint = ResolveStringValue(
            configuration,
            "WIKI_TRANSLATION_ENDPOINT",
            $"{WikiGeneratorOptions.SectionName}:TranslationEndpoint",
            options.ContentEndpoint ?? options.TranslationEndpoint);
        options.TranslationApiKey = ResolveStringValue(
            configuration,
            "WIKI_TRANSLATION_API_KEY",
            $"{WikiGeneratorOptions.SectionName}:TranslationApiKey",
            options.ContentApiKey ?? options.TranslationApiKey);
        options.TranslationRequestType = ResolveRequestType(
            configuration,
            "WIKI_TRANSLATION_REQUEST_TYPE",
            $"{WikiGeneratorOptions.SectionName}:TranslationRequestType",
            options.ContentRequestType,
            options.TranslationRequestType);

        options.Languages = ResolveStringValue(
            configuration,
            "WIKI_LANGUAGES",
            $"{WikiGeneratorOptions.SectionName}:Languages",
            options.Languages);
    }

    private static AiRequestOptions ResolveDefaultAiOptions(IConfiguration configuration)
    {
        var options = configuration.GetSection("AI").Get<AiRequestOptions>() ?? new AiRequestOptions();

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            options.ApiKey = configuration["CHAT_API_KEY"];
        }

        if (string.IsNullOrWhiteSpace(options.Endpoint))
        {
            options.Endpoint = configuration["ENDPOINT"];
        }

        if (!options.RequestType.HasValue)
        {
            options.RequestType = TryParseRequestType(configuration["CHAT_REQUEST_TYPE"]);
        }

        return options;
    }

    private static string? ResolveStringValue(
        IConfiguration configuration,
        string overrideKey,
        string sectionKey,
        string? fallbackValue)
    {
        return !string.IsNullOrWhiteSpace(configuration[overrideKey])
            ? configuration[overrideKey]
            : !string.IsNullOrWhiteSpace(configuration[sectionKey])
                ? configuration[sectionKey]
                : fallbackValue;
    }

    private static AiRequestType? ResolveRequestType(
        IConfiguration configuration,
        string overrideKey,
        string sectionKey,
        AiRequestType? fallbackValue,
        AiRequestType? currentValue)
    {
        return TryParseRequestType(configuration[overrideKey])
            ?? TryParseRequestType(configuration[sectionKey])
            ?? fallbackValue
            ?? currentValue;
    }

    private static AiRequestType? TryParseRequestType(string? requestType)
    {
        if (string.IsNullOrWhiteSpace(requestType))
        {
            return null;
        }

        return Enum.TryParse<AiRequestType>(requestType, true, out var parsed)
            ? parsed
            : null;
    }
}
