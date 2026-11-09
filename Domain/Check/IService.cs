using CSharp_Result;
using Domain.Habit;

namespace Domain.Check;

public interface ICheckService
{
  // Creates the Pending checks for today and tomorrow (user-local) of every enabled habit.
  Task<Result<int>> OpenUpcoming(DateTime nowUtc);

  // Settles every Pending check whose deadline + grace has passed: vacation, then freeze, then missed.
  Task<Result<int>> CloseDue(DateTime nowUtc);

  // Delivers settled-check outbox events to stake bindings.
  Task<Result<int>> DispatchEvents(DateTime nowUtc);

  // OpenUpcoming + CloseDue + DispatchEvents.
  Task<Result<int>> Tick(DateTime nowUtc);

  // The one way to complete (or fail) a habit. Settles the check that was open at
  // evidence.OccurredAtUtc. Null when nothing changed (replay, already settled, too late).
  Task<Result<HabitExecutionPrincipal?>> Report(string userId, Guid habitId, Evidence evidence, DateTime? nowUtc = null);

  // Skips the check that is open now. Null when there is none to skip.
  Task<Result<HabitExecutionPrincipal?>> Skip(string userId, Guid habitId, string? notes, DateTime? nowUtc = null);

  // Re-times Pending checks after a timezone or habit change. Open checks never get a
  // shorter deadline; checks no longer scheduled are removed. Settled checks never change.
  Task<Result<int>> RetimePending(string? userId, List<Guid>? habitIds, DateTime? nowUtc = null);
}

// A stake plugin instance attached to a habit's routine. Delivery must be idempotent per
// (event, binding); the dispatcher records each delivery so it fires at most once.
public interface IStakeBinding
{
  string BindingId { get; }
  bool AppliesTo(CheckEventPrincipal e);
  Task<Result<Unit>> Deliver(CheckEventPrincipal e);
}
