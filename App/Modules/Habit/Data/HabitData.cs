using System.ComponentModel.DataAnnotations;

namespace App.Modules.Habit.Data
{
    public class HabitData
    {
        [Key]
        public Guid? Id { get; set; }

        public required string Task { get; set; }
        public required string DayOfWeek { get; set; }
        public TimeOnly NotificationTime { get; set; }
        // Stake stored as whole number (e.g., cents)
        public int StakeCents { get; set; }
        // Ratio stored as basis points (e.g., 25.5% as 255)
        public int RatioBasisPoints { get; set; }
        public DateOnly StartDate { get; set; }
        public DateOnly EndDate { get; set; }
        public int? CharityId { get; set; }
        public int Version { get; set; }
        public Guid HabitId { get; set; }
        public required string UserId { get; set; }
    }
}
