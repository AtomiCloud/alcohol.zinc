using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace App.Modules.Causes.Data
{
    public class CauseData
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; }

        [MaxLength(128)]
        public required string Key { get; set; }

        [MaxLength(256)]
        public required string Name { get; set; }
    }
}
