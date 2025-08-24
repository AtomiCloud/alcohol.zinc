using Domain.Charity;
using Domain.User;

namespace Domain.Habit
{
    // Habit Blueprint Models (Versioned Templates)
    public record HabitRecord
    {
        public required string Task { get; init; }
        public required string DayOfWeek { get; init; }
        public TimeOnly NotificationTime { get; init; }
        public NodaMoney.Money Stake { get; init; }
        public decimal Ratio { get; init; }
        public DateOnly StartDate { get; init; }
        public DateOnly EndDate { get; init; }
    }

    public record HabitPrincipal
    {
        public required Guid Id { get; init; }           // Unique per version
        public required Guid PlanId { get; init; }       // Same across versions
        public required Guid CharityId { get; init; }
        public required string UserId { get; init; }
        public required ushort Version { get; init; }    // Version number
        public required HabitRecord Record { get; init; }
    }

    public record Habit
    {
        public required HabitPrincipal Principal { get; init; }
        public required UserPrincipal User { get; init; }
        public required CharityPrincipal Charity { get; init; }
    }

}
