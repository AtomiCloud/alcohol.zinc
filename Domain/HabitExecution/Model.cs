using Domain.Habit;

namespace Domain.HabitExecution
{
    // Habit Execution Models (Daily Instances)
    public record HabitExecutionRecord
    {
        public required DateOnly Date { get; init; }
        public required ExecutionStatus Status { get; init; }
        public DateTime? CompletedAt { get; init; }
        public string? Notes { get; init; }
    }

    public record HabitExecutionPrincipal
    {
        public required Guid Id { get; init; }
        public required Guid HabitId { get; init; }      // References specific habit version
        public required HabitExecutionRecord Record { get; init; }
    }

    public record HabitExecution
    {
        public required HabitExecutionPrincipal Principal { get; init; }
        public required HabitPrincipal Habit { get; init; }  // The habit version active when created
    }

    public enum ExecutionStatus
    {
        Pending,    // Created, waiting for user action
        Completed,  // User marked as done
        Failed      // End-of-day passed without completion
    }
}