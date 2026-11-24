using System.Text.Json;
using CSharp_Result;
using Domain.Check;
using Domain.Configuration;
using Domain.Exceptions;
using Domain.Proof;
using Microsoft.Extensions.Logging;

namespace Domain.Strava;

public interface IStravaDayService
{
  // Saves a fetched activity and matches its day again (and its old day, when its start moved).
  // A day that has settled keeps its results.
  Task<Result<Unit>> Record(string userId, long activityId, StravaActivity activity, DateTime? nowUtc = null);

  // The user's activities of a local date, oldest first.
  Task<Result<List<StravaDayActivity>>> ListDay(string userId, DateOnly localDate, DateTime? nowUtc = null);

  // The user's choice for an activity: a Strava habit of the same day it fits, or null for "not
  // used". A habit that held another activity swaps with it. Returns the activity's day.
  Task<Result<List<StravaDayActivity>>> Assign(string userId, long activityId, Guid? habitId,
    DateTime? nowUtc = null);
}

// Matches a user's Strava activities to their Strava-bound habits one local day at a time
// (StravaMatcher) and keeps the habits' checks in step: a habit that gains an activity is Done,
// one that loses it is Pending again. Days settle ICheckSettlePolicy's wait after midnight.
public class StravaDayService(
  IStravaActivityRepository activities,
  ICheckRepository checks,
  IProofBindingRepository bindings,
  IConfigurationRepository configurations,
  ICheckSettlePolicy settlePolicy,
  ILogger<StravaDayService> logger
) : IStravaDayService
{
  // Strava's terms: cached activity data lives at most 7 days.
  public static readonly TimeSpan Retention = TimeSpan.FromDays(7);

  private static readonly JsonSerializerOptions SettingsJson = new(JsonSerializerDefaults.Web);

  public async Task<Result<Unit>> Record(string userId, long activityId, StravaActivity activity,
    DateTime? nowUtc = null)
  {
    var now = nowUtc ?? DateTime.UtcNow;
    var tz = await UserTimezone(userId);
    var start = DateTime.SpecifyKind(activity.StartDate, DateTimeKind.Utc);
    var record = new StravaActivityRecord
    {
      Name = activity.Name,
      SportType = activity.SportType,
      StartUtc = start,
      Manual = activity.Manual,
      LocalDate = CheckWindow.LocalDate(start, tz)
    };

    return await activities.Exclusive<Unit>(userId, async () =>
    {
      var old = (StravaActivityPrincipal?)await activities.Get(userId, activityId);
      await activities.Upsert(userId, activityId, record, now);

      // The old day first: its habit gives the activity up before the new day claims it.
      if (old != null && old.Record.LocalDate != record.LocalDate && !Locked(old.Record.LocalDate, tz, now))
      {
        var rematched = await Reconcile(userId, old.Record.LocalDate, now);
        if (rematched.IsFailure()) return rematched;
      }

      if (Locked(record.LocalDate, tz, now)) return new Unit();
      return await Reconcile(userId, record.LocalDate, now);
    });
  }

  public async Task<Result<List<StravaDayActivity>>> ListDay(string userId, DateOnly localDate,
    DateTime? nowUtc = null)
  {
    var now = nowUtc ?? DateTime.UtcNow;
    var tz = await UserTimezone(userId);
    var day = (List<StravaActivityPrincipal>)await activities.ListForDate(userId, localDate);
    var locked = Locked(localDate, tz, now);
    return day
      .OrderBy(a => a.Record.StartUtc).ThenBy(a => a.ActivityId)
      .Select(a => new StravaDayActivity(a, locked))
      .ToList();
  }

  public async Task<Result<List<StravaDayActivity>>> Assign(string userId, long activityId, Guid? habitId,
    DateTime? nowUtc = null)
  {
    var now = nowUtc ?? DateTime.UtcNow;
    var tz = await UserTimezone(userId);

    var assigned = await activities.Exclusive<DateOnly>(userId, async () =>
    {
      var activity = (StravaActivityPrincipal?)await activities.Get(userId, activityId);
      if (activity == null)
        return new NotFoundException("Strava activity not found", typeof(StravaActivityPrincipal),
          activityId.ToString());
      var date = activity.Record.LocalDate;
      if (Locked(date, tz, now)) return new StravaDayLockedException(date);

      var pins = new Dictionary<long, StravaAssignment>();
      if (habitId is { } target)
      {
        var day = await LoadDay(userId, date, now);
        var habit = day.Open.FirstOrDefault(h => h.HabitId == target);
        if (habit == null)
          return new InvalidRequestException("habitId",
            "Pick a Strava habit of the activity's day that is still open (not skipped, frozen or done another way)");
        if (!StravaMatcher.Fits(ToMatch(activity), habit))
          return new InvalidRequestException("habitId",
            activity.Record.Manual
              ? "Activities entered by hand don't count"
              : $"This habit doesn't count {activity.Record.SportType} activities");

        // The activity holding the target swaps into this activity's old habit when it fits there;
        // otherwise matching places it again around the pins.
        var holder = day.Activities.FirstOrDefault(a =>
          a.ActivityId != activityId && a.Assignment.HabitId == target);
        if (holder != null)
        {
          var oldHabit = activity.Assignment.HabitId is { } o ? day.Open.FirstOrDefault(h => h.HabitId == o) : null;
          pins[holder.ActivityId] = oldHabit != null && StravaMatcher.Fits(ToMatch(holder), oldHabit)
            ? new StravaAssignment(oldHabit.HabitId, true)
            : new StravaAssignment(null, false);
        }
      }

      pins[activityId] = new StravaAssignment(habitId, true);
      await activities.SetAssignments(userId, pins);
      var reconciled = await Reconcile(userId, date, now);
      if (reconciled.IsFailure()) return reconciled.FailureOrDefault();
      return date;
    });
    if (assigned.IsFailure()) return assigned.FailureOrDefault();
    return await ListDay(userId, assigned.Get(), now);
  }

  // The day's Strava habits that an activity can still prove, with the activity each holds now.
  private record Day(List<MatchHabit> Open, List<StravaActivityPrincipal> Activities,
    Dictionary<Guid, (CheckPrincipal? Check, long? ActivityId)> State, Dictionary<Guid, SchedulableHabit> Habits);

  private async Task<Day> LoadDay(string userId, DateOnly date, DateTime now)
  {
    var today = CheckWindow.LocalDate(now, await UserTimezone(userId));
    var habits = ((List<SchedulableHabit>)await checks.ListSchedulableHabits(userId, null))
      .Where(h => h.ProofKey == StravaProofPlugin.Key && h.IsScheduledOn(date))
      .ToDictionary(h => h.HabitId);
    var sportTypes = ((List<ProofBinding>)await bindings.ListForUser(userId, StravaProofPlugin.Key))
      .ToDictionary(b => b.Principal.HabitId, b => SportTypesOf(b.Principal.Record.Settings));
    var dayChecks = ((List<CheckPrincipal>)await checks.ListForDate([.. habits.Keys], date))
      .ToDictionary(c => c.HabitId);
    var evidence = (Dictionary<Guid, string>)await checks.ListEvidence(
      [.. dayChecks.Values.Where(c => c.Record.Status == CheckStatus.Done).Select(c => c.Id)], StravaProofPlugin.Key);
    var dayActivities = (List<StravaActivityPrincipal>)await activities.ListForDate(userId, date);
    var known = dayActivities.Select(a => a.ActivityId).ToHashSet();

    var open = new List<MatchHabit>();
    var state = new Dictionary<Guid, (CheckPrincipal?, long?)>();
    foreach (var h in habits.Values)
    {
      var check = dayChecks.GetValueOrDefault(h.HabitId);
      // A past day without a check: the habit didn't exist yet (the scheduler opens every day ahead).
      if (check == null && date < today) continue;
      long? holds = null;
      if (check != null && check.Record.Status != CheckStatus.Pending)
      {
        // Done by Strava can move to another activity. Anything else (Check in, skip, freeze,
        // vacation, an older release) stays, and so does a Done whose activity was deleted.
        if (check.Record.Status != CheckStatus.Done || check.Record.SettledBy != StravaProofPlugin.Key) continue;
        if (!evidence.TryGetValue(check.Id, out var externalId) || !long.TryParse(externalId, out var id)) continue;
        // Moved to another day by an update: still ours to undo. Unknown (proven before this
        // release cached activities): leave the day as it is.
        if (!known.Contains(id) && (StravaActivityPrincipal?)await activities.Get(userId, id) == null) continue;
        holds = id;
      }

      open.Add(new MatchHabit(h.HabitId, h.SortTime, h.SortOrder,
        sportTypes.GetValueOrDefault(h.HabitId) ?? []));
      state[h.HabitId] = (check, holds);
    }

    return new Day(open, dayActivities, state, habits);
  }

  // Matches the day and moves the habits' checks to the result.
  private async Task<Result<Unit>> Reconcile(string userId, DateOnly date, DateTime now)
  {
    var day = await LoadDay(userId, date, now);
    var matched = StravaMatcher.Match(day.Open, day.Activities.Select(ToMatch).ToList());
    var wanted = matched.Where(m => m.Value != null).ToDictionary(m => m.Value!.Value, m => m.Key);
    var byId = day.Activities.ToDictionary(a => a.ActivityId);

    // Every habit that loses its activity first, so the evidence is free for its new habit.
    foreach (var (habitId, (check, holds)) in day.State)
    {
      if (holds is not { } current || check == null) continue;
      if (wanted.TryGetValue(habitId, out var next) && next == current) continue;
      var reopened = await checks.Reopen(check.Id, userId, StravaProofPlugin.Key, current.ToString());
      if (reopened.IsFailure()) return reopened.FailureOrDefault();
    }

    foreach (var (habitId, activityId) in wanted.ToList())
    {
      if (day.State[habitId].ActivityId == activityId) continue;
      var settled = await SettleDone(day.Habits[habitId], date, byId[activityId], now);
      if (settled.IsFailure()) return settled.FailureOrDefault();
      // Another writer settled the check first (e.g. Check in): the activity stays free.
      if (!settled.Get()) matched[activityId] = null;
    }

    // A pin to a habit that no longer takes the activity (skipped, done another way, sport
    // changed) is dropped, so the activity is free again next time.
    var assignments = day.Activities
      .Select(a => (a.ActivityId, Old: a.Assignment, New: new StravaAssignment(matched[a.ActivityId],
        a.Assignment.Pinned && (a.Assignment.HabitId == null || a.Assignment.HabitId == matched[a.ActivityId]))))
      .Where(x => x.Old != x.New)
      .ToDictionary(x => x.ActivityId, x => x.New);
    if (assignments.Count > 0) await activities.SetAssignments(userId, assignments);
    logger.LogInformation("Matched Strava day {Date} for UserId={UserId}: {Filled} of {Habits} habits", date, userId,
      wanted.Count, day.Open.Count);
    return new Unit();
  }

  // False when the check was no longer Pending, so the activity proved nothing.
  private async Task<Result<bool>> SettleDone(SchedulableHabit habit, DateOnly date, StravaActivityPrincipal activity,
    DateTime now)
  {
    var check = (CheckPrincipal?)await checks.Get(habit.HabitId, date);
    if (check == null)
    {
      // No check yet (e.g. habit created since the last scheduler tick): open it now.
      var tz = CheckWindow.Resolve(habit.Timezone);
      var (opens, deadline) = CheckWindow.For(date, tz);
      await checks.CreatePending([new NewCheck(habit.HabitId, date, tz.Id, opens, deadline)]);
      check = (CheckPrincipal?)await checks.Get(habit.HabitId, date);
    }

    if (check is not { Record.Status: CheckStatus.Pending }) return false;
    var settled = await checks.Settle(new SettleCheck
    {
      CheckId = check.Id,
      HabitVersionId = habit.VersionId,
      UserId = habit.UserId,
      Status = CheckStatus.Done,
      SettledBy = StravaProofPlugin.Key,
      SettledAtUtc = now,
      CompletedAtUtc = activity.Record.StartUtc,
      Evidence = new Evidence
      {
        ProofKey = StravaProofPlugin.Key,
        ExternalId = activity.ActivityId.ToString(),
        Verdict = EvidenceVerdict.Passed,
        OccurredAtUtc = activity.Record.StartUtc
      }
    });
    if (settled.IsFailure()) return settled.FailureOrDefault();
    return settled.Get() is { Adopted: false };
  }

  private static MatchActivity ToMatch(StravaActivityPrincipal a) =>
    new(a.ActivityId, a.Record.StartUtc, a.Record.SportType, a.Record.Manual, a.Assignment);

  private static HashSet<string> SportTypesOf(string settings) =>
    [.. JsonSerializer.Deserialize<StravaSettings>(settings, SettingsJson)?.SportTypes ?? []];

  // A day is locked once it settled: its local midnight plus the Strava upload wait.
  private bool Locked(DateOnly date, TimeZoneInfo tz, DateTime now) =>
    now >= CheckWindow.For(date, tz).DeadlineUtc + settlePolicy.WaitFor(StravaProofPlugin.Key);

  private async Task<TimeZoneInfo> UserTimezone(string userId)
  {
    var config = (Configuration.Configuration?)await configurations.GetByUserId(userId);
    return CheckWindow.Resolve(config?.Principal.Record.Timezone);
  }
}
