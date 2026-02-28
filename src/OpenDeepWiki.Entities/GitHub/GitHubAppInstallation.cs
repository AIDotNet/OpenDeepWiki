using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OpenDeepWiki.Entities;

/// <summary>
/// Represents a GitHub App installation on an organization or user account.
/// </summary>
public class GitHubAppInstallation : AggregateRoot<string>
{
    /// <summary>
    /// GitHub's installation ID.
    /// </summary>
    public long InstallationId { get; set; }

    /// <summary>
    /// Organization or user login name (e.g., "keboola").
    /// </summary>
    [Required]
    [StringLength(100)]
    public string AccountLogin { get; set; } = string.Empty;

    /// <summary>
    /// Account type: "Organization" or "User".
    /// </summary>
    [Required]
    [StringLength(20)]
    public string AccountType { get; set; } = "Organization";

    /// <summary>
    /// GitHub account ID.
    /// </summary>
    public long AccountId { get; set; }

    /// <summary>
    /// Avatar URL of the account.
    /// </summary>
    [StringLength(500)]
    public string? AvatarUrl { get; set; }

    /// <summary>
    /// Optional link to a Department for auto-assignment.
    /// </summary>
    [StringLength(36)]
    public string? DepartmentId { get; set; }

    /// <summary>
    /// Cached installation access token (short-lived, ~1 hour).
    /// </summary>
    public string? CachedAccessToken { get; set; }

    /// <summary>
    /// When the cached access token expires.
    /// </summary>
    public DateTime? TokenExpiresAt { get; set; }

    /// <summary>
    /// Navigation property to the linked department.
    /// </summary>
    [ForeignKey("DepartmentId")]
    public virtual Department? Department { get; set; }
}
