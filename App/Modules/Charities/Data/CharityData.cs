using System.ComponentModel.DataAnnotations;

namespace App.Modules.Charities.Data
{
    public class CharityData
    {
        [Key]
        public Guid Id { get; set; }
        
        [MaxLength(256)]
        public required string Name { get; set; }

        [MaxLength(256)] 
        public required string Email { get; set; }

        [MaxLength(512)] 
        public string? Address { get; set; }
    }
}
