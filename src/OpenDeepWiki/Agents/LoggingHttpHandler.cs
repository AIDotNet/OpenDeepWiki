using System.Net;

namespace OpenDeepWiki.Agents;

/// <summary>
/// 自定义 HTTP 消息处理器，用于拦截和记录请求/响应状态
/// 支持 502/429 错误自动重试
/// </summary>
public class LoggingHttpHandler(HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
{
    private const int MaxRetryAttempts = 3;
    private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(60);

    public LoggingHttpHandler() : this(new HttpClientHandler())
    {
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid().ToString("N")[..8];
        var startTime = DateTime.UtcNow;

        Console.WriteLine($"[{requestId}] >>> Request: {request.Method} {request.RequestUri}");

        var attempt = 0;
        HttpResponseMessage? response = null;

        while (attempt < MaxRetryAttempts)
        {
            attempt++;

            try
            {
                // 如果是重试，需要克隆请求（因为原请求可能已被消费）
                var requestToSend = attempt == 1 ? request : await CloneRequestAsync(request);

                response = await base.SendAsync(requestToSend, cancellationToken);

                // 检查是否需要重试
                if (ShouldRetry(response.StatusCode) && attempt < MaxRetryAttempts)
                {
                    var retryDelay = GetRetryDelay(response, attempt);
                    Console.WriteLine(
                        $"[{requestId}] !!! Got response, retrying in {retryDelay.TotalSeconds:F0}s, attempt  {attempt + 1} retry...");

                    response.Dispose();
                    await Task.Delay(retryDelay, cancellationToken);
                    continue;
                }

                break;
            }
            catch (Exception ex) when (attempt < MaxRetryAttempts && IsTransientException(ex))
            {
                var retryDelay = GetExponentialDelay(attempt);
                Console.WriteLine(
                    $"[{requestId}] !!! Request error: {ex.Message}，{retryDelay.TotalSeconds:F0}s, attempt  {attempt + 1} retry...");

                await Task.Delay(retryDelay, cancellationToken);
            }
            catch (Exception ex)
            {
                var elapsed = DateTime.UtcNow - startTime;
                Console.WriteLine($"[{requestId}] !!! Request error: {ex.Message} | time: {elapsed.TotalMilliseconds:F0}ms");
                throw;
            }
        }

        var totalElapsed = DateTime.UtcNow - startTime;

        if (response != null)
        {
            Console.WriteLine(
                $"[{requestId}] <<< Response: {(int)response.StatusCode} {response.StatusCode} | time: {totalElapsed.TotalMilliseconds:F0}ms | attempts: {attempt}");

            if (!response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                Console.WriteLine($"[{requestId}] !!! Error response: {content[..Math.Min(500, content.Length)]}");
            }
        }

        return response!;
    }

    private static bool ShouldRetry(HttpStatusCode statusCode)
    {
        var i = (int)statusCode;
        if (i >= 500)
        {
            return true;
        }

        return statusCode is HttpStatusCode.BadGateway or HttpStatusCode.TooManyRequests
            or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
    }

    private static bool IsTransientException(Exception ex)
    {
        return ex is HttpRequestException or TaskCanceledException { InnerException: TimeoutException };
    }

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        // 优先使用 Retry-After 头
        if (response.Headers.RetryAfter != null)
        {
            if (response.Headers.RetryAfter.Delta.HasValue)
            {
                var delay = response.Headers.RetryAfter.Delta.Value;
                return delay > MaxRetryDelay ? MaxRetryDelay : delay;
            }

            if (response.Headers.RetryAfter.Date.HasValue)
            {
                var delay = response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero)
                {
                    return delay > MaxRetryDelay ? MaxRetryDelay : delay;
                }
            }
        }

        // 使用指数退避
        return GetExponentialDelay(attempt);
    }

    private static TimeSpan GetExponentialDelay(int attempt)
    {
        var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1) * DefaultRetryDelay.TotalSeconds);
        return delay > MaxRetryDelay ? MaxRetryDelay : delay;
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version
        };

        // 复制内容
        if (request.Content != null)
        {
            var content = await request.Content.ReadAsByteArrayAsync();
            clone.Content = new ByteArrayContent(content);

            // 复制内容头
            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        // 复制请求头
        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        // 复制选项
        foreach (var option in request.Options)
        {
            clone.Options.TryAdd(option.Key, option.Value);
        }

        return clone;
    }
}