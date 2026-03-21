using Microsoft.Extensions.Options;
using OpenDeepWiki.Agents;
using Xunit;

namespace OpenDeepWiki.Tests.Agents;

public class AgentFactoryTests
{
    [Fact]
    public void CreateSimpleChatClient_ShouldThrowHelpfulException_WhenApiKeyIsMissing()
    {
        var factory = new AgentFactory(Options.Create(new AiRequestOptions
        {
            Endpoint = "https://example.com/v1",
            RequestType = AiRequestType.OpenAI
        }));

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateSimpleChatClient("test-model"));

        Assert.Contains("AI API key", exception.Message);
    }
}
