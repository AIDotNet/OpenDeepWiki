using System.IO;
using System.Threading;

namespace OpenDeepWiki.MCP;

/// <summary>
/// Middleware that sends periodic keep-alive comments for SSE connections
/// to prevent MCP client disconnections (Claude Code disconnects after ~5min of inactivity).
/// </summary>
public class SseKeepAliveMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _path;
    private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(25);

    public SseKeepAliveMiddleware(RequestDelegate next, string path)
    {
        _next = next;
        _path = path;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments(_path))
        {
            await _next(context);
            return;
        }

        // Only apply to SSE responses (text/event-stream)
        var originalBody = context.Response.Body;
        using var wrappedBody = new SseKeepAliveStream(originalBody, context.RequestAborted);
        context.Response.Body = wrappedBody;

        try
        {
            await _next(context);
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }

    /// <summary>
    /// Stream wrapper that sends SSE keep-alive comments periodically.
    /// </summary>
    private class SseKeepAliveStream : Stream
    {
        private readonly Stream _inner;
        private readonly CancellationToken _ct;
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly Timer _timer;
        private static readonly byte[] KeepAliveBytes = ": keep-alive\n\n"u8.ToArray();

        public SseKeepAliveStream(Stream inner, CancellationToken ct)
        {
            _inner = inner;
            _ct = ct;
            _timer = new Timer(SendKeepAlive, null, KeepAliveInterval, KeepAliveInterval);
        }

        private async void SendKeepAlive(object? state)
        {
            if (_ct.IsCancellationRequested) return;

            try
            {
                await _writeLock.WaitAsync(_ct);
                try
                {
                    await _inner.WriteAsync(KeepAliveBytes, _ct);
                    await _inner.FlushAsync(_ct);
                }
                finally
                {
                    _writeLock.Release();
                }
            }
            catch
            {
                // Connection closed, ignore
            }
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await _writeLock.WaitAsync(cancellationToken);
            try
            {
                await _inner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await _writeLock.WaitAsync(cancellationToken);
            try
            {
                await _inner.WriteAsync(buffer, cancellationToken);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            await _writeLock.WaitAsync(cancellationToken);
            try
            {
                await _inner.FlushAsync(cancellationToken);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count)
        {
            _writeLock.Wait();
            try { _inner.Write(buffer, offset, count); }
            finally { _writeLock.Release(); }
        }
        public override void Flush()
        {
            _writeLock.Wait();
            try { _inner.Flush(); }
            finally { _writeLock.Release(); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Dispose();
                _writeLock.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

public static class SseKeepAliveExtensions
{
    public static IApplicationBuilder UseSseKeepAlive(this IApplicationBuilder app, string path)
    {
        return app.UseMiddleware<SseKeepAliveMiddleware>(path);
    }
}
