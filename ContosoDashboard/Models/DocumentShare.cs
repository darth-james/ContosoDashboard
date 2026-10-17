using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ContosoDashboard.Models;

public class DocumentShare
{
    [Key]
    public int DocumentShareId { get; set; }

    [Required]
    public int DocumentId { get; set; }

    /// <summary>Always the document owner.</summary>
    [Required]
    public int SharedByUserId { get; set; }

    /// <summary>Set for a user share.</summary>
    public int? SharedWithUserId { get; set; }

    /// <summary>Set for a team share. Matches <see cref="User.Department"/>.</summary>
    [MaxLength(100)]
    public string? SharedWithDepartment { get; set; }

    public DateTime SharedDate { get; set; } = DateTime.UtcNow;

    // Navigation properties
    [ForeignKey("DocumentId")]
    public virtual Document Document { get; set; } = null!;

    [ForeignKey("SharedByUserId")]
    public virtual User SharedByUser { get; set; } = null!;

    [ForeignKey("SharedWithUserId")]
    public virtual User? SharedWithUser { get; set; }
}
