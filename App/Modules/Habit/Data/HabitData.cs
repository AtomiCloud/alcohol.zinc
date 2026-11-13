using System.ComponentModel.DataAnnotations;
using App.Modules.HabitVersion.Data;
using App.Modules.Routine.Data;

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

        // Position in the user's habit list (one order per user). Ties are broken by Id.
        public int SortOrder { get; set; }

        // Owning routine. Nullable in the DB on purpose (expand/contract rollout);
        // new code always sets it.
        public Guid? RoutineId { get; set; }

        // Soft delete
        public DateTime? DeletedAt { get; set; }

        // Navigation properties
        public virtual RoutineData? Routine { get; set; }
        public virtual ICollection<HabitVersionData> Versions { get; set; } = [];
    }
}
