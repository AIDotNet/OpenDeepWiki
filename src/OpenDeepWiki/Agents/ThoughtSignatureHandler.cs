using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenDeepWiki.Agents;

/// <summary>
/// A <see cref="DelegatingHandler"/> that round-trips Gemini's <c>thought_signature</c>
/// across the turns of a tool-calling conversation.
///
/// Gemini 3 models reason before they act. When that reasoning produces a function call,
/// the response carries an opaque signature at
/// <c>choices[].message.tool_calls[].extra_content.google.thought_signature</c>, and the API
/// requires it to be sent back with that same tool call on the following turn. The reasoning
/// itself is never returned as text, so the signature is the only thing carrying it forward;
/// without it the request is rejected outright:
///
///   400 INVALID_ARGUMENT - "Function call is missing a thought_signature in functionCall
///   parts. This is required for tools to work correctly."
///
/// The field lives outside the OpenAI schema, so the OpenAI .NET SDK and
/// Microsoft.Extensions.AI drop it while remapping the response onto their own model, and
/// the follow-up request is rebuilt without it. The first call of a conversation therefore
/// succeeds and the second fails - which shows up as an agent dying on its first tool result.
///
/// This handler closes the gap at the HTTP layer, where the raw JSON is still intact: it
/// records each signature as the response streams past, keyed by tool call id, and restores
/// it on any later request that references the same call. Responses are only read, never
/// modified, and only requests bound for Gemini's OpenAI-compatible endpoint are touched.
///
/// This mirrors <see cref="FinishReasonNormalizingHandler"/>, which already corrects a
/// different Gemini/OpenAI mismatch on the same layer.
/// </summary>
public sealed class ThoughtSignatureHandler : DelegatingHandler
{
    private const string GeminiHost = "generativelanguage.googleapis.com";

    /// <summary>
    /// Upper bound on remembered signatures. A signature runs to several kilobytes and
    /// <c>AgentFactory</c> builds a fresh <see cref="HttpClient"/> per agent, so this only has
    /// to span a single conversation; the cap is a safety net against a very long one.
    /// </summary>
    private const int MaxCachedSignatures = 512;

    private static readonly Serilog.ILogger Logger = Serilog.Log.ForContext<ThoughtSignatureHandler>();

    private readonly Dictionary<string, string> _signatures = new(StringComparer.Ordinal);
    private readonly Queue<string> _insertionOrder = new();
    private readonly object _gate = new();

    public ThoughtSignatureHandler(HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var isGemini = request.RequestUri?.Host.EndsWith(GeminiHost, StringComparison.OrdinalIgnoreCase) == true;

        if (isGemini)
        {
            await TryRestoreSignaturesAsync(request, cancellationToken);
        }

        var response = await base.SendAsync(request, cancellationToken);

        if (!isGemini || !response.IsSuccessStatusCode)
        {
            return response;
        }

        try
        {
            var mediaType = response.Content.Headers.ContentType?.MediaType;

            if (string.Equals(mediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
            {
                response.Content = WrapSseContent(response.Content);
            }
            else if (string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase))
            {
                // Non-streaming completion: buffer it, harvest, hand back an equivalent body.
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                RecordSignatures(json);

                var replacement = new StringContent(json, Encoding.UTF8);
                foreach (var header in response.Content.Headers)
                {
                    replacement.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }

                response.Content = replacement;
            }
        }
        catch (Exception ex)
        {
            // Never break the call - fall through with the original response.
            Logger.Warning(ex, "ThoughtSignatureHandler: failed to record signatures; response left untouched.");
        }

        return response;
    }

    /// <summary>
    /// Rewrites the outgoing body so every tool call we hold a signature for carries it again.
    /// The body is replaced with a fresh <see cref="StringContent"/>, which stays re-readable
    /// and therefore survives retries performed further down the handler chain.
    /// </summary>
    private async Task TryRestoreSignaturesAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            if (request.Content is null || CachedCount == 0)
            {
                return;
            }

            var body = await request.Content.ReadAsStringAsync(cancellationToken);
            if (body.Length == 0 || !body.Contains("\"tool_calls\"", StringComparison.Ordinal))
            {
                return;
            }

            if (JsonNode.Parse(body) is not JsonObject root)
            {
                return;
            }

            var restored = RestoreSignatures(root, TryGetSignature);
            if (restored == 0)
            {
                return;
            }

            var rewritten = new StringContent(root.ToJsonString(), Encoding.UTF8, "application/json");
            foreach (var header in request.Content.Headers)
            {
                if (!string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                {
                    rewritten.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            request.Content = rewritten;

            Logger.Debug("ThoughtSignatureHandler: restored {Count} thought_signature value(s) on the request.", restored);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "ThoughtSignatureHandler: failed to restore signatures; request left untouched.");
        }
    }

    private HttpContent WrapSseContent(HttpContent original)
    {
        var observing = new ObservingStreamContent(original, this);

        foreach (var header in original.Headers)
        {
            observing.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return observing;
    }

    /// <summary>
    /// Attaches a cached signature to every tool call in <paramref name="requestRoot"/> that
    /// names one and does not already carry <c>extra_content</c>. Returns how many were added.
    ///
    /// Exposed as <c>internal static</c> so unit tests can call it without an HTTP stack.
    /// </summary>
    internal static int RestoreSignatures(JsonObject requestRoot, Func<string, string?> lookup)
    {
        if (requestRoot["messages"] is not JsonArray messages)
        {
            return 0;
        }

        var restored = 0;

        foreach (var message in messages.OfType<JsonObject>())
        {
            if (message["tool_calls"] is not JsonArray toolCalls)
            {
                continue;
            }

            foreach (var toolCall in toolCalls.OfType<JsonObject>())
            {
                // Leave anything the caller already supplied alone.
                if (toolCall["extra_content"] is not null)
                {
                    continue;
                }

                var id = toolCall["id"]?.GetValue<string>();
                if (id is null)
                {
                    continue;
                }

                var signature = lookup(id);
                if (signature is null)
                {
                    continue;
                }

                toolCall["extra_content"] = new JsonObject
                {
                    ["google"] = new JsonObject
                    {
                        ["thought_signature"] = signature
                    }
                };

                restored++;
            }
        }

        return restored;
    }

    /// <summary>
    /// Pulls every id/signature pair out of one SSE data line or one complete JSON completion
    /// and remembers it. Streaming splits a tool call across chunks - the <c>id</c> arrives in
    /// the first chunk for a given <c>index</c> and later chunks carry only that index - so
    /// unresolved indices are matched through <paramref name="indexToId"/>, which the caller
    /// keeps for the lifetime of one response stream.
    ///
    /// Exposed as <c>internal</c> so unit tests can call it without an HTTP stack.
    /// </summary>
    internal void RecordSignatures(string payload, Dictionary<int, string>? indexToId = null)
    {
        if (!payload.Contains("thought_signature", StringComparison.Ordinal) &&
            !payload.Contains("\"id\"", StringComparison.Ordinal))
        {
            return;
        }

        JsonObject? root;
        try
        {
            root = JsonNode.Parse(payload) as JsonObject;
        }
        catch (JsonException)
        {
            // A chunk we cannot parse is never worth failing the response over.
            return;
        }

        if (root?["choices"] is not JsonArray choices)
        {
            return;
        }

        foreach (var choice in choices.OfType<JsonObject>())
        {
            // "delta" for streaming chunks, "message" for a complete completion.
            var container = choice["delta"] as JsonObject ?? choice["message"] as JsonObject;

            if (container?["tool_calls"] is not JsonArray toolCalls)
            {
                continue;
            }

            foreach (var toolCall in toolCalls.OfType<JsonObject>())
            {
                var id = toolCall["id"]?.GetValue<string>();
                var index = toolCall["index"] is JsonValue indexValue && indexValue.TryGetValue<int>(out var i)
                    ? i
                    : (int?)null;

                if (id is not null && index is not null && indexToId is not null)
                {
                    indexToId[index.Value] = id;
                }

                var signature = toolCall["extra_content"]?["google"]?["thought_signature"]?.GetValue<string>();
                if (signature is null)
                {
                    continue;
                }

                var resolvedId = id;
                if (resolvedId is null && index is not null && indexToId is not null)
                {
                    indexToId.TryGetValue(index.Value, out resolvedId);
                }

                if (resolvedId is not null)
                {
                    Remember(resolvedId, signature);
                }
            }
        }
    }

    internal int CachedCount
    {
        get
        {
            lock (_gate)
            {
                return _signatures.Count;
            }
        }
    }

    private void Remember(string toolCallId, string signature)
    {
        lock (_gate)
        {
            if (!_signatures.TryAdd(toolCallId, signature))
            {
                return;
            }

            _insertionOrder.Enqueue(toolCallId);

            while (_insertionOrder.Count > MaxCachedSignatures)
            {
                _signatures.Remove(_insertionOrder.Dequeue());
            }
        }
    }

    internal string? TryGetSignature(string toolCallId)
    {
        lock (_gate)
        {
            return _signatures.TryGetValue(toolCallId, out var signature) ? signature : null;
        }
    }

    /// <summary>
    /// Streams the original SSE body through untouched while reading each data line for
    /// signatures. Nothing is buffered beyond the line in hand.
    /// </summary>
    private sealed class ObservingStreamContent(HttpContent inner, ThoughtSignatureHandler owner) : HttpContent
    {
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            var innerStream = await inner.ReadAsStreamAsync();
            await ObserveSseStreamAsync(innerStream, stream, CancellationToken.None);
        }

        protected override async Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context,
            CancellationToken cancellationToken)
        {
            var innerStream = await inner.ReadAsStreamAsync(cancellationToken);
            await ObserveSseStreamAsync(innerStream, stream, cancellationToken);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }

        private async Task ObserveSseStreamAsync(Stream source, Stream destination, CancellationToken cancellationToken)
        {
            var reader = new StreamReader(source, Encoding.UTF8, detectEncodingFromByteOrderMarks: false,
                bufferSize: 4096, leaveOpen: true);
            var writer = new StreamWriter(destination, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: 4096, leaveOpen: true) { AutoFlush = true };

            // Maps a streamed tool call's index to the id announced in its first chunk.
            var indexToId = new Dictionary<int, string>();

            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
            {
                if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    var payload = line["data:".Length..].Trim();
                    if (payload.Length > 0 && payload != "[DONE]")
                    {
                        owner.RecordSignatures(payload, indexToId);
                    }
                }

                // Pass the line on exactly as it arrived.
                await writer.WriteLineAsync(line.AsMemory(), cancellationToken);
            }

            await writer.FlushAsync(cancellationToken);
        }
    }
}
