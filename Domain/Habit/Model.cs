using Domain.Charity;
using Domain.User;
using NodaMoney;

namespace Domain.Habit
{
    // Main habit entity - stable identifier with current version pointer
    public record HabitRecord
    {
        public required ushort Version { get; init; }  // Current version pointer
        public required bool Enabled { get; init; }   // User can enable/disable habit
    }

    public record HabitPrincipal
    {
        public required Guid Id { get; init; }
        public required string UserId { get; init; }
        public required HabitRecord Record { get; init; }
    }

    public record Habit
    {
        public required HabitPrincipal Principal { get; init; }
        public required UserPrincipal User { get; init; }
    }

    // Habit version details - versioned blueprint configuration
    public record HabitVersionRecord
    {
        public required Guid CharityId { get; init; }
        public required string Task { get; init; }
        public required string DayOfWeek { get; init; }
        public required TimeOnly NotificationTime { get; init; }
        public required Money Stake { get; init; }
        public required decimal Ratio { get; init; }
        public required ushort Version { get; init; }
    }

    public record HabitVersionPrincipal
    {
        public required Guid Id { get; init; }
        public required Guid HabitId { get; init; }
        public required HabitVersionRecord Record { get; init; }
    }

    public record HabitVersion
    {
        public required HabitVersionPrincipal Principal { get; init; }
        public required CharityPrincipal Charity { get; init; }
    }

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
        public required Guid HabitVersionId { get; init; }  // References specific habit version
        public required HabitExecutionRecord Record { get; init; }
    }

    public record HabitExecution
    {
        public required HabitExecutionPrincipal Principal { get; init; }
        public required HabitVersionPrincipal HabitVersion { get; init; }  // The habit version active when created
    }

    public enum ExecutionStatus
    {
        Pending,    // Created, waiting for user action
        Completed,  // User marked as done
        Failed      // End-of-day passed without completion
    }
}
