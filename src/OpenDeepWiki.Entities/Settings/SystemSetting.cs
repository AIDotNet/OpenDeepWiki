using System.ComponentModel.DataAnnotations;

namespace OpenDeepWiki.Entities;

/// <summary>
/// System setting entity.
/// </summary>
public class SystemSetting : AggregateRoot<string>
{
    /// <summary>
    /// Setting key.
    /// </summary>
    [Required]
    [StringLength(100)]
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Setting value.
    /// </summary>
    public string? Value { get; set; }

    /// <summary>
    /// Setting description.
    /// </summary>
    [StringLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// Setting category (general, ai, security, etc.).
    /// </summary>
    [StringLength(50)]
    public string Category { get; set; } = "general";
}
