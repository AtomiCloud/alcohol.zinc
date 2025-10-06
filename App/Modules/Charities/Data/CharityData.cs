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

        [MaxLength(2048)]
        public string? Description { get; set; }

        public string[] Countries { get; set; } = [];

        [MaxLength(128)]
        public string? PrimaryRegistrationNumber { get; set; }

        [MaxLength(2)]
        public string? PrimaryRegistrationCountry { get; set; }

        [MaxLength(512)]
        public string? WebsiteUrl { get; set; }

        [MaxLength(512)]
        public string? LogoUrl { get; set; }

        public bool? IsVerified { get; set; }

        [MaxLength(128)]
        public string? VerificationSource { get; set; }

        public DateTimeOffset? LastVerifiedAt { get; set; }

        public bool? DonationEnabled { get; set; }
    }
}
