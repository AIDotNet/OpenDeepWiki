using System.Text.Json.Nodes;
using OpenDeepWiki.Agents;
using Xunit;

namespace OpenDeepWiki.Tests.Agents;

/// <summary>
/// Unit tests for <see cref="ThoughtSignatureHandler"/>.
/// Exercises the internal helpers directly so no HTTP stack is required.
/// </summary>
public class ThoughtSignatureHandlerTests
{
    private static ThoughtSignatureHandler NewHandler() => new(new HttpClientHandler());

    /// <summary>One SSE data payload carrying a tool call and its signature.</summary>
    private static string StreamingChunk(string id, string signature, int index = 0) =>
        "{\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"tool_calls\":[" +
        "{\"index\":" + index + ",\"id\":\"" + id + "\",\"type\":\"function\"," +
        "\"function\":{\"name\":\"ReadFile\",\"arguments\":\"{}\"}," +
        "\"extra_content\":{\"google\":{\"thought_signature\":\"" + signature + "\"}}}]}}]}";

    private static JsonObject RequestWithToolCall(string id, bool withExtraContent = false)
    {
        var toolCall = new JsonObject
        {
            ["id"] = id,
            ["type"] = "function",
            ["function"] = new JsonObject { ["name"] = "ReadFile", ["arguments"] = "{}" }
        };

        if (withExtraContent)
        {
            toolCall["extra_content"] = new JsonObject
            {
                ["google"] = new JsonObject { ["thought_signature"] = "caller-supplied" }
            };
        }

        return new JsonObject
        {
            ["model"] = "gemini-3-pro-preview",
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "user", ["content"] = "read it" },
                new JsonObject
                {
                    ["role"] = "assistant",
                    ["content"] = null,
                    ["tool_calls"] = new JsonArray(toolCall)
                },
                new JsonObject { ["role"] = "tool", ["tool_call_id"] = id, ["content"] = "file body" })
        };
    }

    private static string? SignatureOf(JsonObject request, int messageIndex = 1, int toolCallIndex = 0) =>
        request["messages"]?[messageIndex]?["tool_calls"]?[toolCallIndex]?
            ["extra_content"]?["google"]?["thought_signature"]?.GetValue<string>();

    // ------------------------------------------------------------------
    // RecordSignatures - reading them off the response
    // ------------------------------------------------------------------

    [Fact]
    public void RecordSignatures_Reads_Streaming_Delta()
    {
        var handler = NewHandler();

        handler.RecordSignatures(StreamingChunk("call_1", "sig-abc"), new Dictionary<int, string>());

        Assert.Equal("sig-abc", handler.TryGetSignature("call_1"));
    }

    [Fact]
    public void RecordSignatures_Reads_NonStreaming_Message()
    {
        var handler = NewHandler();
        const string payload = """
        {"choices":[{"message":{"role":"assistant","tool_calls":[
          {"id":"call_2","type":"function","function":{"name":"ReadFile","arguments":"{}"},
           "extra_content":{"google":{"thought_signature":"sig-xyz"}}}]}}]}
        """;

        handler.RecordSignatures(payload);

        Assert.Equal("sig-xyz", handler.TryGetSignature("call_2"));
    }

    [Fact]
    public void RecordSignatures_Pairs_By_Index_When_Chunk_Omits_Id()
    {
        var handler = NewHandler();
        var indexToId = new Dictionary<int, string>();

        // First chunk announces the id but carries no signature yet.
        handler.RecordSignatures(
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_3","type":"function","function":{"name":"ReadFile"}}]}}]}""",
            indexToId);

        // A later chunk carries the signature under the same index, with no id of its own.
        handler.RecordSignatures(
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"extra_content":{"google":{"thought_signature":"sig-late"}}}]}}]}""",
            indexToId);

        Assert.Equal("sig-late", handler.TryGetSignature("call_3"));
    }

    [Fact]
    public void RecordSignatures_Ignores_Payload_Without_Signature()
    {
        var handler = NewHandler();

        handler.RecordSignatures("""{"choices":[{"delta":{"content":"hello"}}]}""");

        Assert.Equal(0, handler.CachedCount);
    }

    [Fact]
    public void RecordSignatures_Does_Not_Throw_On_Malformed_Json()
    {
        var handler = NewHandler();

        // A truncated chunk must never take the response down with it.
        var ex = Record.Exception(() =>
            handler.RecordSignatures("""{"choices":[{"delta":{"tool_calls":[{"id":"call_4","extra_content" """));

        Assert.Null(ex);
        Assert.Equal(0, handler.CachedCount);
    }

    // ------------------------------------------------------------------
    // RestoreSignatures - putting them back on the request
    // ------------------------------------------------------------------

    [Fact]
    public void RestoreSignatures_Attaches_Known_Signature()
    {
        var request = RequestWithToolCall("call_5");

        var restored = ThoughtSignatureHandler.RestoreSignatures(
            request, id => id == "call_5" ? "sig-restored" : null);

        Assert.Equal(1, restored);
        Assert.Equal("sig-restored", SignatureOf(request));
    }

    [Fact]
    public void RestoreSignatures_Leaves_Unknown_Tool_Call_Untouched()
    {
        var request = RequestWithToolCall("call_6");

        var restored = ThoughtSignatureHandler.RestoreSignatures(request, _ => null);

        Assert.Equal(0, restored);
        Assert.Null(SignatureOf(request));
    }

    [Fact]
    public void RestoreSignatures_Does_Not_Overwrite_Existing_ExtraContent()
    {
        var request = RequestWithToolCall("call_7", withExtraContent: true);

        var restored = ThoughtSignatureHandler.RestoreSignatures(request, _ => "sig-ours");

        Assert.Equal(0, restored);
        Assert.Equal("caller-supplied", SignatureOf(request));
    }

    [Fact]
    public void RestoreSignatures_Handles_Request_Without_Messages()
    {
        var request = new JsonObject { ["model"] = "gemini-3-pro-preview" };

        var restored = ThoughtSignatureHandler.RestoreSignatures(request, _ => "sig");

        Assert.Equal(0, restored);
    }

    // ------------------------------------------------------------------
    // Round trip and cache behaviour
    // ------------------------------------------------------------------

    [Fact]
    public void Signature_Survives_A_Full_Round_Trip()
    {
        var handler = NewHandler();

        // Response arrives carrying the signature...
        handler.RecordSignatures(StreamingChunk("call_8", "sig-round-trip"), new Dictionary<int, string>());

        // ...and the next request, rebuilt by the OpenAI SDK without it, gets it back.
        var request = RequestWithToolCall("call_8");
        var restored = ThoughtSignatureHandler.RestoreSignatures(request, handler.TryGetSignature);

        Assert.Equal(1, restored);
        Assert.Equal("sig-round-trip", SignatureOf(request));
    }

    [Fact]
    public void Cache_Is_Bounded_And_Evicts_Oldest_First()
    {
        var handler = NewHandler();
        const int cap = 512;

        for (var i = 0; i < cap + 10; i++)
        {
            handler.RecordSignatures(StreamingChunk($"call_{i}", $"sig-{i}"), new Dictionary<int, string>());
        }

        Assert.Equal(cap, handler.CachedCount);
        Assert.Null(handler.TryGetSignature("call_0"));                 // evicted
        Assert.Equal($"sig-{cap + 9}", handler.TryGetSignature($"call_{cap + 9}")); // newest kept
    }

    [Fact]
    public void First_Signature_Wins_For_A_Repeated_Tool_Call_Id()
    {
        var handler = NewHandler();

        handler.RecordSignatures(StreamingChunk("call_9", "sig-first"), new Dictionary<int, string>());
        handler.RecordSignatures(StreamingChunk("call_9", "sig-second"), new Dictionary<int, string>());

        Assert.Equal("sig-first", handler.TryGetSignature("call_9"));
        Assert.Equal(1, handler.CachedCount);
    }
}
