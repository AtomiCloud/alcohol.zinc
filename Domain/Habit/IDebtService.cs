namespace Domain.Habit;

public record HabitDebtItem(
  Guid ExecutionId,
  Guid HabitId,
  Guid HabitVersionId,
  DateOnly Date,
  decimal Amount,
  string Currency,
  Guid CharityId,
  string Task
);
