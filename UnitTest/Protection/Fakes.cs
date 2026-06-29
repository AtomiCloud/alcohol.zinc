using CSharp_Result;
using Domain.Entitlement;
using Domain.Habit;
using Domain.Protection;
using Domain.Vacation;

namespace UnitTest.Protection;

// Shared hand-rolled fakes (no Moq) for unit-testing the Domain HabitService
// skip / freeze / vacation / daily-failure-cron paths. Every method on the
// faked interfaces is implemented; members not exercised by SkipHabit,
// CompleteHabit or MarkDailyFailures(...) throw NotImplementedException, while
// the ones that ARE exercised are backed by simple, seedable, inspectable
// in-memory state.
//
// A single shared monotonically-increasing "call sequence" lets ordering tests
// assert that Vacation inserts precede Freeze inserts precede the Fail step.

public sealed class CallLog
{
  private int _seq;
  public int Next() => ++_seq;
}

// ---------------------------------------------------------------------------
// IHabitRepository
// ---------------------------------------------------------------------------
//
// Seedable state:
//   ActiveVersions          -> returned by GetActiveHabitVersionsByIds
//   Habits                  -> returned by GetHabitsByIds (and GetHabit)
//   CompletedOrSkipped      -> set of habitVersionIds considered "done"
//                              (drives HasAnyCompletedOrSkippedForVersions)
//   ExistingExecutions      -> set of (habitVersionId, date) that already have
//                              an execution row of ANY status. Seed this to
//                              model Completed/Skipped/Vacation/Freeze/Failed
//                              rows so the in-memory CreateFailedExecutions and
//                              CreateExecutionsForVersionsWithStatus anti-joins
//                              skip them, exactly like the real SQL LEFT JOIN.
//   FailedRowTemplates      -> map habitVersionId -> FailedExecutionRow factory
//                              data used to build the rows CreateFailedExecutions
//                              returns for versions that are scheduled today and
//                              have NO existing execution.
//   CurrentDate             -> returned by GetUserCurrentDate (skip/complete)
//   Timezones / HabitIdsByTimezone -> drive the cron entrypoint
//
// Inspectable state (recorded calls, each tagged with a sequence number):
//   CreateExecutionsForVersionsWithStatusCalls
//   CreateFailedExecutionsCalls
//   HasAnyCompletedOrSkippedCalls
//   SkipHabitCalls / CompleteHabitCalls
public sealed class FakeHabitRepository(CallLog? log = null) : IHabitRepository
{
  private readonly CallLog _log = log ?? new CallLog();

  // ----- seedable inputs -----
  public List<HabitVersionPrincipal> ActiveVersions { get; } = [];
  public List<HabitPrincipal> Habits { get; } = [];
  public HashSet<Guid> CompletedOrSkipped { get; } = [];
  public HashSet<(Guid HabitVersionId, DateOnly Date)> ExistingExecutions { get; } = [];

  // For each scheduled habitVersionId that truly misses, what FailedExecutionRow
  // should CreateFailedExecutions produce. ExecutionId/UserId/CharityId/stake/etc.
  public Dictionary<Guid, FailedRowSeed> FailedRowSeeds { get; } = [];

  public DateOnly CurrentDate { get; set; } = new(2026, 1, 1);
  public List<string> Timezones { get; } = [];
  public Dictionary<string, List<Guid>> HabitIdsByTimezone { get; } = [];

  // ----- recorded calls (inspectable) -----
  public List<(int Seq, List<Guid> HvIds, DateOnly Date, ExecutionStatus Status)> CreateExecutionsForVersionsWithStatusCalls { get; } = [];
  public List<(int Seq, List<Guid> HabitIds, DateOnly Date, List<FailedExecutionRow> Returned)> CreateFailedExecutionsCalls { get; } = [];
  public List<(int Seq, List<Guid> HvIds, DateOnly Date)> HasAnyCompletedOrSkippedCalls { get; } = [];
  public List<(string UserId, Guid HabitVersionId, DateOnly Date, string? Notes)> SkipHabitCalls { get; } = [];
  public List<(string UserId, Guid HabitVersionId, DateOnly Date, string? Notes)> CompleteHabitCalls { get; } = [];

  // ---- methods exercised by MarkDailyFailures / Skip / Complete ----

  public Task<Result<List<HabitVersionPrincipal>>> GetActiveHabitVersionsByIds(List<Guid> habitIds, DateOnly date)
  {
    var hs = habitIds.ToHashSet();
    var result = ActiveVersions.Where(v => hs.Contains(v.HabitId)).ToList();
    return Task.FromResult<Result<List<HabitVersionPrincipal>>>(result);
  }

  public Task<Result<List<HabitPrincipal>>> GetHabitsByIds(List<Guid> habitIds)
  {
    var hs = habitIds.ToHashSet();
    var result = Habits.Where(h => hs.Contains(h.Id)).ToList();
    return Task.FromResult<Result<List<HabitPrincipal>>>(result);
  }

  public Task<Result<bool>> HasAnyCompletedOrSkippedForVersions(List<Guid> habitVersionIds, DateOnly date)
  {
    HasAnyCompletedOrSkippedCalls.Add((_log.Next(), habitVersionIds, date));
    var any = habitVersionIds.Any(CompletedOrSkipped.Contains);
    return Task.FromResult<Result<bool>>(any);
  }

  public Task<Result<int>> CreateExecutionsForVersionsWithStatus(List<Guid> habitVersionIds, DateOnly date, ExecutionStatus status)
  {
    CreateExecutionsForVersionsWithStatusCalls.Add((_log.Next(), [.. habitVersionIds], date, status));
    // Mimic the real anti-join: only insert where no execution exists yet for
    // (version, date). Record the freshly inserted rows so subsequent inserts /
    // the fail step see them as existing.
    var inserted = 0;
    foreach (var hv in habitVersionIds)
    {
      if (ExistingExecutions.Add((hv, date))) inserted++;
    }
    return Task.FromResult<Result<int>>(inserted);
  }

  public Task<Result<List<FailedExecutionRow>>> CreateFailedExecutions(List<Guid> habitIds, DateOnly date)
  {
    var seq = _log.Next();
    var hs = habitIds.ToHashSet();
    var rows = new List<FailedExecutionRow>();

    // Anti-join semantics: only fail a scheduled version that has NO existing
    // execution row for that date (so completed / skipped / vacation / frozen
    // rows — modelled via ExistingExecutions — are excluded).
    foreach (var v in ActiveVersions)
    {
      if (!hs.Contains(v.HabitId)) continue;
      if (ExistingExecutions.Contains((v.Id, date))) continue;
      if (!FailedRowSeeds.TryGetValue(v.Id, out var seed)) continue;

      ExistingExecutions.Add((v.Id, date)); // a Failed row now occupies the slot
      rows.Add(seed.ToRow());
    }

    CreateFailedExecutionsCalls.Add((seq, [.. habitIds], date, rows));
    return Task.FromResult<Result<List<FailedExecutionRow>>>(rows);
  }

  public Task<Result<DateOnly>> GetUserCurrentDate(string userId, Guid habitVersionId)
    => Task.FromResult<Result<DateOnly>>(CurrentDate);

  public Task<Result<HabitExecutionPrincipal>> SkipHabit(string userId, Guid habitVersionId, DateOnly date, string? notes)
  {
    SkipHabitCalls.Add((userId, habitVersionId, date, notes));
    return Task.FromResult<Result<HabitExecutionPrincipal>>(
      MakeExecution(habitVersionId, date, ExecutionStatus.Skipped, notes));
  }

  public Task<Result<HabitExecutionPrincipal>> CompleteHabit(string userId, Guid habitVersionId, DateOnly date, string? notes)
  {
    CompleteHabitCalls.Add((userId, habitVersionId, date, notes));
    return Task.FromResult<Result<HabitExecutionPrincipal>>(
      MakeExecution(habitVersionId, date, ExecutionStatus.Completed, notes));
  }

  public Task<Result<List<string>>> GetDistinctTimezonesForEnabledHabits()
    => Task.FromResult<Result<List<string>>>(Timezones.ToList());

  public Task<Result<List<Guid>>> GetEnabledHabitIdsByTimezone(string timezone)
  {
    var ids = HabitIdsByTimezone.TryGetValue(timezone, out var x) ? x.ToList() : new List<Guid>();
    return Task.FromResult<Result<List<Guid>>>(ids);
  }

  public Task<Result<HabitPrincipal?>> GetHabit(Guid habitId)
    => Task.FromResult<Result<HabitPrincipal?>>(Habits.FirstOrDefault(h => h.Id == habitId));

  private static HabitExecutionPrincipal MakeExecution(Guid hvId, DateOnly date, ExecutionStatus status, string? notes)
    => new()
    {
      Id = Guid.NewGuid(),
      HabitVersionId = hvId,
      Record = new HabitExecutionRecord
      {
        Date = date,
        Status = status,
        CompletedAt = status == ExecutionStatus.Completed ? DateTime.UtcNow : null,
        Notes = notes
      }
    };

  // ---- members not exercised by the paths under test ----
  public Task<Result<List<HabitVersionPrincipal>>> GetActiveHabitVersions(string userId, DateOnly date)
    => throw new NotImplementedException();
  public Task<Result<List<HabitVersionPrincipal>>> SearchHabits(HabitSearch habitSearch)
    => throw new NotImplementedException();
  public Task<Result<HabitVersionPrincipal?>> GetCurrentVersion(string userId, Guid habitId)
    => throw new NotImplementedException();
  public Task<Result<HabitVersionPrincipal>> Create(string userId, HabitVersionRecord versionRecord)
    => throw new NotImplementedException();
  public Task<Result<HabitVersionPrincipal?>> Update(Guid habitId, string userId, HabitVersionRecord versionRecord, bool enabled)
    => throw new NotImplementedException();
  public Task<Result<Unit?>> Delete(Guid habitId, string userId)
    => throw new NotImplementedException();
  public Task<Result<List<HabitExecutionPrincipal>>> SearchHabitExecutions(string userId, HabitExecutionSearch habitExecutionSearch)
    => throw new NotImplementedException();
  public Task<Result<List<HabitVersionPrincipal>>> GetVersions(string userId, Guid habitId)
    => throw new NotImplementedException();
  public Task<Result<int>> CountHabitsForUser(string userId)
    => throw new NotImplementedException();
  public Task<Result<int>> CountUserSkipsForMonth(string userId, DateOnly monthStart, DateOnly monthEnd)
    => throw new NotImplementedException();
}

// Seed data for a Failed row produced by CreateFailedExecutions for a true miss.
// amountCents in the service = StakeCents * RatioBasisPoints / 10000.
public sealed record FailedRowSeed
{
  public Guid ExecutionId { get; init; } = Guid.NewGuid();
  public required string UserId { get; init; }
  public Guid CharityId { get; init; } = Guid.NewGuid();
  public int StakeCents { get; init; } = 1000;        // $10.00
  public string StakeCurrency { get; init; } = "USD";
  public int RatioBasisPoints { get; init; } = 5000;  // 50%

  public FailedExecutionRow ToRow()
    => new(ExecutionId, UserId, CharityId, StakeCents, StakeCurrency, RatioBasisPoints);
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
    var list = ActiveByUser.TryGetValue(userId, out var v) ? v.ToList() : new List<VacationPrincipal>();
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
  public Task<Result<VacationPrincipal>> Create(string userId, VacationRecord record) => throw new NotImplementedException();
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
//   FreezeBalance is the in-memory pool. TryConsumeFreeze decrements it by 1 and
//   returns true while > 0; returns false (no decrement) at 0. It is idempotent
//   per date: a date already consumed returns true WITHOUT decrementing again.
// Inspectable: TryConsumeFreezeCalls, ConsumedDates, IncrementFreezeCalls,
//   ClampFreezeToCapCalls, RecordFreezeAwardIfAbsentCalls.
public sealed class FakeProtectionRepository(int freezeBalance = 0) : IProtectionRepository
{
  public int FreezeBalance { get; set; } = freezeBalance;

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
    if (FreezeBalance <= 0)
      return Task.FromResult<Result<bool>>(false);
    FreezeBalance -= 1;
    ConsumedDates.Add((userId, date));
    return Task.FromResult<Result<bool>>(true);
  }

  public Task<Result<Unit>> IncrementFreeze(string userId, int n)
  {
    IncrementFreezeCalls.Add((userId, n));
    FreezeBalance += Math.Max(0, n); // cap-blind, like the real repo
    return Task.FromResult<Result<Unit>>(new Unit());
  }

  public Task<Result<Unit>> ClampFreezeToCap(string userId, int cap)
  {
    ClampFreezeToCapCalls.Add((userId, cap));
    if (FreezeBalance > cap) FreezeBalance = cap;
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
      Record = new UserProtectionRecord { FreezeCurrent = FreezeBalance }
    });

  public Task<Result<UserProtectionPrincipal>> UpsertProtection(string userId)
    => Task.FromResult<Result<UserProtectionPrincipal>>(new UserProtectionPrincipal
    {
      UserId = userId,
      Record = new UserProtectionRecord { FreezeCurrent = FreezeBalance }
    });
}

// ---------------------------------------------------------------------------
// IEntitlementService
// ---------------------------------------------------------------------------
//
// Not exercised by MarkDailyFailures / SkipHabit / CompleteHabit, but the
// HabitService ctor requires it. Configurable so it can also be reused for
// skip/vacation/freeze entitlement tests.
//   EnsureSkipsAllowedResult / EnsureVacationWindowAllowedResult /
//   EnsureHabitsAllowedResult  -> the Result<Unit> each Ensure* returns.
//   FreezeCap                  -> the int GetFreezeCapForUser returns.
public sealed class FakeEntitlementService : IEntitlementService
{
  public Result<Unit> EnsureSkipsAllowedResult { get; set; } = new Unit();
  public Result<Unit> EnsureVacationWindowAllowedResult { get; set; } = new Unit();
  public Result<Unit> EnsureHabitsAllowedResult { get; set; } = new Unit();
  public int FreezeCap { get; set; } = 7;

  public List<(string UserId, DateOnly MonthStart, DateOnly MonthEnd)> EnsureSkipsAllowedCalls { get; } = [];
  public List<(string UserId, DateOnly StartDate)> EnsureVacationWindowAllowedCalls { get; } = [];
  public List<(string UserId, int UserMaxStreak)> GetFreezeCapForUserCalls { get; } = [];
  public List<string> EnsureHabitsAllowedCalls { get; } = [];

  public Task<Result<Unit>> EnsureSkipsAllowed(string userId, DateOnly monthStart, DateOnly monthEnd)
  {
    EnsureSkipsAllowedCalls.Add((userId, monthStart, monthEnd));
    return Task.FromResult(EnsureSkipsAllowedResult);
  }

  public Task<Result<Unit>> EnsureVacationWindowAllowed(string userId, DateOnly startDate)
  {
    EnsureVacationWindowAllowedCalls.Add((userId, startDate));
    return Task.FromResult(EnsureVacationWindowAllowedResult);
  }

  public Task<Result<int>> GetFreezeCapForUser(string userId, int userMaxStreak)
  {
    GetFreezeCapForUserCalls.Add((userId, userMaxStreak));
    return Task.FromResult<Result<int>>(FreezeCap);
  }

  public Task<Result<Unit>> EnsureHabitsAllowed(string userId)
  {
    EnsureHabitsAllowedCalls.Add(userId);
    return Task.FromResult(EnsureHabitsAllowedResult);
  }
}

// ---------------------------------------------------------------------------
// Builders for the domain principals tests need to seed.
// ---------------------------------------------------------------------------
public static class HabitFakeFactory
{
  public static HabitPrincipal Habit(Guid id, string userId, ushort version = 1, bool enabled = true)
    => new()
    {
      Id = id,
      UserId = userId,
      Record = new HabitRecord { Version = version, Enabled = enabled }
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
