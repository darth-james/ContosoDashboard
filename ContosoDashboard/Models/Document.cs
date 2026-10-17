using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ContosoDashboard.Models;

public class Document
{
    [Key]
    public int DocumentId { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    [Required]
    [MaxLength(50)]
    public string Category { get; set; } = string.Empty;

    public int? ProjectId { get; set; }

    [Required]
    public int UploadedByUserId { get; set; }

    public DateTime UploadedDate { get; set; } = DateTime.UtcNow;

    public DateTime LastModifiedDate { get; set; } = DateTime.UtcNow;

    [Required]
    [MaxLength(255)]
    public string OriginalFileName { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    [Required]
    [MaxLength(255)]
    public string FileType { get; set; } = string.Empty;

    [Required]
    [MaxLength(500)]
    public string FilePath { get; set; } = string.Empty;

    // Navigation properties
    [ForeignKey("UploadedByUserId")]
    public virtual User UploadedByUser { get; set; } = null!;

    [ForeignKey("ProjectId")]
    public virtual Project? Project { get; set; }

    public virtual ICollection<DocumentTag> Tags { get; set; } = new List<DocumentTag>();
    public virtual ICollection<DocumentShare> Shares { get; set; } = new List<DocumentShare>();
    public virtual ICollection<TaskDocument> TaskAttachments { get; set; } = new List<TaskDocument>();
}
