namespace OpenDeepWiki.Services.Repositories;

/// <summary>
/// 公开仓库只读接口（tree/docs/branches 等）的进程内缓存约定。
/// 缓存 key 携带仓库级版本号；RegenerateAsync 等写操作通过更换版本号让旧缓存立即失效。
/// 仅缓存公开仓库数据；私有仓库响应永不写缓存。
/// </summary>
public static class RepositoryPublicReadCache
{
    private const string VersionKeyPrefix = "repos:cache-version";

    /// <summary>稳定状态（Completed）响应缓存时长</summary>
    public static readonly TimeSpan StableTtl = TimeSpan.FromMinutes(2);

    /// <summary>易变状态（生成中/失败/不存在）响应缓存时长，同时防止爬虫扫描不存在路径造成缓存穿透</summary>
    public static readonly TimeSpan VolatileTtl = TimeSpan.FromSeconds(30);

    /// <summary>仓库级版本号的缓存时长，到期自动轮换</summary>
    public static readonly TimeSpan VersionTtl = TimeSpan.FromHours(1);

    public static string BuildVersionKey(string owner, string repo) =>
        $"{VersionKeyPrefix}:{owner.ToLowerInvariant()}:{repo.ToLowerInvariant()}";

    public static string BuildEntryKey(
        string prefix,
        string version,
        string owner,
        string repo,
        string? branch,
        string? lang) =>
        $"{prefix}:{version}:{owner.ToLowerInvariant()}:{repo.ToLowerInvariant()}:{branch?.ToLowerInvariant()}:{lang?.ToLowerInvariant()}";
}
