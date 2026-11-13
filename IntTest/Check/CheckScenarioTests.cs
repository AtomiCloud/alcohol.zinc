using App.Modules.Charities.Data;
using App.Modules.Check.Data;
using App.Modules.Configurations.Data;
using App.Modules.Habit.Data;
using App.Modules.HabitExecution.Data;
using App.Modules.HabitVersion.Data;
using App.Modules.NfcTag.Data;
using App.Modules.Proof.Data;
using App.Modules.Protection.Data;
using App.Modules.Routine.Data;
using App.Modules.Users.Data;
using App.Modules.Vacation.Data;
using App.StartUp.Database;
using CSharp_Result;
using Domain.Allowance;
using Domain.Check;
using Domain.Habit;
using Domain.Subscription;
using IntTest.Penalty;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace IntTest.Check;

// End-to-end scenarios for the core: daily checks, the user's timezone that follows the
// phone, protections, Report and the stake outbox. A fake clock (explicit nowUtc) drives
// the real CheckService against a real Postgres (PENALTY_TEST_DB), as the scheduler would.
[Collection(DbIntegrationCollection.Name)]
public class CheckScenarioTests : IAsyncLifetime
{
  private const string SkipReason = "PENALTY_TEST_DB not set; skipping DB-backed check scenarios.";
  private const string Singapore = "Asia/Singapore";
  private const string LosAngeles = "America/Los_Angeles";
  private readonly string? _conn = Environment.GetEnvironmentVariable("PENALTY_TEST_DB");
  private MainDbContext _db = null!;

  public async Task InitializeAsync()
  {
    if (_conn == null) return;
    _db = PenaltyIntegrationTests.NewContext(_conn);
    await _db.Database.EnsureCreatedAsync();
  }

  public async Task DisposeAsync()
  {
    if (_conn == null) return;
    await _db.Database.EnsureDeletedAsync();
    await _db.DisposeAsync();
  }

  private static DateTime U(int y, int m, int d, int h, int min = 0) => new(y, m, d, h, min, 0, DateTimeKind.Utc);

  private async Task<(string UserId, Guid CharityId)> SeedUser(string timezone, int freezes = 0)
  {
    var userId = $"user-{Guid.NewGuid():N}";
    var charityId = Guid.NewGuid();
    _db.Users.Add(new UserData { Id = userId, Username = userId, Email = $"{userId}@test.local" });
    _db.Charities.Add(new CharityData { Id = charityId, Name = $"Charity {charityId:N}" });
    _db.Configurations.Add(new ConfigurationData { UserId = userId, Timezone = timezone, DefaultCharityId = charityId });
    if (freezes > 0) _db.UserProtections.Add(new UserProtectionData { UserId = userId, FreezeCurrent = freezes });
    await _db.SaveChangesAsync();
    return (userId, charityId);
  }

  private async Task<Guid> SeedHabit(string userId, Guid charityId, string[] days, string versionTimezone,
    TimeOnly? deadline = null)
  {
    var habitId = Guid.NewGuid();
    _db.Habits.Add(new HabitData { Id = habitId, UserId = userId, Version = 1, Enabled = true });
    _db.HabitVersions.Add(new HabitVersionData
    {
      HabitId = habitId, CharityId = charityId, Version = 1, Task = "Run 5 km", DaysOfWeek = days,
      NotificationTime = new TimeOnly(19, 0), DeadlineTime = deadline ?? new TimeOnly(23, 59),
      Timezone = versionTimezone,
      StakeCents = 1000, StakeCurrency = "USD", RatioBasisPoints = 10000
    });
    await _db.SaveChangesAsync();
    return habitId;
  }

  private async Task MoveUserTo(string userId, string timezone)
  {
    await _db.Configurations.Where(c => c.UserId == userId)
      .ExecuteUpdateAsync(s => s.SetProperty(c => c.Timezone, timezone));
  }

  private Task<List<HabitCheckData>> ChecksOf(Guid habitId)
    => _db.HabitChecks.AsNoTracking().Where(c => c.HabitId == habitId).OrderBy(c => c.LocalDate).ToListAsync();

  private Task<List<HabitExecutionData>> ExecutionsOf(Guid habitId)
    => _db.HabitExecutions.AsNoTracking().Where(e => e.HabitVersion!.HabitId == habitId).ToListAsync();

  private Task<int> PenaltiesOf(string userId) => _db.Penalties.CountAsync(p => p.UserId == userId);

  // Created in Singapore, then the user is in San Francisco: Monday is Monday in SF.
  // Before checks, Singapore's Monday ended at Mon 08:00 SF and a 19:05 SF tap was rejected.
  [SkippableFact]
  public async Task SingaporeToSanFrancisco_MondayTapAt1905SF_IsDone()
  {
    Skip.If(_conn == null, SkipReason);
    var (userId, charityId) = await SeedUser(Singapore);
    var habitId = await SeedHabit(userId, charityId, ["Monday"], Singapore);
    var svc = Checks.Service(_db);

    (await svc.OpenUpcoming(U(2026, 1, 4, 17))).Get().Should().Be(1); // Mon 01:00 in Singapore
    await MoveUserTo(userId, LosAngeles);
    (await svc.MoveToTimezone(userId, U(2026, 1, 5, 2))).Get().Should().BeEmpty(); // Sun 18:00 SF

    var monday = (await ChecksOf(habitId)).Single();
    monday.Timezone.Should().Be(LosAngeles);
    monday.OpensAtUtc.Should().Be(U(2026, 1, 5, 8));
    monday.DeadlineUtc.Should().Be(U(2026, 1, 6, 8));

    (await svc.CloseDue(U(2026, 1, 5, 16, 45))).Get().Should().Be(0, "Mon 08:45 SF is not the end of Monday");
    var done = (await svc.Report(userId, habitId, Checks.Manual(U(2026, 1, 6, 3, 5)), U(2026, 1, 6, 3, 5))).Get();
    done.Should().NotBeNull();
    done!.Record.Date.Should().Be(new DateOnly(2026, 1, 5));
    done.Record.Status.Should().Be(Domain.Habit.ExecutionStatus.Completed);

    (await svc.CloseDue(U(2026, 1, 7, 12))).Get().Should().Be(0);
    (await svc.DispatchEvents(U(2026, 1, 7, 12))).IsSuccess().Should().BeTrue();
    (await PenaltiesOf(userId)).Should().Be(0);
  }

  // Done Mon 16:00 in Singapore, then land Mon 16:00 in SF: Monday stays done, once.
  [SkippableFact]
  public async Task DoneInSingapore_LandMondayInSF_NoSecondMondayAndTuesdayOnSFTime()
  {
    Skip.If(_conn == null, SkipReason);
    var (userId, charityId) = await SeedUser(Singapore);
    var habitId = await SeedHabit(userId, charityId, ["Monday", "Tuesday"], Singapore);
    var svc = Checks.Service(_db);

    (await svc.OpenUpcoming(U(2026, 1, 5, 8))).Get().Should().Be(2);
    (await svc.Report(userId, habitId, Checks.Manual(U(2026, 1, 5, 8)), U(2026, 1, 5, 8))).Get()
      .Should().NotBeNull();

    await MoveUserTo(userId, LosAngeles);
    await svc.MoveToTimezone(userId, U(2026, 1, 5, 9));
    var landed = U(2026, 1, 6, 0); // Mon 16:00 SF
    (await svc.OpenUpcoming(landed)).Get().Should().Be(0, "Monday already has its check");
    (await svc.Report(userId, habitId, Checks.Manual(landed), landed)).Get().Should().BeNull();

    var checks = await ChecksOf(habitId);
    checks.Should().HaveCount(2);
    checks[0].Status.Should().Be(HabitCheckStatusData.Done);
    checks[0].Timezone.Should().Be(Singapore, "a settled check never changes");
    checks[1].Timezone.Should().Be(LosAngeles);
    checks[1].DeadlineUtc.Should().Be(U(2026, 1, 7, 8));
    (await ExecutionsOf(habitId)).Should().ContainSingle();
  }

  // SF -> Singapore: Tuesday already ended in Singapore when the phone syncs on landing. The End
  // follows the phone, so Tuesday is settled by the change itself: a freeze if one is left, else missed.
  [SkippableTheory]
  [InlineData(1, CheckStatus.Frozen, HabitCheckStatusData.Frozen, 0)]
  [InlineData(0, CheckStatus.Missed, HabitCheckStatusData.Missed, 1)]
  public async Task SanFranciscoToSingapore_TuesdayEndedOnLanding_SettledByTheChange(int freezes,
    CheckStatus expected, HabitCheckStatusData stored, int penalties)
  {
    Skip.If(_conn == null, SkipReason);
    var (userId, charityId) = await SeedUser(LosAngeles, freezes);
    var habitId = await SeedHabit(userId, charityId, ["Tuesday"], LosAngeles);
    var svc = Checks.Service(_db);

    await svc.OpenUpcoming(U(2026, 1, 6, 10)); // Tue 02:00 SF
    await MoveUserTo(userId, Singapore);
    var landed = U(2026, 1, 7, 0); // Wed 08:00 Singapore
    var settled = (await svc.MoveToTimezone(userId, landed)).Get();

    settled.Should().Equal(new SettledCheck(habitId, new DateOnly(2026, 1, 6), expected));
    var tuesday = (await ChecksOf(habitId)).Single();
    tuesday.Timezone.Should().Be(Singapore);
    tuesday.DeadlineUtc.Should().Be(U(2026, 1, 6, 16), "Tuesday ends at midnight Singapore time");
    tuesday.Status.Should().Be(stored);

    (await svc.MoveToTimezone(userId, landed.AddMinutes(1))).Get().Should().BeEmpty("a day settles once");
    (await svc.Report(userId, habitId, Checks.Manual(landed), landed)).Get().Should().BeNull();
    await svc.DispatchEvents(landed.AddMinutes(5));
    (await PenaltiesOf(userId)).Should().Be(penalties);
  }

  // A habit edit is not a timezone change: a started day keeps its End when the deadline time moves earlier.
  [SkippableFact]
  public async Task HabitDeadlineMovedEarlier_StartedDayKeepsItsEnd()
  {
    Skip.If(_conn == null, SkipReason);
    var (userId, charityId) = await SeedUser(LosAngeles);
    var habitId = await SeedHabit(userId, charityId, ["Tuesday"], LosAngeles);
    var svc = Checks.Service(_db);

    await svc.OpenUpcoming(U(2026, 1, 6, 18)); // Tue 10:00 SF
    await _db.HabitVersions.Where(v => v.HabitId == habitId)
      .ExecuteUpdateAsync(x => x.SetProperty(v => v.DeadlineTime, new TimeOnly(8, 0)));
    await svc.RetimePending(userId, [habitId], U(2026, 1, 6, 18));

    (await ChecksOf(habitId)).Single().DeadlineUtc.Should().Be(U(2026, 1, 7, 8), "Tuesday 23:59 SF stays");
  }

  // DST change days in Los Angeles: one check of 25 h / 23 h that settles exactly once.
  [SkippableTheory]
  [InlineData(2026, 11, 1, 7, 2026, 11, 2, 8)]
  [InlineData(2026, 3, 8, 8, 2026, 3, 9, 7)]
  public async Task DstDay_OneCheck_SettlesOnce_OnePenalty(int y, int m, int d, int openHour,
    int dy, int dm, int dd, int deadlineHour)
  {
    Skip.If(_conn == null, SkipReason);
    var (userId, charityId) = await SeedUser(LosAngeles);
    var habitId = await SeedHabit(userId, charityId, ["Sunday"], LosAngeles);
    var svc = Checks.Service(_db);
    var opens = U(y, m, d, openHour);
    var deadline = U(dy, dm, dd, deadlineHour);

    await svc.OpenUpcoming(opens.AddHours(1));
    var check = (await ChecksOf(habitId)).Single();
    check.LocalDate.Should().Be(new DateOnly(y, m, d));
    check.OpensAtUtc.Should().Be(opens);
    check.DeadlineUtc.Should().Be(deadline);

    (await svc.CloseDue(deadline.AddMinutes(29))).Get().Should().Be(0, "still inside the grace period");
    (await svc.CloseDue(deadline.AddMinutes(31))).Get().Should().Be(1);
    (await svc.CloseDue(deadline.AddMinutes(46))).Get().Should().Be(0);
    await svc.DispatchEvents(deadline.AddHours(1));
    await svc.DispatchEvents(deadline.AddHours(2));

    var execution = (await ExecutionsOf(habitId)).Single();
    execution.Status.Should().Be(HabitExecutionStatusData.Failed);
    (await PenaltiesOf(userId)).Should().Be(1);
  }

  // Habits saved in two timezones now share the user's day: one freeze covers what's missing.
  [SkippableFact]
  public async Task HabitsFromTwoTimezones_OneDone_FreezeCoversTheOther_Once()
  {
    Skip.If(_conn == null, SkipReason);
    var (userId, charityId) = await SeedUser(Singapore, freezes: 1);
    var done = await SeedHabit(userId, charityId, ["Monday"], Singapore);
    var missed = await SeedHabit(userId, charityId, ["Monday"], "America/New_York");
    var svc = Checks.Service(_db);

    await svc.OpenUpcoming(U(2026, 1, 5, 1));
    (await ChecksOf(missed)).Single().LocalDate.Should().Be(new DateOnly(2026, 1, 5));
    (await svc.Report(userId, done, Checks.Manual(U(2026, 1, 5, 2)), U(2026, 1, 5, 2))).Get().Should().NotBeNull();

    (await svc.CloseDue(U(2026, 1, 5, 17))).Get().Should().Be(1);
    (await svc.CloseDue(U(2026, 1, 6, 17))).Get().Should().Be(0);
    await svc.DispatchEvents(U(2026, 1, 6, 17));

    (await ExecutionsOf(done)).Single().Status.Should().Be(HabitExecutionStatusData.Completed);
    (await ExecutionsOf(missed)).Single().Status.Should().Be(HabitExecutionStatusData.Frozen);
    (await _db.UserProtections.AsNoTracking().SingleAsync(x => x.UserId == userId)).FreezeCurrent.Should().Be(0);
    (await _db.FreezeConsumptions.CountAsync(x => x.UserId == userId)).Should().Be(1);
    (await PenaltiesOf(userId)).Should().Be(0);
  }

  // A fully recorded day (e.g. written by a pod on the previous release) never spends a freeze.
  [SkippableFact]
  public async Task DayAlreadyRecordedElsewhere_IsAdopted_WithoutFreezeOrPenalty()
  {
    Skip.If(_conn == null, SkipReason);
    var (userId, charityId) = await SeedUser("UTC", freezes: 2);
    var habitId = await SeedHabit(userId, charityId, ["Monday"], "UTC");
    var svc = Checks.Service(_db);
    await svc.OpenUpcoming(U(2026, 1, 5, 1));
    var versionId = await _db.HabitVersions.Where(v => v.HabitId == habitId).Select(v => v.Id).SingleAsync();
    _db.HabitExecutions.Add(new HabitExecutionData
    {
      HabitVersionId = versionId, Date = new DateOnly(2026, 1, 5), Status = HabitExecutionStatusData.Skipped
    });
    await _db.SaveChangesAsync();

    (await svc.CloseDue(U(2026, 1, 6, 1))).Get().Should().Be(0);
    await svc.DispatchEvents(U(2026, 1, 6, 1));

    var check = (await ChecksOf(habitId)).Single();
    check.Status.Should().Be(HabitCheckStatusData.Skipped);
    check.SettledBy.Should().Be("adopted");
    (await ExecutionsOf(habitId)).Should().ContainSingle();
    (await _db.UserProtections.AsNoTracking().SingleAsync(x => x.UserId == userId)).FreezeCurrent.Should().Be(2);
    (await PenaltiesOf(userId)).Should().Be(0);
  }

  private Task<int> FreezesOf(string userId)
    => _db.UserProtections.AsNoTracking().Where(x => x.UserId == userId).Select(x => x.FreezeCurrent).SingleAsync();

  // A UTC user with `freezes` banked, on a plan with base cap `freezeBase` (no streak), misses Monday.
  private async Task<(HabitCheckData Monday, int Freezes)> MissMonday(int freezes, int freezeBase)
  {
    var (userId, charityId) = await SeedUser("UTC", freezes);
    var habitId = await SeedHabit(userId, charityId, ["Monday"], "UTC");
    var svc = Checks.Service(_db, new Checks.FreezeBase(freezeBase));

    await svc.OpenUpcoming(U(2026, 1, 5, 1));
    (await svc.CloseDue(U(2026, 1, 6, 1))).Get().Should().Be(1);
    return ((await ChecksOf(habitId)).Single(), await FreezesOf(userId));
  }

  // Freezes banked on a paid plan are cut to the free cap (0) before one would be spent.
  [SkippableFact]
  public async Task MissedDay_BalanceAboveZeroCap_IsMissedAndBalanceCut()
  {
    Skip.If(_conn == null, SkipReason);
    var (monday, freezes) = await MissMonday(freezes: 5, freezeBase: 0);

    monday.Status.Should().Be(HabitCheckStatusData.Missed, "a cap of 0 leaves no freeze to spend");
    freezes.Should().Be(0);
  }

  [SkippableFact]
  public async Task MissedDay_BalanceAboveCap_FreezesAndSpendsFromCutBalance()
  {
    Skip.If(_conn == null, SkipReason);
    var (monday, freezes) = await MissMonday(freezes: 5, freezeBase: 2);

    monday.Status.Should().Be(HabitCheckStatusData.Frozen);
    freezes.Should().Be(1, "cut to 2, then 1 spent");
  }

  [SkippableFact]
  public async Task MissedDay_BalanceUnderCap_Unchanged()
  {
    Skip.If(_conn == null, SkipReason);
    var (monday, freezes) = await MissMonday(freezes: 2, freezeBase: 3);

    monday.Status.Should().Be(HabitCheckStatusData.Frozen);
    freezes.Should().Be(1);
  }

  // Two checks of one day settle in separate ticks. The second finds the balance at 0 and the
  // day already frozen: it stays frozen, and skips the cap (whose streak query loads every
  // execution of the user). The cap lookup is down for that tick, so a reconcile would fail
  // and leave the check pending.
  [SkippableFact]
  public async Task SecondCheckOfFrozenDay_StaysFrozen()
  {
    Skip.If(_conn == null, SkipReason);
    var (userId, charityId) = await SeedUser("UTC", freezes: 1);
    var noon = await SeedHabit(userId, charityId, ["Monday"], "UTC", deadline: new TimeOnly(12, 0));
    var midnight = await SeedHabit(userId, charityId, ["Monday"], "UTC");
    var svc = Checks.Service(_db, new Checks.FreezeBase(3));

    await svc.OpenUpcoming(U(2026, 1, 5, 1));
    (await svc.CloseDue(U(2026, 1, 5, 13))).Get().Should().Be(1, "only the noon check is due");
    (await FreezesOf(userId)).Should().Be(0);

    (await Checks.Service(_db, new PlanLookupDown()).CloseDue(U(2026, 1, 6, 1))).Get().Should().Be(1);

    (await ChecksOf(noon)).Single().Status.Should().Be(HabitCheckStatusData.Frozen);
    (await ChecksOf(midnight)).Single().Status.Should().Be(HabitCheckStatusData.Frozen);
    (await _db.FreezeConsumptions.CountAsync(x => x.UserId == userId)).Should().Be(1);
  }

  // Reading the balance cuts it to the cap: GET Protection/{userId} never shows more than the
  // cap, and what it shows is what is stored.
  [SkippableTheory]
  [InlineData(5, 2, 2)]
  [InlineData(5, 0, 0)]
  [InlineData(1, 3, 1)]
  public async Task GetBalance_ReturnsBalanceCutToCap_AndStoresIt(int freezes, int freezeBase, int expected)
  {
    Skip.If(_conn == null, SkipReason);
    var (userId, _) = await SeedUser("UTC", freezes);
    var controller = new App.Modules.Protection.API.V1.ProtectionController(null!,
      Checks.FreezeCap(_db, new Checks.FreezeBase(freezeBase)), null!)
    {
      ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
      {
        HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
        {
          User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, userId)], "test"))
        }
      }
    };

    var response = await controller.GetBalance(userId);

    var ok = response.Result.Should().BeOfType<Microsoft.AspNetCore.Mvc.OkObjectResult>().Subject;
    ok.Value.Should().Be(new App.Modules.Protection.API.V1.ProtectionController.ProtectionBalanceRes(
      userId, expected, freezeBase));
    (await FreezesOf(userId)).Should().Be(expected);
  }

  // Vacation protects every scheduled habit of the day before any freeze is considered.
  [SkippableFact]
  public async Task Vacation_ProtectsAllHabits_KeepsFreezes_NoPenalty()
  {
    Skip.If(_conn == null, SkipReason);
    var (userId, charityId) = await SeedUser(Singapore, freezes: 2);
    var a = await SeedHabit(userId, charityId, ["Monday"], Singapore);
    var b = await SeedHabit(userId, charityId, ["Monday"], LosAngeles);
    _db.VacationPeriods.Add(new VacationPeriodData
    {
      UserId = userId, StartDate = new DateOnly(2026, 1, 5), EndDate = new DateOnly(2026, 1, 5), Timezone = Singapore
    });
    await _db.SaveChangesAsync();
    var svc = Checks.Service(_db);

    await svc.OpenUpcoming(U(2026, 1, 5, 1));
    (await svc.CloseDue(U(2026, 1, 5, 17))).Get().Should().Be(2);
    await svc.DispatchEvents(U(2026, 1, 5, 17));

    (await ExecutionsOf(a)).Single().Status.Should().Be(HabitExecutionStatusData.Vacation);
    (await ExecutionsOf(b)).Single().Status.Should().Be(HabitExecutionStatusData.Vacation);
    (await _db.UserProtections.AsNoTracking().SingleAsync(x => x.UserId == userId)).FreezeCurrent.Should().Be(2);
    (await PenaltiesOf(userId)).Should().Be(0);
  }

  // Evidence that happened before the deadline lands if it arrives within the grace period.
  [SkippableTheory]
  [InlineData(20, true)]
  [InlineData(40, false)]
  public async Task LateEvidence_InsideGraceIsDone_OutsideIsMissed(int minutesAfterDeadline, bool expectDone)
  {
    Skip.If(_conn == null, SkipReason);
    var (userId, charityId) = await SeedUser("UTC");
    var habitId = await SeedHabit(userId, charityId, ["Monday"], "UTC");
    var svc = Checks.Service(_db);
    var deadline = U(2026, 1, 6, 0);
    await svc.OpenUpcoming(U(2026, 1, 5, 1));

    var occurred = deadline.AddMinutes(-5);
    var arrived = deadline.AddMinutes(minutesAfterDeadline);
    var result = (await svc.Report(userId, habitId, Checks.Manual(occurred), arrived)).Get();
    (result != null).Should().Be(expectDone);
    if (expectDone) result!.Record.CompletedAt.Should().Be(occurred);

    (await svc.CloseDue(deadline.AddMinutes(45))).Get().Should().Be(expectDone ? 0 : 1);
    await svc.DispatchEvents(deadline.AddMinutes(45));
    (await PenaltiesOf(userId)).Should().Be(expectDone ? 0 : 1);
  }

  [SkippableFact]
  public async Task ReplayedEvidence_ChangesNothing()
  {
    Skip.If(_conn == null, SkipReason);
    var (userId, charityId) = await SeedUser("UTC");
    var habitId = await SeedHabit(userId, charityId, ["Monday", "Tuesday"], "UTC");
    var svc = Checks.Service(_db);
    var tap = new Evidence
    {
      ProofKey = "nfc", ExternalId = "tap-1", Verdict = EvidenceVerdict.Passed, OccurredAtUtc = U(2026, 1, 5, 10)
    };

    (await svc.Report(userId, habitId, tap, U(2026, 1, 5, 10))).Get().Should().NotBeNull();
    (await svc.Report(userId, habitId, tap, U(2026, 1, 5, 10, 1))).Get().Should().BeNull();
    // The same tap id replayed on Tuesday still changes nothing.
    (await svc.Report(userId, habitId, tap with { OccurredAtUtc = U(2026, 1, 6, 10) }, U(2026, 1, 6, 10)))
      .Get().Should().BeNull();

    (await _db.HabitEvidence.CountAsync(x => x.HabitId == habitId)).Should().Be(1);
    (await ExecutionsOf(habitId)).Should().ContainSingle();
  }

  // Failed evidence (e.g. a screen-time limit broken) settles the day as missed right away.
  [SkippableFact]
  public async Task FailedEvidence_MissesImmediately_StakeFiresOnce()
  {
    Skip.If(_conn == null, SkipReason);
    var (userId, charityId) = await SeedUser("UTC");
    var habitId = await SeedHabit(userId, charityId, ["Monday"], "UTC");
    var svc = Checks.Service(_db);
    var broken = new Evidence
    {
      ProofKey = "screen-time", ExternalId = "2026-01-05", Verdict = EvidenceVerdict.Failed,
      OccurredAtUtc = U(2026, 1, 5, 14)
    };

    var missed = (await svc.Report(userId, habitId, broken, U(2026, 1, 5, 14))).Get();
    missed!.Record.Status.Should().Be(Domain.Habit.ExecutionStatus.Failed);
    (await ChecksOf(habitId)).Single().Status.Should().Be(HabitCheckStatusData.Missed);
    (await svc.Report(userId, habitId, Checks.Manual(U(2026, 1, 5, 15)), U(2026, 1, 5, 15))).Get().Should().BeNull();

    await svc.DispatchEvents(U(2026, 1, 5, 15));
    (await svc.CloseDue(U(2026, 1, 6, 1))).Get().Should().Be(0);
    await svc.DispatchEvents(U(2026, 1, 6, 1));
    (await PenaltiesOf(userId)).Should().Be(1);
  }

  // Skips use the user's day, so they count in the month the user saw.
  [SkippableFact]
  public async Task SkipOnLastAndFirstDayOfMonth_CountsInEachMonth_WithoutStake()
  {
    Skip.If(_conn == null, SkipReason);
    var (userId, charityId) = await SeedUser("UTC");
    var habitId = await SeedHabit(userId, charityId,
      ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"], "UTC");
    var svc = Checks.Service(_db);

    // No scheduler tick ran: Skip opens the day's check on demand.
    (await svc.Skip(userId, habitId, "sick", U(2026, 1, 31, 23))).Get()!.Record.Date.Should().Be(new DateOnly(2026, 1, 31));
    (await svc.Skip(userId, habitId, null, U(2026, 2, 1, 1))).Get()!.Record.Date.Should().Be(new DateOnly(2026, 2, 1));
    (await svc.Skip(userId, habitId, null, U(2026, 2, 1, 2))).Get().Should().BeNull("the day is already skipped");
    await svc.DispatchEvents(U(2026, 2, 1, 3));

    var habits = new HabitRepository(_db, NullLogger<HabitRepository>.Instance);
    (await habits.CountUserSkipsForMonth(userId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31))).Get().Should().Be(1);
    (await habits.CountUserSkipsForMonth(userId, new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28))).Get().Should().Be(1);
    (await PenaltiesOf(userId)).Should().Be(0);
  }

  // A habit disabled after its check opened owes nothing for that day.
  [SkippableFact]
  public async Task HabitDisabledAfterOpening_IsNotMissed()
  {
    Skip.If(_conn == null, SkipReason);
    var (userId, charityId) = await SeedUser("UTC");
    var habitId = await SeedHabit(userId, charityId, ["Monday"], "UTC");
    var svc = Checks.Service(_db);
    await svc.OpenUpcoming(U(2026, 1, 5, 1));
    await _db.Habits.Where(h => h.Id == habitId).ExecuteUpdateAsync(s => s.SetProperty(h => h.Enabled, false));

    (await svc.CloseDue(U(2026, 1, 6, 1))).Get().Should().Be(0);
    (await ChecksOf(habitId)).Should().BeEmpty();
    (await ExecutionsOf(habitId)).Should().BeEmpty();
  }

  private sealed class FlakyBinding : IStakeBinding
  {
    public int Calls;
    public string BindingId => "flaky";
    public bool AppliesTo(CheckEventPrincipal e) => true;

    public Task<Result<Unit>> Deliver(CheckEventPrincipal e)
    {
      Calls++;
      return Task.FromResult(Calls == 1 ? (Result<Unit>)new Exception("stake plugin down") : (Result<Unit>)new Unit());
    }
  }

  // The outbox survives a crash between settle and dispatch, retries with backoff, and
  // delivers to each binding at most once.
  [SkippableFact]
  public async Task Outbox_SurvivesRestart_RetriesFailedBinding_DeliversEachOnce()
  {
    Skip.If(_conn == null, SkipReason);
    var (userId, charityId) = await SeedUser("UTC");
    var habitId = await SeedHabit(userId, charityId, ["Monday"], "UTC");
    await Checks.Service(_db).OpenUpcoming(U(2026, 1, 5, 1));
    (await Checks.Service(_db).CloseDue(U(2026, 1, 6, 1))).Get().Should().Be(1);

    // "Restart": a fresh context and service pick up the undelivered event.
    await using var db2 = PenaltyIntegrationTests.NewContext(_conn!);
    var flaky = new FlakyBinding();
    var svc = Checks.Service(db2, flaky);
    var t = U(2026, 1, 6, 2);
    (await svc.DispatchEvents(t)).Get().Should().Be(0);
    (await svc.DispatchEvents(t.AddMinutes(1))).Get().Should().Be(0, "backing off");
    (await svc.DispatchEvents(t.AddMinutes(5))).Get().Should().Be(1);
    (await svc.DispatchEvents(t.AddMinutes(30))).Get().Should().Be(0);

    flaky.Calls.Should().Be(2);
    (await PenaltiesOf(userId)).Should().Be(1);
    var ev = await _db.CheckEvents.AsNoTracking().SingleAsync(e => e.HabitId == habitId);
    ev.DispatchedAtUtc.Should().NotBeNull();
    (await _db.CheckEventDeliveries.CountAsync(d => d.EventId == ev.Id)).Should().Be(2);
  }

  // The overview gives each habit its deadline, today's check End, its proof and its routine.
  // Today's End is the stored check's (here re-timed), not recomputed from the deadline time.
  [SkippableFact]
  public async Task Overview_DeadlineProofAndRoutine_PerHabit()
  {
    Skip.If(_conn == null, SkipReason);
    var (userId, charityId) = await SeedUser(LosAngeles);
    var tagged = await SeedHabit(userId, charityId, ["Monday"], LosAngeles);
    var offDay = await SeedHabit(userId, charityId, ["Tuesday"], LosAngeles);
    var routineId = Guid.NewGuid();
    _db.Routines.Add(new RoutineData { Id = routineId, UserId = userId, Name = "Morning" });
    _db.NfcTags.Add(new NfcTagData { Id = $"tag-{Guid.NewGuid():N}", UserId = userId, HabitId = tagged });
    // Tagged and bound to a plugin: the plugin is the proof the overview reports.
    var bound = await SeedHabit(userId, charityId, ["Monday"], LosAngeles);
    _db.NfcTags.Add(new NfcTagData { Id = $"tag-{Guid.NewGuid():N}", UserId = userId, HabitId = bound });
    _db.ProofBindings.Add(new ProofBindingData
    {
      HabitId = bound, PluginKey = "strava", PluginVersion = 1, Settings = """{"sportTypes":["Run"]}"""
    });
    await _db.SaveChangesAsync();
    await _db.Habits.Where(h => h.Id == tagged).ExecuteUpdateAsync(x => x.SetProperty(h => h.RoutineId, routineId));
    await _db.HabitVersions.Where(v => v.HabitId == tagged)
      .ExecuteUpdateAsync(x => x.SetProperty(v => v.DeadlineTime, new TimeOnly(20, 0)));

    var now = U(2026, 1, 5, 18); // Mon 10:00 in Los Angeles
    var checks = Checks.Service(_db);
    await checks.OpenUpcoming(now);
    var retimedEnd = U(2026, 1, 6, 6, 30);
    await _db.HabitChecks.Where(c => c.HabitId == tagged)
      .ExecuteUpdateAsync(x => x.SetProperty(c => c.DeadlineUtc, retimedEnd));

    var overview = Overview(checks);
    var res = App.Modules.Habit.API.V1.HabitOverviewMapper.ToRes(
      (await overview.GetOverview(new HabitOverviewSearch(userId, 10, 0), "skips", now)).Get());

    var a = res.Habits.Single(h => h.Id == tagged.ToString());
    a.DeadlineTime.Should().Be("20:00");
    a.TodayDeadlineUtc.Should().Be(retimedEnd);
    a.TodayDeadlineUtc!.Value.Kind.Should().Be(DateTimeKind.Utc);
    a.Proof.Should().Be("nfc");
    a.RoutineId.Should().Be(routineId.ToString());

    var b = res.Habits.Single(h => h.Id == offDay.ToString());
    b.DeadlineTime.Should().Be("23:59");
    b.TodayDeadlineUtc.Should().BeNull("Tuesday habit, Monday today");
    b.Proof.Should().Be("manual");
    b.RoutineId.Should().BeNull();

    res.Habits.Single(h => h.Id == bound.ToString()).Proof.Should().Be("strava");
  }

  // A habit without a money stake has no charity: Today shows it without one beside a staked
  // habit that keeps its charity, and its missed day settles with no penalty.
  [SkippableFact]
  public async Task FreeHabitWithoutCharity_InOverview_AndMissedWithoutPenalty()
  {
    Skip.If(_conn == null, SkipReason);
    var (userId, charityId) = await SeedUser(LosAngeles);
    var staked = await SeedHabit(userId, charityId, ["Monday"], LosAngeles);
    var free = await SeedHabit(userId, charityId, ["Monday"], LosAngeles);
    await _db.HabitVersions.Where(v => v.HabitId == free).ExecuteUpdateAsync(x => x
      .SetProperty(v => v.CharityId, (Guid?)null)
      .SetProperty(v => v.StakeCents, 0));

    var now = U(2026, 1, 5, 18); // Mon 10:00 in Los Angeles
    var svc = Checks.Service(_db);
    var res = App.Modules.Habit.API.V1.HabitOverviewMapper.ToRes(
      (await Overview(svc).GetOverview(new HabitOverviewSearch(userId, 10, 0), "skips", now)).Get());
    res.Habits.Single(h => h.Id == free.ToString()).Charity.Should().BeNull();
    res.Habits.Single(h => h.Id == staked.ToString()).Charity!.Id.Should().Be(charityId.ToString());

    await svc.OpenUpcoming(now);
    (await svc.CloseDue(U(2026, 1, 6, 9))).Get().Should().Be(2);
    await svc.DispatchEvents(U(2026, 1, 6, 9));
    (await ExecutionsOf(free)).Single().Status.Should().Be(HabitExecutionStatusData.Failed);
    (await _db.Penalties.AsNoTracking().Where(p => p.UserId == userId).Select(p => p.CharityId).ToListAsync())
      .Should().Equal(charityId);
    (await _db.CheckEvents.AsNoTracking().Where(e => e.HabitId == free).Select(e => e.DispatchedAtUtc).SingleAsync())
      .Should().NotBeNull("delivered, not stuck retrying");
  }

  // A configuration needs no charity; an update without one clears the default.
  [SkippableFact]
  public async Task Configuration_WithoutCharity_SavesReadsAndClears()
  {
    Skip.If(_conn == null, SkipReason);
    var (_, charityId) = await SeedUser(Singapore);
    var userId = $"user-{Guid.NewGuid():N}";
    _db.Users.Add(new UserData { Id = userId, Username = userId, Email = $"{userId}@test.local" });
    await _db.SaveChangesAsync();
    var configs = new Domain.Configuration.ConfigurationService(
      new ConfigurationRepository(_db, NullLogger<ConfigurationRepository>.Instance), Checks.Service(_db),
      new NoTransaction());

    var created = (await configs.Create(userId,
      new Domain.Configuration.ConfigurationRecord { Timezone = Singapore }, null)).Get();
    created.Record.DefaultCharityId.Should().BeNull();
    var read = (await configs.GetByUserId(userId)).Get()!;
    read.Principal.Record.DefaultCharityId.Should().BeNull();
    read.Charity.Should().BeNull();

    await configs.Update(created.Id, userId,
      new Domain.Configuration.ConfigurationRecord { Timezone = Singapore, DefaultCharityId = charityId });
    (await configs.GetByUserId(userId)).Get()!.Charity!.Id.Should().Be(charityId);

    await configs.Update(created.Id, userId, new Domain.Configuration.ConfigurationRecord { Timezone = Singapore });
    _db.ChangeTracker.Clear();
    var cleared = (await configs.GetByUserId(userId)).Get()!;
    cleared.Principal.Record.DefaultCharityId.Should().BeNull();
    cleared.Charity.Should().BeNull();
  }

  private HabitOverviewService Overview(CheckService checks) => new(
    new HabitService(new HabitRepository(_db, NullLogger<HabitRepository>.Instance), null!, checks,
      null!, NullLogger<HabitService>.Instance),
    new HabitRepository(_db, NullLogger<HabitRepository>.Instance),
    new Domain.Configuration.ConfigurationService(
      new ConfigurationRepository(_db, NullLogger<ConfigurationRepository>.Instance), checks, new NoTransaction()),
    new Domain.Charity.CharityService(new CharityRepository(_db, NullLogger<CharityRepository>.Instance)),
    new StreakService(new StreakRepository(_db, NullLogger<StreakRepository>.Instance)),
    new StreakRepository(_db, NullLogger<StreakRepository>.Instance),
    new FixedMonth(),
    new FreeTier(),
    new CheckRepository(_db, NullLogger<CheckRepository>.Instance),
    new NfcTagRepository(_db, NullLogger<NfcTagRepository>.Instance),
    new ProofBindingRepository(_db, NullLogger<ProofBindingRepository>.Instance),
    new NoTransaction());

  // Each habit gets its own streaks and week, across its versions: current counts Completed
  // days after the last Failed day up to today, max is the longest Completed run (only a
  // Failed day breaks it), and later days don't count toward today.
  [SkippableFact]
  public async Task Overview_StreaksAndWeek_PerHabit()
  {
    Skip.If(_conn == null, SkipReason);
    var (userId, charityId) = await SeedUser(LosAngeles);
    var a = await SeedHabit(userId, charityId, ["Sunday", "Monday", "Wednesday"], LosAngeles);
    var b = await SeedHabit(userId, charityId, ["Monday"], LosAngeles);
    var a1 = await _db.HabitVersions.Where(v => v.HabitId == a).Select(v => v.Id).SingleAsync();
    var a2 = Guid.NewGuid();
    _db.HabitVersions.Add(new HabitVersionData
    {
      Id = a2, HabitId = a, CharityId = charityId, Version = 2, Task = "Run 5 km", DaysOfWeek = ["Sunday", "Monday", "Wednesday"],
      NotificationTime = new TimeOnly(19, 0), Timezone = LosAngeles,
      StakeCents = 1000, StakeCurrency = "USD", RatioBasisPoints = 10000
    });
    await _db.SaveChangesAsync();
    await _db.Habits.Where(h => h.Id == a).ExecuteUpdateAsync(x => x.SetProperty(h => h.Version, (ushort)2));
    var b1 = await _db.HabitVersions.Where(v => v.HabitId == b).Select(v => v.Id).SingleAsync();

    const HabitExecutionStatusData C = HabitExecutionStatusData.Completed, F = HabitExecutionStatusData.Failed;
    void Exec(Guid versionId, int m, int d, HabitExecutionStatusData status) =>
      _db.HabitExecutions.Add(new HabitExecutionData
      {
        HabitVersionId = versionId, Date = new DateOnly(m == 12 ? 2025 : 2026, m, d), Status = status
      });
    // a: a run of 4, a miss, the best run (5 Completed days across a skip, a vacation day
    // and both versions), a miss, then 2 Completed days up to today and a miss on
    // Wednesday, after today.
    foreach (var d in new[] { 15, 16, 17, 18 }) Exec(a1, 12, d, C);
    Exec(a1, 12, 19, F);
    Exec(a1, 12, 20, C);
    Exec(a1, 12, 21, C);
    Exec(a1, 12, 22, HabitExecutionStatusData.Skipped);
    Exec(a1, 12, 23, C);
    Exec(a1, 12, 24, HabitExecutionStatusData.Vacation);
    Exec(a2, 12, 25, C);
    Exec(a2, 12, 26, C);
    Exec(a2, 12, 27, F);
    Exec(a2, 1, 3, HabitExecutionStatusData.Vacation);
    Exec(a2, 1, 4, C);
    Exec(a2, 1, 5, C);
    Exec(a2, 1, 7, F);
    // b: two Completed days, then a miss today.
    Exec(b1, 1, 2, C);
    Exec(b1, 1, 3, C);
    Exec(b1, 1, 5, F);
    await _db.SaveChangesAsync();

    var now = U(2026, 1, 5, 18); // Mon 10:00 in Los Angeles; the week is Sun 4 to Sat 10
    var summary = (await Overview(Checks.Service(_db))
      .GetOverview(new HabitOverviewSearch(userId, 10, 0), "skips", now)).Get();

    var sa = summary.Items.Single(i => i.HabitId == a).Status;
    sa.CurrentStreak.Should().Be(2);
    sa.MaxStreak.Should().Be(5);
    sa.Today.Should().Be(new DateOnly(2026, 1, 5));
    sa.IsCompleteToday.Should().BeTrue();
    sa.TodayStatus.Should().Be(HabitDayStatus.Succeeded);
    sa.WeekStatuses[DayOfWeek.Sunday].Should().Be(HabitDayStatus.Succeeded);
    sa.WeekStatuses[DayOfWeek.Monday].Should().Be(HabitDayStatus.Succeeded);
    sa.WeekStatuses[DayOfWeek.Wednesday].Should().Be(HabitDayStatus.Failed);
    sa.WeekStatuses[DayOfWeek.Saturday].Should().Be(HabitDayStatus.NotApplicable, "Jan 3 is last week");

    var sb = summary.Items.Single(i => i.HabitId == b).Status;
    sb.CurrentStreak.Should().Be(0);
    sb.MaxStreak.Should().Be(2);
    sb.IsCompleteToday.Should().BeFalse();
    sb.TodayStatus.Should().Be(HabitDayStatus.Failed);
    sb.WeekStatuses[DayOfWeek.Sunday].Should().Be(HabitDayStatus.NotApplicable);
  }

  private sealed class NoTransaction : Domain.ITransactionManager
  {
    public Task<Result<T>> Start<T>(Func<Task<Result<T>>> func) => func();
  }

  private sealed class FixedMonth : IAllowanceService
  {
    public Task<Result<UserMonthWindow>> GetUserMonthWindow(string userId, DateTime? utcNow = null)
      => Task.FromResult<Result<UserMonthWindow>>(new UserMonthWindow(LosAngeles, new DateOnly(2026, 1, 1),
        new DateOnly(2026, 1, 31), new DateTime(2026, 1, 5, 10, 0, 0)));

    public Task<Result<(string Timezone, DateOnly Today)>> GetUserToday(string userId, DateTime? utcNow = null)
      => throw new NotImplementedException();
  }

  private sealed class PlanLookupDown : ISubscriptionService
  {
    public Task<Result<string>> GetUserTier(string userId)
      => Task.FromResult<Result<string>>(new InvalidOperationException("subscription lookup down"));

    public Task<Result<int>> GetLimitForTier(string tier, string key) => throw new NotImplementedException();
  }

  private sealed class FreeTier : ISubscriptionService
  {
    public Task<Result<string>> GetUserTier(string userId) => Task.FromResult<Result<string>>("free");
    public Task<Result<int>> GetLimitForTier(string tier, string key) => Task.FromResult<Result<int>>(3);
  }
}
