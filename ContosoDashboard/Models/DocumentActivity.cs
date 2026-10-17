using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ContosoDashboard.Models;

/// <summary>Insert-only audit record. DocumentId and TaskId deliberately have no foreign key so the record survives deletion (FR-036).</summary>
public class DocumentActivity
{
    [Key]
    public int DocumentActivityId { get; set; }

    /// <summary>No FK: survives document deletion.</summary>
    [Required]
    public int DocumentId { get; set; }

    /// <summary>Title snapshot at the time of the action.</summary>
    [Required]
    [MaxLength(200)]
    public string DocumentTitle { get; set; } = string.Empty;

    /// <summary>MIME snapshot (Upload and Replace).</summary>
    [MaxLength(255)]
    public string? FileType { get; set; }

    [Required]
    public int UserId { get; set; }

    [Required]
    public DocumentActivityAction Action { get; set; }

    /// <summary>No FK. Required for TaskAttach and TaskDetach.</summary>
    public int? TaskId { get; set; }

    [MaxLength(500)]
    public string? Details { get; set; }

    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    [ForeignKey("UserId")]
    public virtual User User { get; set; } = null!;
}

public enum DocumentActivityAction
{
    Upload = 0,
    Download = 1,
    Preview = 2,
    MetadataEdit = 3,
    Replace = 4,
    Delete = 5,
    Share = 6,
    ShareRemoved = 7,
    TaskAttach = 8,
    TaskDetach = 9
}
