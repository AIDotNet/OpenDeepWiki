using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OpenDeepWiki.Entities;

/// <summary>
/// Repository processing status
/// </summary>
public enum RepositoryStatus
{
    Pending = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3
}

/// <summary>
/// Repository entity
/// </summary>
public class Repository : AggregateRoot<string>
{
    /// <summary>
    /// Owner user ID
    /// </summary>
    [Required]
    [StringLength(36)]
    public string OwnerUserId { get; set; } = string.Empty;

    /// <summary>
    /// Git URL
    /// </summary>
    [Required]
    [StringLength(500)]
    public string GitUrl { get; set; } = string.Empty;

    /// <summary>
    /// Repository name
    /// </summary>
    [Required]
    [StringLength(100)]
    public string RepoName { get; set; } = string.Empty;

    /// <summary>
    /// Repository organization
    /// </summary>
    [Required]
    [StringLength(100)]
    public string OrgName { get; set; } = string.Empty;

    /// <summary>
    /// Repository authentication account
    /// </summary>
    [StringLength(200)]
    public string? AuthAccount { get; set; }

    /// <summary>
    /// Repository authentication password (stored in plaintext)
    /// </summary>
    [StringLength(500)]
    public string? AuthPassword { get; set; }

    /// <summary>
    /// Whether public
    /// </summary>
    public bool IsPublic { get; set; } = true;

    /// <summary>
    /// Repository processing status
    /// </summary>
    public RepositoryStatus Status { get; set; } = RepositoryStatus.Pending;

    /// <summary>
    /// Star count
    /// </summary>
    public int StarCount { get; set; }

    /// <summary>
    /// Fork count
    /// </summary>
    public int ForkCount { get; set; }

    /// <summary>
    /// Bookmark count
    /// </summary>
    public int BookmarkCount { get; set; } = 0;

    /// <summary>
    /// Subscription count
    /// </summary>
    public int SubscriptionCount { get; set; } = 0;

    /// <summary>
    /// View count
    /// </summary>
    public int ViewCount { get; set; } = 0;

    /// <summary>
    /// Repository primary programming language
    /// </summary>
    [StringLength(50)]
    public string? PrimaryLanguage { get; set; }

    /// <summary>
    /// Update check interval (minutes)
    /// null means use global default
    /// </summary>
    public int? UpdateIntervalMinutes { get; set; }

    /// <summary>
    /// Last update check time
    /// </summary>
    public DateTime? LastUpdateCheckAt { get; set; }

    /// <summary>
    /// Whether this repository is owned by a department (org import) rather than an individual user.
    /// When true, the repo appears in Organization view only, not in the importing user's "My Repos".
    /// </summary>
    public bool IsDepartmentOwned { get; set; } = false;

    /// <summary>
    /// Owner user navigation property
    /// </summary>
    [ForeignKey("OwnerUserId")]
    public virtual User? Owner { get; set; }
}
