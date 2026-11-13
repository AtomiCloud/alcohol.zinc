using System.Data.Common;
using System.Security.Claims;
using App.Modules.Check.Data;
using App.Modules.Configurations.Data;
using App.Modules.Entitlement;
using App.Modules.Habit.API.V1;
using App.Modules.Habit.Data;
using App.Modules.HabitExecution.Data;
using App.Modules.HabitVersion.Data;
using App.Modules.Protection;
using App.Modules.Users.Data;
using App.Modules.Vacation;
using App.Modules.Vacation.API.V1;
using App.Modules.Vacation.Data;
using App.StartUp.Database;
using App.StartUp.Options;
using App.StartUp.Registry;
using CSharp_Result;
using Domain.Allowance;
using Domain.Check;
using Domain.Habit;
using Domain.Subscription;
using IntTest.Check;
using IntTest.Penalty;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace IntTest.Concurrency;

// Concurrent requests can't take a user past a counted limit (habits, skips a month, vacation
// windows a year). Each request is the real controller over its own MainDbContext and connection,
// like a request scope, against Postgres (PENALTY_TEST_DB); only the plan's limits and the user's
// month are stubbed. The user is one below the limit and N requests start together. A barrier
// (CountGate) holds each request right after its count until all N have counted, so without the
// user's lock they all count the same total and all write; with it, the others wait on the lock
// before counting, and exactly one request succeeds.
[Collection(DbIntegrationCollection.Name)]
public class QuotaRaceTests : IAsyncLifetime
{
  private const string SkipReason = "PENALTY_TEST_DB not set; skipping DB-backed quota race tests.";
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

  // The free plan's habits and skips, and the pro plan's vacation windows.
  private static readonly Limits Plan = new(Habits: 2, Skips: 3, VacationWindows: 6);

  [SkippableFact]
  public async Task CreateHabit_EightAtOnce_OneBelowTheLimit_CreatesOne()
  {
    Skip.If(_conn == null, SkipReason);
    var userId = await SeedUser("Asia/Singapore");
    await SeedHabitVersion(userId, "Asia/Singapore");

    var gate = new CountGate(8, sql => sql.Contains(@"FROM ""Habits"""));
    var results = await Concurrently(8, gate, async (db, _) =>
    {
      var controller = HabitController(db, userId, Today("Asia/Singapore"));
      return (controller, (await controller.Create(userId,
        new CreateHabitReq("Run 5 km", ["Monday"], "19:00:00", "0", null, "Asia/Singapore"))).Result);
    });

    ExactlyOneSucceeded(results);
    (await _db.Habits.CountAsync(h => h.UserId == userId)).Should().Be(Plan.Habits);
  }

  [SkippableFact]
  public async Task SkipHabit_SixAtOnce_OneBelowTheLimit_SkipsOne()
  {
    Skip.If(_conn == null, SkipReason);
    var (userId, tz, versions) = await UserWithSkipsUsed(Plan.Skips - 1, toSkip: 6);

    var gate = new CountGate(versions.Count, IsSkipCount);
    var results = await Concurrently(versions.Count, gate, async (db, i) =>
    {
      var controller = HabitController(db, userId, Today(tz));
      return (controller, (await controller.SkipHabit(userId, versions[i], new SkipHabitReq(null))).Result);
    });

    ExactlyOneSucceeded(results);
    (await SkipsOf(userId)).Should().Be(Plan.Skips);
  }

  [SkippableFact]
  public async Task CreateVacation_SixAtOnce_OneBelowTheYearlyLimit_CreatesOne()
  {
    Skip.If(_conn == null, SkipReason);
    var userId = await SeedUser("Asia/Singapore");
    foreach (var month in Enumerable.Range(1, Plan.VacationWindows - 1))
      _db.VacationPeriods.Add(new VacationPeriodData
      {
        UserId = userId, StartDate = new DateOnly(2028, month, 1), EndDate = new DateOnly(2028, month, 3),
        Timezone = "Asia/Singapore"
      });
    await _db.SaveChangesAsync();

    // Six more 2028 windows that overlap neither each other nor the used ones.
    var gate = new CountGate(6, sql => sql.Contains(@"FROM ""VacationPeriods"""));
    var results = await Concurrently(6, gate, async (db, i) =>
    {
      var controller = VacationController(db, userId);
      var month = Plan.VacationWindows + i;
      return (controller, (await controller.Create(userId,
        new CreateVacationReq($"01-{month:00}-2028", $"03-{month:00}-2028", "Asia/Singapore"))).Result);
    });

    ExactlyOneSucceeded(results);
    (await _db.VacationPeriods.CountAsync(v => v.UserId == userId && v.StartDate.Year == 2028))
      .Should().Be(Plan.VacationWindows);
  }

  // Settlement settles under the same user lock as a skip, taken before the check's row lock: it
  // waits for the skips instead of deadlocking or failing, and the skip limit still holds.
  [SkippableFact]
  public async Task SkipHabit_FourAtOnce_RacingSettlementOfTheSameUser_SkipsOneAndSettles()
  {
    Skip.If(_conn == null, SkipReason);
    var (userId, tz, versions) = await UserWithSkipsUsed(Plan.Skips - 1, toSkip: 4);
    var dueHabit = await SeedHabitWithDueCheck(userId, tz);

    var gate = new CountGate(versions.Count, IsSkipCount);
    var skips = Concurrently(versions.Count, gate, async (db, i) =>
    {
      var controller = HabitController(db, userId, Today(tz));
      return (controller, (await controller.SkipHabit(userId, versions[i], new SkipHabitReq(null))).Result);
    });
    await using var cron = PenaltyIntegrationTests.NewContext(_conn!);
    var settled = Task.Run(() => Checks.Service(cron).CloseDue(DateTime.UtcNow));

    ExactlyOneSucceeded(await skips);
    (await settled).Get().Should().Be(1);
    (await SkipsOf(userId)).Should().Be(Plan.Skips);
    (await _db.HabitChecks.AsNoTracking().SingleAsync(c => c.HabitId == dueHabit)).Status
      .Should().Be(HabitCheckStatusData.Missed);
  }

  // A request that can't get the user's lock within the lock timeout is refused as busy (409),
  // not left hanging or failed with a 500, and writes nothing.
  [SkippableFact]
  public async Task CreateHabit_WhileAnotherTransactionHoldsTheUserPastTheTimeout_Is409Busy()
  {
    Skip.If(_conn == null, SkipReason);
    var userId = await SeedUser("Asia/Singapore");
    await using var holder = PenaltyIntegrationTests.NewContext(_conn!);
    await using var held = await holder.Database.BeginTransactionAsync();
    await holder.Database.LockUser(userId);

    await using var db = PenaltyIntegrationTests.NewContext(_conn!);
    var controller = HabitController(db, userId, Today("Asia/Singapore"));
    var res = await controller.Create(userId,
      new CreateHabitReq("Run 5 km", ["Monday"], "19:00:00", "0", null, "Asia/Singapore"));

    (res.Result as IStatusCodeActionResult)?.StatusCode.Should().Be(409);
    controller.HttpContext.Items["DomainProblemContext"].Should().BeOfType<App.Error.V1.UserBusy>();
    (await _db.Habits.CountAsync(h => h.UserId == userId)).Should().Be(0);
  }

  // ---- harness ----

  private static bool IsSkipCount(string sql) => sql.Contains(@"FROM ""HabitExecutions""");

  // One OK; every other request was refused for the plan (400 TierInsufficient), not busy or failed.
  private static void ExactlyOneSucceeded(List<(ControllerBase Controller, IActionResult? Result)> results)
  {
    results.Count(r => r.Result is OkObjectResult).Should().Be(1);
    results.Where(r => r.Result is not OkObjectResult).Should().AllSatisfy(r =>
    {
      (r.Result as IStatusCodeActionResult)?.StatusCode.Should().Be(400);
      r.Controller.HttpContext.Items["DomainProblemContext"]!.GetType().Name.Should().Be("TierInsufficient");
    });
  }

  // Runs n requests at once, each on its own context (with the barrier) and an open pooled connection.
  private async Task<List<(ControllerBase, IActionResult?)>> Concurrently(int n, CountGate gate,
    Func<MainDbContext, int, Task<(ControllerBase, IActionResult?)>> request)
  {
    var contexts = Enumerable.Range(0, n).Select(_ => PenaltyIntegrationTests.NewContext(_conn!, gate)).ToList();
    try
    {
      foreach (var db in contexts) await db.Users.AnyAsync(); // warm the pool
      var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var requests = contexts.Select((db, i) => Task.Run(async () =>
      {
        await start.Task;
        return await request(db, i);
      })).ToList();
      start.SetResult();
      return [.. await Task.WhenAll(requests)];
    }
    finally
    {
      foreach (var db in contexts) await db.DisposeAsync();
    }
  }

  // Holds each request right after a matching count until all n requests have counted, or until
  // the first one held has waited a while (the others are waiting on the user's lock), after
  // which it holds no one.
  private sealed class CountGate(int n, Func<string, bool> countsFrom) : DbCommandInterceptor
  {
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(1.5);
    private readonly TaskCompletionSource open = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int counted;

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
      CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
      var sql = command.CommandText;
      if (sql.Contains("count(", StringComparison.OrdinalIgnoreCase) && countsFrom(sql))
      {
        if (Interlocked.Increment(ref this.counted) >= n) this.open.TrySetResult();
        await Task.WhenAny(this.open.Task, Task.Delay(Patience, cancellationToken));
        this.open.TrySetResult();
      }

      return result;
    }
  }

  private static HabitController HabitController(MainDbContext db, string userId, DateOnly today)
  {
    var repo = new HabitRepository(db, NullLogger<HabitRepository>.Instance);
    var service = new HabitService(repo, Entitlement(repo), Checks.Service(db), null!,
      NullLogger<HabitService>.Instance);
    var options = new StaticOptionsMonitor<HabitOption>(new HabitOption());
    return new HabitController(service, null!, null!, new MonthOf(today), null!,
      new CreateHabitReqValidator(options), new UpdateHabitReqValidator(options), new(), new(), new(), new())
    {
      ControllerContext = SignedIn(userId)
    };
  }

  private static VacationController VacationController(MainDbContext db, string userId)
  {
    var repo = new VacationRepository(db, NullLogger<VacationRepository>.Instance);
    var entitlement = Entitlement(new HabitRepository(db, NullLogger<HabitRepository>.Instance));
    return new VacationController(new VacationService(repo, null!, entitlement), repo, null!, new(), new())
    {
      ControllerContext = SignedIn(userId)
    };
  }

  private static EntitlementService Entitlement(HabitRepository habits) => new(Plan, habits, new FreezePolicy(), null!);

  private static ControllerContext SignedIn(string userId) => new()
  {
    HttpContext = new DefaultHttpContext
    {
      User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, userId)], "test"))
    }
  };

  private Task<int> SkipsOf(string userId) => _db.HabitExecutions.CountAsync(e =>
    e.HabitVersion!.Habit!.UserId == userId && e.Status == HabitExecutionStatusData.Skipped);

  private static DateOnly Today(string tz) =>
    DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(tz)));

  private async Task<string> SeedUser(string timezone)
  {
    var userId = $"user-{Guid.NewGuid():N}";
    _db.Users.Add(new UserData { Id = userId, Username = userId, Email = $"{userId}@test.local" });
    _db.Configurations.Add(new ConfigurationData { UserId = userId, Timezone = timezone });
    await _db.SaveChangesAsync();
    return userId;
  }

  // A user with `used` skips this month (on habits of their own) and `toSkip` habits whose check
  // is open now. The timezone keeps the test clear of the user's midnight.
  private async Task<(string UserId, string Tz, List<Guid> Versions)> UserWithSkipsUsed(int used, int toSkip)
  {
    var tz = DateTime.UtcNow.Hour is >= 1 and <= 22 ? "UTC" : "Asia/Singapore";
    var userId = await SeedUser(tz);
    foreach (var _ in Enumerable.Range(0, used))
      _db.HabitExecutions.Add(new HabitExecutionData
      {
        HabitVersionId = await SeedHabitVersion(userId, tz), Date = Today(tz),
        Status = HabitExecutionStatusData.Skipped, PaymentProcessed = false
      });
    await _db.SaveChangesAsync();
    var versions = new List<Guid>();
    foreach (var _ in Enumerable.Range(0, toSkip)) versions.Add(await SeedHabitVersion(userId, tz));
    return (userId, tz, versions);
  }

  // A habit whose check from two days ago is still Pending, so settlement closes it now.
  private async Task<Guid> SeedHabitWithDueCheck(string userId, string tz)
  {
    var versionId = await SeedHabitVersion(userId, tz);
    var habitId = (await _db.HabitVersions.AsNoTracking().SingleAsync(v => v.Id == versionId)).HabitId;
    var date = Today(tz).AddDays(-2);
    var (opens, deadline) = CheckWindow.For(date, TimeZoneInfo.FindSystemTimeZoneById(tz), new TimeOnly(23, 59));
    await new CheckRepository(_db, NullLogger<CheckRepository>.Instance)
      .CreatePending([new NewCheck(habitId, date, tz, opens, deadline)]);
    return habitId;
  }

  // An enabled habit due every day, without a stake; returns its version id.
  private async Task<Guid> SeedHabitVersion(string userId, string timezone)
  {
    var habitId = Guid.NewGuid();
    _db.Habits.Add(new HabitData { Id = habitId, UserId = userId, Version = 1, Enabled = true });
    var version = new HabitVersionData
    {
      HabitId = habitId, CharityId = null, Version = 1, Task = "Run 5 km",
      DaysOfWeek = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"],
      NotificationTime = new TimeOnly(19, 0), DeadlineTime = new TimeOnly(23, 59), Timezone = timezone,
      StakeCents = 0, StakeCurrency = "USD", RatioBasisPoints = 10000
    };
    _db.HabitVersions.Add(version);
    await _db.SaveChangesAsync();
    return version.Id;
  }

  private sealed record Limits(int Habits, int Skips, int VacationWindows) : ISubscriptionService
  {
    public Task<Result<string>> GetUserTier(string userId) => Task.FromResult<Result<string>>("free");

    public Task<Result<int>> GetLimitForTier(string tier, string key) => Task.FromResult<Result<int>>(key switch
    {
      EntitlementKeys.HabitsMax => this.Habits,
      EntitlementKeys.SkipsMonthly => this.Skips,
      EntitlementKeys.VacationWindowsYearly => this.VacationWindows,
      _ => throw new InvalidOperationException($"unexpected limit {key}")
    });
  }

  // The user's month is the one containing `today`.
  private sealed class MonthOf(DateOnly today) : IAllowanceService
  {
    public Task<Result<UserMonthWindow>> GetUserMonthWindow(string userId, DateTime? utcNow = null)
    {
      var start = new DateOnly(today.Year, today.Month, 1);
      return Task.FromResult<Result<UserMonthWindow>>(
        new UserMonthWindow("UTC", start, start.AddMonths(1).AddDays(-1), DateTime.UtcNow));
    }

    public Task<Result<(string Timezone, DateOnly Today)>> GetUserToday(string userId, DateTime? utcNow = null)
      => throw new NotImplementedException();
  }
}
