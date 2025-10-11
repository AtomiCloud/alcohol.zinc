using System.ComponentModel.DataAnnotations;
using App.Modules.HabitVersion.Data;
using App.Modules.Payment.Data;

namespace App.Modules.HabitExecution.Data
{
    public class HabitExecutionData
    {
        [Key]
        public Guid Id { get; set; }

        public required Guid HabitVersionId { get; set; }  // References specific habit version
        public required DateOnly Date { get; set; }
        public required HabitExecutionStatusData Status { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string? Notes { get; set; }

        // Navigation properties
        public virtual HabitVersionData? HabitVersion { get; set; }

        // Many-to-many relationship with PaymentIntents
        public virtual ICollection<PaymentIntentExecutionData> PaymentIntentExecutions { get; set; } = [];
    }
}
