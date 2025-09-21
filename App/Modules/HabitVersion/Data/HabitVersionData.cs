using System.ComponentModel.DataAnnotations;
using App.Modules.Charities.Data;
using App.Modules.Habit.Data;
using App.Modules.HabitExecution.Data;

namespace App.Modules.HabitVersion.Data
{
    public class HabitVersionData
    {
        [Key]
        public Guid Id { get; set; }
        public required Guid HabitId { get; set; }
        public required Guid CharityId { get; set; }
        public required ushort Version { get; set; }
        
        // Habit blueprint data
        public required string Task { get; set; }
        public required string[] DaysOfWeek { get; set; }
        public TimeOnly NotificationTime { get; set; }
        public int StakeCents { get; set; }              // Stake stored as cents
        public string StakeCurrency { get; set; } = "SGD";
        public int RatioBasisPoints { get; set; }        // Ratio stored as basis points

        [MaxLength(64)]
        public required string Timezone { get; set; }

        // Navigation properties
        public virtual HabitData? Habit { get; set; }
        public virtual CharityData? Charity { get; set; }
        public virtual ICollection<HabitExecutionData> Executions { get; set; } = [];
    }
}
