using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace App.Modules.Charities.Data
{
    public class CharityData
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; }

        [MaxLength(256)]
        public required string Name { get; set; }

        [MaxLength(128)]
        public string? Slug { get; set; }

        [MaxLength(8192)]
        public string? Mission { get; set; }

        public string[] Countries { get; set; } = [];

        [MaxLength(128)]
        public string? PrimaryRegistrationNumber { get; set; }

        [MaxLength(2)]
        public string? PrimaryRegistrationCountry { get; set; }

        [MaxLength(512)]
        public string? WebsiteUrl { get; set; }

        [MaxLength(512)]
        public string? LogoUrl { get; set; }

        // Set when an admin deletes the charity: it is switched off (hidden from search, cannot be
        // chosen for a habit or as a default) but never erased, because penalties, balances, payouts
        // and habit history point at it.
        public DateTime? DeletedAt { get; set; }
    }
}
