using System.ComponentModel.DataAnnotations;
using App.Modules.HabitVersion.Data;

namespace App.Modules.Habit.Data
{
    public class HabitData
    {
        [Key]
        public Guid Id { get; set; }
        [MaxLength(128)]
        public required string UserId { get; set; }
        public required ushort Version { get; set; }     // Current version pointer
        public required bool Enabled { get; set; } = true;  // User can enable/disable habit

        // System-set when the user's tier cap is exceeded after a downgrade:
        // the newest habits over the cap are paused (read-only, no penalties)
        // until an upgrade or a deletion frees a slot. Distinct from Enabled,
        // which is the user's own toggle and must survive pause/unpause.
        public bool PausedByLimit { get; set; }

        // Over-cap pausing keeps the OLDEST habits active, so creation time is
        // load-bearing. Rows from before this column existed share the
        // migration timestamp; ties are broken by Id for a stable order.
        public DateTime CreatedAt { get; set; }

        // Soft delete
        public DateTime? DeletedAt { get; set; }

        // Navigation properties
        public virtual ICollection<HabitVersionData> Versions { get; set; } = [];
    }
}
