using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace App.Modules.Charities.Data
{
    public class CharityCauseData
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; }

        [ForeignKey("Charity")] public Guid CharityId { get; set; }
        [ForeignKey("Cause")] public Guid CauseId { get; set; }
    }
}
