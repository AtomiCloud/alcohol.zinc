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

  // The Pending check whose window contains atUtc (clamped to now), created on demand like
  // Report does. Null when that moment's day is settled, unscheduled, or over (past grace).
  Task<Result<CheckPrincipal?>> PendingCheckAt(string userId, Guid habitId, DateTime atUtc, DateTime? nowUtc = null);

  // Skips the check that is open now, within the user's monthly skips. Null when there is none to skip.
  Task<Result<HabitExecutionPrincipal?>> Skip(string userId, Guid habitId, string? notes, SkipQuota quota,
    DateTime? nowUtc = null);

  // Re-times Pending checks after a habit change (days, deadline time, enabled). Started
  // checks never get a shorter deadline; checks no longer scheduled are removed.
  Task<Result<int>> RetimePending(string? userId, List<Guid>? habitIds, DateTime? nowUtc = null);

  // Moves the user's Pending checks to their new timezone: Start and End follow the phone,
  // earlier or later. Days whose new End (+ grace) has already passed are settled now
  // (vacation, then freeze, then missed) and returned. Settled checks never change.
  Task<Result<List<SettledCheck>>> MoveToTimezone(string userId, DateTime? nowUtc = null);
}

// A stake plugin instance attached to a habit's routine. Delivery must be idempotent per
// (event, binding); the dispatcher records each delivery so it fires at most once.
public interface IStakeBinding
{
  string BindingId { get; }
  bool AppliesTo(CheckEventPrincipal e);
  Task<Result<Unit>> Deliver(CheckEventPrincipal e);
}
