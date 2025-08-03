using System.ComponentModel.DataAnnotations;

namespace App.Modules.Configurations.Data
{
    public class ConfigurationData
    {
        [Key]
        public required string Sub { get; set; }

        public required string Timezone { get; set; }
        public TimeOnly EndOfDay { get; set; }
        public int? DefaultCharityId { get; set; }
    }
}
