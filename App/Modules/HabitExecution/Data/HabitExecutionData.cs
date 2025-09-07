using System.ComponentModel.DataAnnotations;
using App.Modules.HabitVersion.Data;
using Domain.Habit;

namespace App.Modules.HabitExecution.Data
{
    public class HabitExecutionData
    {
        [Key]
        public Guid Id { get; set; }

        public required Guid HabitVersionId { get; set; }  // References specific habit version
        public required DateOnly Date { get; set; }
        public required ExecutionStatus Status { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string? Notes { get; set; }
        public bool PaymentProcessed { get; set; } = false;  // For penalty tracking

        // Navigation property
        public virtual HabitVersionData? HabitVersion { get; set; }
    }
}