using Microsoft.Extensions.Configuration;
using OpenDeepWiki.Agents;
using OpenDeepWiki.Services.Wiki;
using Xunit;

namespace OpenDeepWiki.Tests.Services.Wiki;

public class WikiGeneratorOptionsConfiguratorTests
{
    [Fact]
    public void Apply_ShouldFallbackToGlobalAiSettings_WhenWikiSpecificSettingsAreMissing()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["AI:Endpoint"] = "https://ark.example/v1",
            ["AI:ApiKey"] = "global-key",
            ["CHAT_REQUEST_TYPE"] = "OpenAI"
        });

        var options = new WikiGeneratorOptions();

        WikiGeneratorOptionsConfigurator.Apply(options, configuration);

        Assert.Equal("https://ark.example/v1", options.CatalogEndpoint);
        Assert.Equal("https://ark.example/v1", options.ContentEndpoint);
        Assert.Equal("https://ark.example/v1", options.TranslationEndpoint);
        Assert.Equal("global-key", options.CatalogApiKey);
        Assert.Equal("global-key", options.ContentApiKey);
        Assert.Equal("global-key", options.TranslationApiKey);
        Assert.Equal(AiRequestType.OpenAI, options.CatalogRequestType);
        Assert.Equal(AiRequestType.OpenAI, options.ContentRequestType);
        Assert.Equal(AiRequestType.OpenAI, options.TranslationRequestType);
    }

    [Fact]
    public void Apply_ShouldPreferWikiSpecificOverrides_OverGlobalAiFallbacks()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["AI:Endpoint"] = "https://global.example/v1",
            ["AI:ApiKey"] = "global-key",
            ["CHAT_REQUEST_TYPE"] = "OpenAI",
            ["WikiGenerator:CatalogEndpoint"] = "https://catalog.example/v1",
            ["WIKI_CONTENT_API_KEY"] = "content-key",
            ["WIKI_TRANSLATION_ENDPOINT"] = "https://translation.example/v1",
            ["WIKI_TRANSLATION_API_KEY"] = "translation-key",
            ["WIKI_TRANSLATION_REQUEST_TYPE"] = "Anthropic"
        });

        var options = new WikiGeneratorOptions();

        WikiGeneratorOptionsConfigurator.Apply(options, configuration);

        Assert.Equal("https://catalog.example/v1", options.CatalogEndpoint);
        Assert.Equal("global-key", options.CatalogApiKey);
        Assert.Equal("https://global.example/v1", options.ContentEndpoint);
        Assert.Equal("content-key", options.ContentApiKey);
        Assert.Equal("https://translation.example/v1", options.TranslationEndpoint);
        Assert.Equal("translation-key", options.TranslationApiKey);
        Assert.Equal(AiRequestType.Anthropic, options.TranslationRequestType);
    }

    private static IConfiguration BuildConfiguration(IEnumerable<KeyValuePair<string, string?>> settings)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
    }
}
