using Domain.Habit;

namespace Domain.Failure
{
    public record FailureRecord
    {
        public DateOnly FailedAt { get; set; }
    }
    
    public record FailurePrincipal
    {
      public required Guid Id { get; init; }
      public required Guid HabitId { get; set; }
      public required FailureRecord  Record { get; init; }
    }
    
    public record Failure
    {
      public required FailurePrincipal Principal { get; init; }
      public required HabitPrincipal Habit { get; init; }
    }
}
