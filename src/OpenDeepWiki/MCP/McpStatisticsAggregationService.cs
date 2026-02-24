using OpenDeepWiki.Services.Mcp;

namespace OpenDeepWiki.MCP;

/// <summary>
/// MCP 统计聚合后台服务
/// 每小时聚合前一天的使用日志到 McpDailyStatistics
/// </summary>
public class McpStatisticsAggregationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<McpStatisticsAggregationService> _logger;
    private static readonly TimeSpan AggregationInterval = TimeSpan.FromHours(1);

    public McpStatisticsAggregationService(
        IServiceScopeFactory scopeFactory,
        ILogger<McpStatisticsAggregationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait a bit before first run to let the app start up
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var logService = scope.ServiceProvider.GetRequiredService<IMcpUsageLogService>();

                // Aggregate today and yesterday
                var today = DateTime.UtcNow.Date;
                await logService.AggregateDailyStatisticsAsync(today);
                await logService.AggregateDailyStatisticsAsync(today.AddDays(-1));

                _logger.LogDebug("MCP 统计聚合完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MCP 统计聚合服务异常");
            }

            await Task.Delay(AggregationInterval, stoppingToken);
        }
    }
}
