using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace App.Modules.Charities.Data
{
    public class ExternalIdData
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; }

        [ForeignKey("Charity")] public Guid CharityId { get; set; }

        [MaxLength(64)] public required string Source { get; set; }
        [MaxLength(256)] public required string ExternalKey { get; set; }

        [MaxLength(2048)] public string? Url { get; set; }

        public string? Payload { get; set; }

        public DateTimeOffset? LastSyncedAt { get; set; }
    }
}

