using System.ComponentModel.DataAnnotations;
using App.Modules.HabitVersion.Data;

namespace App.Modules.Habit.Data
{
    public class HabitData
    {
        [Key]
        public Guid Id { get; set; }
        public required string UserId { get; set; }
        public required ushort Version { get; set; }     // Current version pointer
        public required bool Enabled { get; set; } = true;  // User can enable/disable habit

        // Soft delete
        public DateTime? DeletedAt { get; set; }

        // Navigation properties
        public virtual ICollection<HabitVersionData> Versions { get; set; } = [];
    }
}
