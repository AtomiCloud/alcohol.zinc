using System.ComponentModel.DataAnnotations;

namespace App.Modules.Charities.Data
{
    public class CharityData
    {
        [Key]
        public int Id { get; set; }
        public required string Name { get; set; }
        public required string Email { get; set; }
    }
}
