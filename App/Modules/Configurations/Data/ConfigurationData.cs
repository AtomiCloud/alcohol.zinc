using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using App.Modules.Charities.Data;

namespace App.Modules.Configurations.Data
{
    public class ConfigurationData
    {
        [Key]
        public Guid Id { get; init; }
        
        [MaxLength(128)]
        public required string UserId { get; set; }
        
        [MaxLength(64)]
        public required string Timezone { get; set; }
        
        public TimeOnly EndOfDay { get; set; }
        
        [ForeignKey(nameof(Charity))]
        public Guid DefaultCharityId { get; set; }
        
        // Navigation property
        public CharityData? Charity { get; set; }
    }
}
