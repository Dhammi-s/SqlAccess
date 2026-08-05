using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SqlAccess.Api.Models;

/// <summary>
/// EF Core entity mapped to the existing `agencies` table in the master DB.
/// DbPassword and ConnectionString are stored ENCRYPTED at rest (see EncryptionService).
/// </summary>
[Table("agencies")]
public class Agency
{
    [Key]
    public int AgencyId { get; set; }

    public string AgencyName { get; set; } = string.Empty;

    public string? DomainUrl { get; set; }

    public string? Location { get; set; }

    public string? DbServer { get; set; }

    public string? DbName { get; set; }

    public string? DbUser { get; set; }

    /// <summary>Encrypted at rest.</summary>
    public string? DbPassword { get; set; }

    /// <summary>Encrypted at rest.</summary>
    public string? ConnectionString { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsArchived { get; set; }

    public DateTime CreatedOn { get; set; }

    public DateTime? UpdatedOn { get; set; }
}
