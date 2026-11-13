using App.StartUp.Registry;
using CSharp_Result;
using Domain.Check;
using Domain.Entitlement;
using Domain.Habit;
using Domain.Proof;
using Domain.Protection;
using Domain.Subscription;
using Domain.Vacation;

namespace UnitTest.Protection;

// Shared hand-rolled fakes (no Moq) for unit-testing the Domain services that
// don't need a database. Daily settlement (checks, freeze, vacation, penalties)
// is DB-backed and covered in IntTest/Check/CheckScenarioTests.cs.

// ---------------------------------------------------------------------------
// IHabitRepository: only GetHabit / Update (HabitService.Update) and
// GetOrderedIds / SetOrder (HabitService.Reorder) are backed.
// Inspectable: SetOrderCalls.
// ---------------------------------------------------------------------------
public sealed class FakeHabitRepository : IHabitRepository
{
  public List<HabitPrincipal> Habits { get; } = [];
  public List<(string UserId, List<Guid> OrderedIds)> SetOrderCalls { get; } = [];

  public Task<Result<List<Guid>>> GetOrderedIds(string userId)
    => Task.FromResult<Result<List<Guid>>>(Habits.Where(h => h.UserId == userId)
      .OrderBy(h => h.Record.SortOrder).Select(h => h.Id).ToList());

  public Task<Result<Unit>> SetOrder(string userId, IReadOnlyList<Guid> orderedIds)
  {
    var ids = orderedIds.ToList();
    SetOrderCalls.Add((userId, ids));
    for (var i = 0; i < Habits.Count; i++)
    {
      var index = ids.IndexOf(Habits[i].Id);
      if (index >= 0 && Habits[i].UserId == userId)
        Habits[i] = Habits[i] with { Record = Habits[i].Record with { SortOrder = index } };
    }
    return Task.FromResult<Result<Unit>>(new Unit());
  }

  public Task<Result<HabitPrincipal?>> GetHabit(Guid habitId)
    => Task.FromResult<Result<HabitPrincipal?>>(Habits.FirstOrDefault(h => h.Id == habitId));

  public Task<Result<List<HabitPrincipal>>> GetHabits(List<Guid> habitIds) => throw new NotImplementedException();

  public Task<Result<HabitVersionPrincipal?>> Update(Guid habitId, string userId, HabitVersionRecord versionRecord, bool enabled)
  {
    var index = Habits.FindIndex(h => h.Id == habitId && h.UserId == userId);
    if (index < 0) return Task.FromResult<Result<HabitVersionPrincipal?>>((HabitVersionPrincipal?)null);
    var habit = Habits[index];
    var version = (ushort)(habit.Record.Version + 1);
    Habits[index] = habit with { Record = habit.Record with { Version = version, Enabled = enabled } };
    return Task.FromResult<Result<HabitVersionPrincipal?>>(new HabitVersionPrincipal
    {
      Id = Guid.NewGuid(), HabitId = habitId, Version = version, Record = versionRecord
    });
  }

  public Task<Result<List<HabitVersionPrincipal>>> GetActiveHabitVersions(string userId, DateOnly date)
    => throw new NotImplementedException();
  public Task<Result<List<HabitVersionPrincipal>>> SearchHabits(HabitSearch habitSearch)
    => throw new NotImplementedException();
  public Task<Result<HabitVersionPrincipal?>> GetCurrentVersion(string userId, Guid habitId)
    => throw new NotImplementedException();
  public Task<Result<HabitVersionPrincipal>> Create(string userId, HabitVersionRecord versionRecord, Guid? routineId,
    Quota habits)
    => throw new NotImplementedException();
  public Task<Result<Unit?>> Delete(Guid habitId, string userId)
    => throw new NotImplementedException();
  public Task<Result<string?>> GetTaskNameByExecutionId(Guid executionId)
    => throw new NotImplementedException();
  public Task<Result<List<HabitExecutionPrincipal>>> SearchHabitExecutions(string userId, HabitExecutionSearch habitExecutionSearch)
    => throw new NotImplementedException();
  public Task<Result<List<HabitVersionPrincipal>>> GetVersions(string userId, Guid habitId)
    => throw new NotImplementedException();
  public Task<Result<HabitVersionPrincipal?>> GetVersion(Guid habitVersionId)
    => throw new NotImplementedException();
  public Task<Result<int>> CountHabitsForUser(string userId)
    => throw new NotImplementedException();
  public Task<Result<int>> CountUserSkipsForMonth(string userId, DateOnly monthStart, DateOnly monthEnd)
    => throw new NotImplementedException();
}

// ---------------------------------------------------------------------------
// ICheckService: records RetimePending calls; nothing else is exercised.
// ---------------------------------------------------------------------------
public sealed class FakeCheckService : ICheckService
{
  public List<(string? UserId, List<Guid>? HabitIds)> RetimeCalls { get; } = [];

  public Task<Result<int>> RetimePending(string? userId, List<Guid>? habitIds, DateTime? nowUtc = null)
  {
    RetimeCalls.Add((userId, habitIds));
    return Task.FromResult<Result<int>>(0);
  }

  public Task<Result<List<SettledCheck>>> MoveToTimezone(string userId, DateTime? nowUtc = null)
    => throw new NotImplementedException();
  public Task<Result<int>> OpenUpcoming(DateTime nowUtc) => throw new NotImplementedException();
  public Task<Result<int>> CloseDue(DateTime nowUtc) => throw new NotImplementedException();
  public Task<Result<int>> DispatchEvents(DateTime nowUtc) => throw new NotImplementedException();
  public Task<Result<int>> Tick(DateTime nowUtc) => throw new NotImplementedException();
  public Task<Result<HabitExecutionPrincipal?>> Report(string userId, Guid habitId, Evidence evidence, DateTime? nowUtc = null)
    => throw new NotImplementedException();
  public Task<Result<HabitExecutionPrincipal?>> Skip(string userId, Guid habitId, string? notes, SkipQuota quota,
    DateTime? nowUtc = null)
    => throw new NotImplementedException();
  public Task<Result<CheckPrincipal?>> PendingCheckAt(string userId, Guid habitId, DateTime atUtc, DateTime? nowUtc = null)
    => throw new NotImplementedException();
}

// ---------------------------------------------------------------------------
// IVacationRepository
// ---------------------------------------------------------------------------
//
// Seedable state: ActiveByUser[userId] = list of VacationPrincipal active on the
// queried date. ListActiveForUserOnDate returns it (the service only reads .Count).
// Inspectable: ListActiveForUserOnDateCalls.
public sealed class FakeVacationRepository : IVacationRepository
{
  public Dictionary<string, List<VacationPrincipal>> ActiveByUser { get; } = [];
  public List<(string UserId, DateOnly Date)> ListActiveForUserOnDateCalls { get; } = [];

  public Task<Result<List<VacationPrincipal>>> ListActiveForUserOnDate(string userId, DateOnly date)
  {
    ListActiveForUserOnDateCalls.Add((userId, date));
    var list = ActiveByUser.TryGetValue(userId, out var v)
      ? v.Where(x => x.Record.StartDate <= date && date <= x.Record.EndDate).ToList()
      : [];
    return Task.FromResult<Result<List<VacationPrincipal>>>(list);
  }

  // Convenience: seed one active vacation window for a user.
  public FakeVacationRepository WithActive(string userId, DateOnly start, DateOnly end, string timezone = "UTC")
  {
    if (!ActiveByUser.TryGetValue(userId, out var list))
    {
      list = [];
      ActiveByUser[userId] = list;
    }
    list.Add(new VacationPrincipal
    {
      Id = Guid.NewGuid(),
      UserId = userId,
      Record = new VacationRecord { StartDate = start, EndDate = end, Timezone = timezone }
    });
    return this;
  }

  // ---- members not exercised by MarkDailyFailures ----
  public Task<Result<VacationPrincipal?>> Get(Guid id) => throw new NotImplementedException();
  public Task<Result<VacationPrincipal?>> Get(Guid id, string? userId) => throw new NotImplementedException();
  public Task<Result<VacationPrincipal>> Create(string userId, VacationRecord record, Quota windows) => throw new NotImplementedException();
  public Task<Result<VacationPrincipal?>> Update(Guid id, VacationRecord record) => throw new NotImplementedException();
  public Task<Result<Unit?>> Delete(Guid id, string userId) => throw new NotImplementedException();
  public Task<Result<List<VacationPrincipal>>> Search(VacationSearch search) => throw new NotImplementedException();
  public Task<Result<int>> CountWindowsForYear(string userId, int year) => throw new NotImplementedException();
  public Task<Result<bool>> HasOverlap(string userId, DateOnly start, DateOnly end) => throw new NotImplementedException();
}

// ---------------------------------------------------------------------------
// IProtectionRepository
// ---------------------------------------------------------------------------
//
// Freeze balance / cap model:
//   The freeze pool is PER USER (mirrors the real UserProtections row keyed by
//   userId), held in a dictionary that lazily defaults to the ctor seed on first
//   touch. Use BalanceFor(userId) to read and SetBalanceFor(userId, n) to seed a
//   specific user. TryConsumeFreeze decrements that user's pool by 1 and returns
//   true while > 0; returns false (no decrement) at 0. It is idempotent per date:
//   a date already consumed returns true WITHOUT decrementing again.
// Inspectable: TryConsumeFreezeCalls, ConsumedDates, IncrementFreezeCalls,
//   ClampFreezeToCapCalls, RecordFreezeAwardIfAbsentCalls.
public sealed class FakeProtectionRepository(int freezeBalance = 0) : IProtectionRepository
{
  private readonly Dictionary<string, int> _balances = [];

  // Per-user freeze pool, lazily seeded from the ctor default on first read.
  public int BalanceFor(string userId) => _balances.TryGetValue(userId, out var b) ? b : freezeBalance;
  public void SetBalanceFor(string userId, int balance) => _balances[userId] = balance;

  public List<(string UserId, DateOnly Date)> TryConsumeFreezeCalls { get; } = [];
  public HashSet<(string UserId, DateOnly Date)> ConsumedDates { get; } = [];
  public List<(string UserId, int N)> IncrementFreezeCalls { get; } = [];
  public List<(string UserId, int Cap)> ClampFreezeToCapCalls { get; } = [];
  public List<(Guid HabitId, DateOnly WeekStart)> RecordFreezeAwardIfAbsentCalls { get; } = [];
  public HashSet<(Guid HabitId, DateOnly WeekStart)> AwardLedger { get; } = [];

  public Task<Result<bool>> TryConsumeFreeze(string userId, DateOnly date)
  {
    TryConsumeFreezeCalls.Add((userId, date));
    // Idempotent per date: already consumed -> true, no further decrement.
    if (ConsumedDates.Contains((userId, date)))
      return Task.FromResult<Result<bool>>(true);
    if (BalanceFor(userId) <= 0)
      return Task.FromResult<Result<bool>>(false);
    SetBalanceFor(userId, BalanceFor(userId) - 1);
    ConsumedDates.Add((userId, date));
    return Task.FromResult<Result<bool>>(true);
  }

  public Task<Result<Unit>> IncrementFreeze(string userId, int n)
  {
    IncrementFreezeCalls.Add((userId, n));
    SetBalanceFor(userId, BalanceFor(userId) + Math.Max(0, n)); // cap-blind, like the real repo
    return Task.FromResult<Result<Unit>>(new Unit());
  }

  public Task<Result<Unit>> ClampFreezeToCap(string userId, int cap)
  {
    ClampFreezeToCapCalls.Add((userId, cap));
    if (BalanceFor(userId) > cap) SetBalanceFor(userId, cap);
    return Task.FromResult<Result<Unit>>(new Unit());
  }

  public Task<Result<bool>> RecordFreezeAwardIfAbsent(Guid habitId, DateOnly weekStart)
  {
    RecordFreezeAwardIfAbsentCalls.Add((habitId, weekStart));
    var added = AwardLedger.Add((habitId, weekStart));
    return Task.FromResult<Result<bool>>(added);
  }

  public Task<Result<UserProtectionPrincipal?>> GetProtection(string userId)
    => Task.FromResult<Result<UserProtectionPrincipal?>>(new UserProtectionPrincipal
    {
      UserId = userId,
      Record = new UserProtectionRecord { FreezeCurrent = BalanceFor(userId) }
    });

  public Task<Result<UserProtectionPrincipal>> UpsertProtection(string userId)
    => Task.FromResult<Result<UserProtectionPrincipal>>(new UserProtectionPrincipal
    {
      UserId = userId,
      Record = new UserProtectionRecord { FreezeCurrent = BalanceFor(userId) }
    });
}

// ---------------------------------------------------------------------------
// IEntitlementService
// ---------------------------------------------------------------------------
//
// Not exercised by MarkDailyFailures / SkipHabit / CompleteHabit, but the
// HabitService ctor requires it. Configurable so it can also be reused for
// skip/vacation/freeze entitlement tests.
//   EnsureHabitsAllowedResult  -> the Result<Unit> EnsureHabitsAllowed returns.
//   FreezeCap                  -> the int GetFreezeCapForUser returns.
public sealed class FakeEntitlementService : IEntitlementService
{
  public Result<Unit> EnsureHabitsAllowedResult { get; set; } = new Unit();
  public int FreezeCap { get; set; } = 7;
  public Exception? FreezeCapFails { get; set; }

  public List<(string UserId, int UserMaxStreak)> GetFreezeCapForUserCalls { get; } = [];
  public List<string> EnsureHabitsAllowedCalls { get; } = [];

  public Task<Result<Quota>> HabitQuota(string userId) => throw new NotImplementedException();
  public Task<Result<Quota>> SkipQuota(string userId) => throw new NotImplementedException();
  public Task<Result<Quota>> VacationWindowQuota(string userId) => throw new NotImplementedException();

  public Task<Result<int>> GetFreezeCapForUser(string userId, int userMaxStreak)
  {
    GetFreezeCapForUserCalls.Add((userId, userMaxStreak));
    if (FreezeCapFails is not null) return Task.FromResult<Result<int>>(FreezeCapFails);
    return Task.FromResult<Result<int>>(FreezeCap);
  }

  public Task<Result<Unit>> EnsureHabitsAllowed(string userId)
  {
    EnsureHabitsAllowedCalls.Add(userId);
    return Task.FromResult(EnsureHabitsAllowedResult);
  }

  public Task<Result<Unit>> EnsureProofPluginAllowed(string userId, PluginManifest plugin)
    => throw new NotImplementedException();
}

// ---------------------------------------------------------------------------
// Builders for the domain principals tests need to seed.
// ---------------------------------------------------------------------------
public static class HabitFakeFactory
{
  public static HabitPrincipal Habit(Guid id, string userId, ushort version = 1, bool enabled = true,
    int sortOrder = 0)
    => new()
    {
      Id = id,
      UserId = userId,
      Record = new HabitRecord { Version = version, Enabled = enabled, SortOrder = sortOrder }
    };

  public static HabitVersionPrincipal Version(Guid id, Guid habitId, ushort version = 1,
    string timezone = "UTC", string[]? daysOfWeek = null)
    => new()
    {
      Id = id,
      HabitId = habitId,
      Version = version,
      Record = new HabitVersionRecord
      {
        CharityId = Guid.NewGuid(),
        Task = "task",
        DaysOfWeek = daysOfWeek ?? ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"],
        NotificationTime = new TimeOnly(9, 0),
        Stake = new NodaMoney.Money(10m, NodaMoney.Currency.FromCode("USD")),
        Ratio = 0.5m,
        Timezone = timezone
      }
    };
}

// A plan with one tier; every limit is 0 except the monthly skips.
internal sealed class FakeSubscriptionService(string tier, int skipsMonthlyLimit) : ISubscriptionService
{
  public Task<Result<string>> GetUserTier(string userId)
    => Task.FromResult<Result<string>>(tier);

  public Task<Result<int>> GetLimitForTier(string t, string key)
    => Task.FromResult<Result<int>>(key == EntitlementKeys.SkipsMonthly ? skipsMonthlyLimit : 0);
}
