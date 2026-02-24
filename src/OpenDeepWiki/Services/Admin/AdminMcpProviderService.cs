using Microsoft.EntityFrameworkCore;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Models.Admin;

namespace OpenDeepWiki.Services.Admin;

/// <summary>
/// 管理员 MCP 提供商服务实现
/// </summary>
public class AdminMcpProviderService : IAdminMcpProviderService
{
    private const string RepositoryScopedMcpPathTemplate = "/api/mcp/{owner}/{repo}";

    private readonly IContext _context;
    private readonly ILogger<AdminMcpProviderService> _logger;

    public AdminMcpProviderService(IContext context, ILogger<AdminMcpProviderService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<McpProviderDto>> GetProvidersAsync()
    {
        var providers = await _context.McpProviders
            .Where(p => !p.IsDeleted)
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.Name)
            .ToListAsync();

        var modelIds = providers
            .Where(p => !string.IsNullOrEmpty(p.ModelConfigId))
            .Select(p => p.ModelConfigId!)
            .Distinct()
            .ToList();

        var modelNames = modelIds.Count > 0
            ? await _context.ModelConfigs
                .Where(m => modelIds.Contains(m.Id) && !m.IsDeleted)
                .ToDictionaryAsync(m => m.Id, m => m.Name)
            : new Dictionary<string, string>();

        return providers.Select(p => new McpProviderDto
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description,
            ServerUrl = RepositoryScopedMcpPathTemplate,
            TransportType = p.TransportType,
            RequiresApiKey = p.RequiresApiKey,
            ApiKeyObtainUrl = p.ApiKeyObtainUrl,
            HasSystemApiKey = !string.IsNullOrEmpty(p.SystemApiKey),
            ModelConfigId = p.ModelConfigId,
            ModelConfigName = p.ModelConfigId != null && modelNames.TryGetValue(p.ModelConfigId, out var name) ? name : null,
            IsActive = p.IsActive,
            MaxRequestsPerDay = p.MaxRequestsPerDay,
            CreatedAt = p.CreatedAt
        }).ToList();
    }

    public async Task<McpProviderDto> CreateProviderAsync(McpProviderRequest request)
    {
        var provider = new McpProvider
        {
            Id = Guid.NewGuid().ToString(),
            Name = request.Name,
            Description = request.Description,
            ServerUrl = RepositoryScopedMcpPathTemplate,
            TransportType = request.TransportType,
            RequiresApiKey = request.RequiresApiKey,
            ApiKeyObtainUrl = request.ApiKeyObtainUrl,
            SystemApiKey = request.SystemApiKey,
            ModelConfigId = request.ModelConfigId,
            IsActive = request.IsActive,
            MaxRequestsPerDay = request.MaxRequestsPerDay,
            CreatedAt = DateTime.UtcNow
        };

        _context.McpProviders.Add(provider);
        await _context.SaveChangesAsync();

        _logger.LogInformation("MCP 提供商已创建: {Name} ({Id})", provider.Name, provider.Id);

        string? modelName = null;
        if (!string.IsNullOrEmpty(provider.ModelConfigId))
        {
            modelName = await _context.ModelConfigs
                .Where(m => m.Id == provider.ModelConfigId && !m.IsDeleted)
                .Select(m => m.Name)
                .FirstOrDefaultAsync();
        }

        return new McpProviderDto
        {
            Id = provider.Id,
            Name = provider.Name,
            Description = provider.Description,
            ServerUrl = RepositoryScopedMcpPathTemplate,
            TransportType = provider.TransportType,
            RequiresApiKey = provider.RequiresApiKey,
            ApiKeyObtainUrl = provider.ApiKeyObtainUrl,
            HasSystemApiKey = !string.IsNullOrEmpty(provider.SystemApiKey),
            ModelConfigId = provider.ModelConfigId,
            ModelConfigName = modelName,
            IsActive = provider.IsActive,
            MaxRequestsPerDay = provider.MaxRequestsPerDay,
            CreatedAt = provider.CreatedAt
        };
    }

    public async Task<bool> UpdateProviderAsync(string id, McpProviderRequest request)
    {
        var provider = await _context.McpProviders
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);

        if (provider == null) return false;

        provider.Name = request.Name;
        provider.Description = request.Description;
        provider.ServerUrl = RepositoryScopedMcpPathTemplate;
        provider.TransportType = request.TransportType;
        provider.RequiresApiKey = request.RequiresApiKey;
        provider.ApiKeyObtainUrl = request.ApiKeyObtainUrl;
        if (request.SystemApiKey != null) provider.SystemApiKey = request.SystemApiKey;
        provider.ModelConfigId = request.ModelConfigId;
        provider.IsActive = request.IsActive;
        provider.MaxRequestsPerDay = request.MaxRequestsPerDay;
        provider.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("MCP 提供商已更新: {Name} ({Id})", provider.Name, provider.Id);
        return true;
    }

    public async Task<bool> DeleteProviderAsync(string id)
    {
        var provider = await _context.McpProviders
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);

        if (provider == null) return false;

        provider.IsDeleted = true;
        provider.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("MCP 提供商已删除: {Name} ({Id})", provider.Name, provider.Id);
        return true;
    }

    public async Task<Models.Admin.PagedResult<McpUsageLogDto>> GetUsageLogsAsync(McpUsageLogFilter filter)
    {
        var query = _context.McpUsageLogs
            .Where(l => !l.IsDeleted)
            .AsQueryable();

        if (!string.IsNullOrEmpty(filter.McpProviderId))
            query = query.Where(l => l.McpProviderId == filter.McpProviderId);
        if (!string.IsNullOrEmpty(filter.UserId))
            query = query.Where(l => l.UserId == filter.UserId);
        if (!string.IsNullOrEmpty(filter.ToolName))
            query = query.Where(l => l.ToolName.Contains(filter.ToolName));

        var total = await query.CountAsync();

        var items = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync();

        // Batch resolve user names and provider names
        var userIds = items.Where(l => l.UserId != null).Select(l => l.UserId!).Distinct().ToList();
        var providerIds = items.Where(l => l.McpProviderId != null).Select(l => l.McpProviderId!).Distinct().ToList();

        var userNames = userIds.Count > 0
            ? await _context.Users
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Name)
            : new Dictionary<string, string>();

        var providerNames = providerIds.Count > 0
            ? await _context.McpProviders
                .Where(p => providerIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Name)
            : new Dictionary<string, string>();

        return new Models.Admin.PagedResult<McpUsageLogDto>
        {
            Items = items.Select(l => new McpUsageLogDto
            {
                Id = l.Id,
                UserId = l.UserId,
                UserName = l.UserId != null && userNames.TryGetValue(l.UserId, out var uName) ? uName : null,
                McpProviderId = l.McpProviderId,
                McpProviderName = l.McpProviderId != null && providerNames.TryGetValue(l.McpProviderId, out var pName) ? pName : null,
                ToolName = l.ToolName,
                RequestSummary = l.RequestSummary,
                ResponseStatus = l.ResponseStatus,
                DurationMs = l.DurationMs,
                InputTokens = l.InputTokens,
                OutputTokens = l.OutputTokens,
                IpAddress = l.IpAddress,
                ErrorMessage = l.ErrorMessage,
                CreatedAt = l.CreatedAt
            }).ToList(),
            Total = total,
            Page = filter.Page,
            PageSize = filter.PageSize
        };
    }

    public async Task<McpUsageStatisticsResponse> GetMcpUsageStatisticsAsync(int days)
    {
        var startDate = DateTime.UtcNow.Date.AddDays(-days + 1);
        var response = new McpUsageStatisticsResponse();

        // Try from daily statistics first
        var dailyStats = await _context.McpDailyStatistics
            .Where(s => !s.IsDeleted && s.Date >= startDate)
            .GroupBy(s => s.Date)
            .Select(g => new
            {
                Date = g.Key,
                RequestCount = g.Sum(s => s.RequestCount),
                SuccessCount = g.Sum(s => s.SuccessCount),
                ErrorCount = g.Sum(s => s.ErrorCount),
                InputTokens = g.Sum(s => s.InputTokens),
                OutputTokens = g.Sum(s => s.OutputTokens)
            })
            .ToListAsync();

        for (var date = startDate; date <= DateTime.UtcNow.Date; date = date.AddDays(1))
        {
            var stat = dailyStats.FirstOrDefault(s => s.Date == date);
            var usage = new McpDailyUsage
            {
                Date = date,
                RequestCount = stat?.RequestCount ?? 0,
                SuccessCount = stat?.SuccessCount ?? 0,
                ErrorCount = stat?.ErrorCount ?? 0,
                InputTokens = stat?.InputTokens ?? 0,
                OutputTokens = stat?.OutputTokens ?? 0
            };
            response.DailyUsages.Add(usage);
            response.TotalRequests += usage.RequestCount;
            response.TotalSuccessful += usage.SuccessCount;
            response.TotalErrors += usage.ErrorCount;
            response.TotalInputTokens += usage.InputTokens;
            response.TotalOutputTokens += usage.OutputTokens;
        }

        return response;
    }
}
