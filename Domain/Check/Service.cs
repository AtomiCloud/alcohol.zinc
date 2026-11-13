using CSharp_Result;
using Domain.Exceptions;
using Domain.Habit;
using Domain.Protection;
using Domain.Vacation;
using Microsoft.Extensions.Logging;

namespace Domain.Check;

public class CheckService(
  ICheckRepository repo,
  IVacationRepository vacationRepo,
  IProtectionRepository protectionRepo,
  IFreezeCapService freezeCap,
  IEnumerable<IStakeBinding> bindings,
  ILogger<CheckService> logger
) : ICheckService
{
  public const string ManualProof = "manual";
  private const int CloseBatchSize = 500;
  private const int EventBatchSize = 100;
  private const int MaxEventAttempts = 10;

  public async Task<Result<int>> OpenUpcoming(DateTime nowUtc)
  {
    var habits = (List<SchedulableHabit>)await repo.ListSchedulableHabits(null, null);
    var checks = new List<NewCheck>();
    foreach (var h in habits)
    {
      var tz = CheckWindow.Resolve(h.Timezone);
      var today = CheckWindow.LocalDate(nowUtc, tz);
      foreach (var date in new[] { today, today.AddDays(1) })
      {
        if (!h.IsScheduledOn(date)) continue;
        var (opens, deadline) = CheckWindow.For(date, tz, h.DeadlineTime);
        // Never open a day whose deadline already passed (e.g. a habit created after its deadline).
        if (deadline <= nowUtc) continue;
        checks.Add(new NewCheck(h.HabitId, date, tz.Id, opens, deadline));
      }
    }

    return await repo.CreatePending(checks);
  }

  public async Task<Result<int>> CloseDue(DateTime nowUtc)
  {
    var due = (List<CheckPrincipal>)await repo.ListDuePending(nowUtc - CheckWindow.Grace, CloseBatchSize);
    if (due.Count == 0) return 0;
    return (await SettleDue(due, nowUtc)).Count;
  }

  // Settles Pending checks past deadline + grace, per user and date. Returns what was newly settled.
  private async Task<List<SettledCheck>> SettleDue(List<CheckPrincipal> due, DateTime nowUtc)
  {
    var habits = (List<SchedulableHabit>)await repo.ListSchedulableHabits(null,
      due.Select(c => c.HabitId).Distinct().ToList());
    var byId = habits.ToDictionary(h => h.HabitId);

    // Checks of habits that were disabled, deleted or unscheduled for that weekday since they opened.
    var stale = due
      .Where(c => !byId.TryGetValue(c.HabitId, out var h) || !h.IsScheduledOn(c.Record.LocalDate))
      .Select(c => c.Id)
      .ToHashSet();
    if (stale.Count > 0) await repo.DeletePending([.. stale]);

    var live = due.Where(c => !stale.Contains(c.Id)).ToList();
    var recorded = (HashSet<Guid>)await repo.ListRecorded(live.Select(c => c.Id).ToList());

    var settled = new List<SettledCheck>();
    foreach (var group in live.GroupBy(c => (byId[c.HabitId].UserId, c.Record.LocalDate)))
    {
      var (userId, date) = group.Key;
      try
      {
        // Days already recorded elsewhere only get adopted; protections are decided only
        // when something is actually missing, so a fully recorded day never spends a freeze.
        var missing = group.Any(c => !recorded.Contains(c.Id));
        var status = missing ? await ProtectionFor(userId, date) : CheckStatus.Missed;
        foreach (var c in group)
        {
          var outcome = (SettleOutcome?)await repo.Settle(new SettleCheck
          {
            CheckId = c.Id,
            HabitVersionId = byId[c.HabitId].VersionId,
            UserId = userId,
            Status = status,
            SettledBy = status switch
            {
              CheckStatus.Vacation => "vacation",
              CheckStatus.Frozen => "freeze",
              _ => "scheduler"
            },
            SettledAtUtc = nowUtc
          });
          if (outcome is { Adopted: false }) settled.Add(new SettledCheck(c.HabitId, date, status));
        }
      }
      catch (Exception e)
      {
        // One user's failure must not stop the rest of the batch; the checks stay
        // Pending and are retried on the next tick.
        logger.LogError(e, "CloseDue failed for UserId={UserId} Date={Date}", userId, date);
      }
    }

    return settled;
  }

  // Protections apply per user and date: vacation first, then one freeze for the whole day.
  private async Task<CheckStatus> ProtectionFor(string userId, DateOnly date)
  {
    var vacations = (List<VacationPrincipal>)await vacationRepo.ListActiveForUserOnDate(userId, date);
    if (vacations.Count > 0) return CheckStatus.Vacation;
    // Never spend above the current cap (e.g. after a downgrade). An empty balance skips the
    // cap, which loads all of the user's executions for the streak bonus.
    var protection = (UserProtectionPrincipal?)await protectionRepo.GetProtection(userId);
    if (protection?.Record.FreezeCurrent > 0) _ = (FreezeBalance)await freezeCap.Reconcile(userId);
    // Idempotent per (user, date): later checks of an already-frozen day are frozen too.
    var frozen = (bool)await protectionRepo.TryConsumeFreeze(userId, date);
    return frozen ? CheckStatus.Frozen : CheckStatus.Missed;
  }

  public async Task<Result<int>> DispatchEvents(DateTime nowUtc)
  {
    var events = (List<CheckEventPrincipal>)await repo.ClaimEvents(EventBatchSize, MaxEventAttempts, nowUtc);
    var dispatched = 0;
    foreach (var ev in events)
    {
      try
      {
        foreach (var binding in bindings.Where(b => b.AppliesTo(ev)))
        {
          if ((bool)await repo.HasDelivery(ev.Id, binding.BindingId)) continue;
          var delivered = await binding.Deliver(ev);
          if (delivered.IsFailure()) throw delivered.FailureOrDefault();
          await repo.RecordDelivery(ev.Id, binding.BindingId);
        }

        await repo.MarkDispatched(ev.Id, nowUtc);
        dispatched++;
      }
      catch (Exception e)
      {
        var backoff = TimeSpan.FromMinutes(Math.Min(360, Math.Pow(2, ev.Attempts)));
        logger.LogError(e, "Dispatching check event {EventId} failed (attempt {Attempts})", ev.Id, ev.Attempts);
        await repo.MarkFailed(ev.Id, e.Message, nowUtc + backoff);
      }
    }

    return dispatched;
  }

  public async Task<Result<int>> Tick(DateTime nowUtc)
  {
    var opened = (int)await OpenUpcoming(nowUtc);
    var closed = (int)await CloseDue(nowUtc);
    var dispatched = (int)await DispatchEvents(nowUtc);
    logger.LogInformation("Check tick: opened {Opened}, closed {Closed}, dispatched {Dispatched}",
      opened, closed, dispatched);
    return opened + closed + dispatched;
  }

  public async Task<Result<HabitExecutionPrincipal?>> Report(string userId, Guid habitId, Evidence evidence,
    DateTime? nowUtc = null)
  {
    var now = nowUtc ?? DateTime.UtcNow;
    var habit = ((List<SchedulableHabit>)await repo.ListSchedulableHabits(userId, [habitId])).FirstOrDefault();
    if (habit == null)
      return new NotFoundException("Habit not found or not enabled", typeof(HabitPrincipal), habitId.ToString());

    if ((bool)await repo.EvidenceExists(habitId, evidence.ProofKey, evidence.ExternalId))
      return (HabitExecutionPrincipal?)null;

    // Clock skew: evidence can't be from the future.
    var occurred = evidence.OccurredAtUtc > now ? now : evidence.OccurredAtUtc;
    var check = await OpenCheckAt(habit, occurred);
    if (check == null || now > check.Record.DeadlineUtc + CheckWindow.Grace)
      return (HabitExecutionPrincipal?)null;

    var status = evidence.Verdict == EvidenceVerdict.Passed ? CheckStatus.Done : CheckStatus.Missed;
    var outcome = (SettleOutcome?)await repo.Settle(new SettleCheck
    {
      CheckId = check.Id,
      HabitVersionId = habit.VersionId,
      UserId = habit.UserId,
      Status = status,
      SettledBy = evidence.ProofKey,
      SettledAtUtc = now,
      CompletedAtUtc = status == CheckStatus.Done ? occurred : null,
      Notes = evidence.Notes,
      Evidence = evidence with { OccurredAtUtc = occurred }
    });
    return outcome is { Adopted: false } ? outcome.Execution : null;
  }

  public async Task<Result<CheckPrincipal?>> PendingCheckAt(string userId, Guid habitId, DateTime atUtc,
    DateTime? nowUtc = null)
  {
    var now = nowUtc ?? DateTime.UtcNow;
    var habit = ((List<SchedulableHabit>)await repo.ListSchedulableHabits(userId, [habitId])).FirstOrDefault();
    if (habit == null)
      return new NotFoundException("Habit not found or not enabled", typeof(HabitPrincipal), habitId.ToString());

    var check = await OpenCheckAt(habit, atUtc > now ? now : atUtc);
    return check == null || now > check.Record.DeadlineUtc + CheckWindow.Grace ? null : check;
  }

  public async Task<Result<HabitExecutionPrincipal?>> Skip(string userId, Guid habitId, string? notes,
    DateTime? nowUtc = null)
  {
    var now = nowUtc ?? DateTime.UtcNow;
    var habit = ((List<SchedulableHabit>)await repo.ListSchedulableHabits(userId, [habitId])).FirstOrDefault();
    if (habit == null)
      return new NotFoundException("Habit not found or not enabled", typeof(HabitPrincipal), habitId.ToString());

    var check = await OpenCheckAt(habit, now);
    if (check == null) return (HabitExecutionPrincipal?)null;

    var outcome = (SettleOutcome?)await repo.Settle(new SettleCheck
    {
      CheckId = check.Id,
      HabitVersionId = habit.VersionId,
      UserId = habit.UserId,
      Status = CheckStatus.Skipped,
      SettledBy = ManualProof,
      SettledAtUtc = now,
      Notes = notes
    });
    return outcome is { Adopted: false } ? outcome.Execution : null;
  }

  // The Pending check whose window contains `at`. If two windows ever overlap, the earlier
  // day is settled first, so both days need their own completion.
  private async Task<CheckPrincipal?> OpenCheckAt(SchedulableHabit habit, DateTime at)
  {
    var covering = (List<CheckPrincipal>)await repo.ListCovering(habit.HabitId, at);
    var pending = covering
      .Where(c => c.Record.Status == CheckStatus.Pending)
      .OrderBy(c => c.Record.LocalDate)
      .FirstOrDefault();
    if (pending != null) return pending;
    if (covering.Count > 0) return null; // that moment's day is already settled

    // No check yet (e.g. habit created since the last scheduler tick): open it now.
    var tz = CheckWindow.Resolve(habit.Timezone);
    var date = CheckWindow.LocalDate(at, tz);
    if (!habit.IsScheduledOn(date)) return null;
    var (opens, deadline) = CheckWindow.For(date, tz, habit.DeadlineTime);
    if (at < opens || at >= deadline) return null;

    await repo.CreatePending([new NewCheck(habit.HabitId, date, tz.Id, opens, deadline)]);
    var check = (CheckPrincipal?)await repo.Get(habit.HabitId, date);
    return check is { Record.Status: CheckStatus.Pending }
           && check.Record.OpensAtUtc <= at && at < check.Record.DeadlineUtc
      ? check
      : null;
  }

  public Task<Result<int>> RetimePending(string? userId, List<Guid>? habitIds, DateTime? nowUtc = null)
    => Retime(userId, habitIds, nowUtc ?? DateTime.UtcNow, followTimezone: false);

  public async Task<Result<List<SettledCheck>>> MoveToTimezone(string userId, DateTime? nowUtc = null)
  {
    var now = nowUtc ?? DateTime.UtcNow;
    var retimed = await Retime(userId, null, now, followTimezone: true);
    if (retimed.IsFailure()) return retimed.FailureOrDefault();

    // After an eastward move a day can already be over in the new timezone: settle it now,
    // so the response can tell the user, instead of on the next scheduler tick.
    var habits = (List<SchedulableHabit>)await repo.ListSchedulableHabits(userId, null);
    if (habits.Count == 0) return new List<SettledCheck>();
    var pending = (List<CheckPrincipal>)await repo.ListPending([.. habits.Select(h => h.HabitId)]);
    var due = pending.Where(c => c.Record.DeadlineUtc <= now - CheckWindow.Grace).ToList();
    List<SettledCheck> settled = due.Count == 0 ? [] : await SettleDue(due, now);
    return settled;
  }

  // followTimezone: a timezone change, where Start and End follow the new timezone either way.
  // Otherwise (a habit edit) a started check never gets a shorter deadline.
  private async Task<Result<int>> Retime(string? userId, List<Guid>? habitIds, DateTime now, bool followTimezone)
  {
    var habits = (List<SchedulableHabit>)await repo.ListSchedulableHabits(userId, habitIds);
    var byId = habits.ToDictionary(h => h.HabitId);
    var pending = (List<CheckPrincipal>)await repo.ListPending(habitIds ?? [.. byId.Keys]);

    var changed = 0;
    var stale = new List<Guid>();
    foreach (var c in pending)
    {
      if (!byId.TryGetValue(c.HabitId, out var h) || !h.IsScheduledOn(c.Record.LocalDate))
      {
        stale.Add(c.Id);
        continue;
      }

      var tz = CheckWindow.Resolve(h.Timezone);
      var (opens, deadline) = CheckWindow.For(c.Record.LocalDate, tz, h.DeadlineTime);
      // On a habit edit a started check never gets shorter, and a check that hasn't started
      // takes the new window unless that window is already over.
      var started = c.Record.OpensAtUtc <= now;
      var newDeadline = followTimezone
        ? deadline
        : started
          ? (deadline > c.Record.DeadlineUtc ? deadline : c.Record.DeadlineUtc)
          : (deadline > now ? deadline : c.Record.DeadlineUtc);
      if (opens >= newDeadline) opens = c.Record.OpensAtUtc;

      if (tz.Id == c.Record.Timezone && opens == c.Record.OpensAtUtc && newDeadline == c.Record.DeadlineUtc)
        continue;
      if ((bool)await repo.UpdatePendingWindow(c.Id, tz.Id, opens, newDeadline)) changed++;
    }

    if (stale.Count > 0) changed += (int)await repo.DeletePending(stale);
    return changed;
  }
}
