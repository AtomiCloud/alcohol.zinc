using App.Error;
using App.Error.V1;
using App.Modules.Configurations.Data;
using App.Modules.Entitlement;
using App.Modules.Habit.Data;
using App.Modules.HabitExecution.Data;
using App.Modules.HabitVersion.Data;
using App.Modules.Protection;
using App.Modules.Subscription;
using App.Modules.Subscription.Data;
using App.Modules.Users.Data;
using App.Modules.Vacation;
using App.Modules.Vacation.Data;
using App.StartUp.Database;
using App.StartUp.Options;
using App.StartUp.Registry;
using CSharp_Result;
using Domain.Allowance;
using Domain.Habit;
using Domain.Subscription;
using Domain.Vacation;
using IntTest.Check;
using IntTest.Penalty;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace IntTest.Subscription;

// Plan limits as shipped (App/Config/settings.yaml): skips a month free 3 / pro 5 / ultimate 10,
// vacations a year 0 / 2 / 5, longest vacation pro 14 / ultimate 30 days. Real services and
// repositories over Postgres (PENALTY_TEST_DB), with the user's plan from a UserSubscriptions row.
[Collection(DbIntegrationCollection.Name)]
public class PlanLimitTests : IAsyncLifetime
{
  private const string SkipReason = "PENALTY_TEST_DB not set; skipping DB-backed plan limit tests.";
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

  // ---- vacations ----

  [SkippableTheory]
  [InlineData("free", 0)]
  [InlineData("pro", 2)]
  [InlineData("ultimate", 5)]
  public async Task Vacations_CanBookThePlansNumberAYear_AndAreRefusedTheNext(string tier, int windows)
  {
    Skip.If(_conn == null, SkipReason);
    var user = await SeedUser(tier);

    for (var i = 0; i < windows; i++)
      (await Book(user, D(2028, i + 1, 1), D(2028, i + 1, 3))).IsSuccess().Should().BeTrue($"vacation {i + 1}");

    ShouldBeRefused(await Book(user, D(2028, 10, 1), D(2028, 10, 3)),
      EntitlementKeys.VacationWindowsYearly, windows);
    (await CountVacations(user)).Should().Be(windows);
  }

  [SkippableFact]
  public async Task Vacations_ProUserWithFourBookedUnderTheOldLimit_KeepsThemAndIsRefusedANewOne()
  {
    Skip.If(_conn == null, SkipReason);
    var user = await SeedUser("pro");
    foreach (var month in new[] { 1, 3, 5, 7 }) await SeedVacation(user, D(2028, month, 1), D(2028, month, 3));

    ShouldBeRefused(await Book(user, D(2028, 9, 1), D(2028, 9, 3)), EntitlementKeys.VacationWindowsYearly, 2);
    (await CountVacations(user)).Should().Be(4, "vacations booked under the old limit stay");
  }

  [SkippableTheory]
  [InlineData("pro", 14)]
  [InlineData("ultimate", 30)]
  public async Task Vacations_LongerThanThePlansMaximum_AreRefused(string tier, int maxDays)
  {
    Skip.If(_conn == null, SkipReason);
    var user = await SeedUser(tier);
    var start = D(2028, 3, 1);

    // Both the first and the last day count.
    ShouldBeRefused(await Book(user, start, start.AddDays(maxDays)), EntitlementKeys.VacationMaxDays, maxDays);
    (await Book(user, start, start.AddDays(maxDays - 1))).IsSuccess().Should().BeTrue("exactly the maximum");
  }

  [SkippableFact]
  public async Task Vacations_ExistingOneLongerThanTheMaximum_StaysAsBooked()
  {
    Skip.If(_conn == null, SkipReason);
    var user = await SeedUser("pro");
    await SeedVacation(user, D(2028, 1, 1), D(2028, 2, 15));

    (await Book(user, D(2028, 6, 1), D(2028, 6, 3))).IsSuccess().Should().BeTrue("one of two windows is left");
    (await _db.VacationPeriods.AsNoTracking().CountAsync(v => v.UserId == user && v.EndDate == D(2028, 2, 15)))
      .Should().Be(1);
  }

  [SkippableFact]
  public async Task CancelUnstarted_DeletesOnlyVacationsAfterToday_AndIsSafeToRepeat()
  {
    Skip.If(_conn == null, SkipReason);
    var user = await SeedUser("free");
    var other = await SeedUser("free");
    var today = D(2028, 5, 10);
    await SeedVacation(user, D(2028, 5, 1), D(2028, 5, 5));   // over
    await SeedVacation(user, D(2028, 5, 8), D(2028, 5, 12));  // running
    await SeedVacation(user, D(2028, 5, 10), D(2028, 5, 11)); // starts today: started
    await SeedVacation(user, D(2028, 5, 11), D(2028, 5, 12)); // not started
    await SeedVacation(user, D(2028, 8, 1), D(2028, 8, 5));   // not started
    await SeedVacation(other, D(2028, 8, 1), D(2028, 8, 5));  // someone else's

    var service = Vacations(new TodayIs(today));
    (await service.CancelUnstarted(user)).Get().Should().Be(2);
    (await service.CancelUnstarted(user)).Get().Should().Be(0, "a repeat finds nothing left to cancel");

    (await _db.VacationPeriods.AsNoTracking().Where(v => v.UserId == user).Select(v => v.StartDate).ToListAsync())
      .Should().BeEquivalentTo([D(2028, 5, 1), D(2028, 5, 8), D(2028, 5, 10)]);
    (await CountVacations(other)).Should().Be(1);
  }

  // ---- skips ----

  [SkippableTheory]
  [InlineData("pro", 5)]
  [InlineData("ultimate", 10)]
  public async Task Skips_OneBelowThePlansLimit_SkipsOnce_ThenIsRefused(string tier, int limit)
  {
    Skip.If(_conn == null, SkipReason);
    var (user, tz) = await UserWithSkips(tier, used: limit - 1);

    (await SkipHabit(user, tz, await SeedHabitVersion(user, tz))).IsSuccess().Should().BeTrue("one skip is left");
    ShouldBeRefused(await SkipHabit(user, tz, await SeedHabitVersion(user, tz)), EntitlementKeys.SkipsMonthly, limit);
    (await SkipsOf(user)).Should().Be(limit);
  }

  [SkippableFact]
  public async Task Skips_ProUserAlreadyAtSeven_IsRefusedAndKeepsThem()
  {
    Skip.If(_conn == null, SkipReason);
    var (user, tz) = await UserWithSkips("pro", used: 7);

    ShouldBeRefused(await SkipHabit(user, tz, await SeedHabitVersion(user, tz)), EntitlementKeys.SkipsMonthly, 5);
    (await SkipsOf(user)).Should().Be(7);
  }

  // ---- harness ----

  private static readonly SubscriptionOption Shipped = LoadShipped();

  private static SubscriptionOption LoadShipped()
  {
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null && !File.Exists(Path.Combine(dir.FullName, "App", "Config", "settings.yaml")))
      dir = dir.Parent;
    var opt = new SubscriptionOption();
    new ConfigurationBuilder()
      .AddYamlFile(Path.Combine(dir!.FullName, "App", "Config", "settings.yaml"), optional: false)
      .Build()
      .GetSection(SubscriptionOption.Key)
      .Bind(opt);
    return opt;
  }

  private SubscriptionService Plans() => new(
    new SubscriptionRepository(_db, NullLogger<SubscriptionRepository>.Instance),
    new SubscriptionPlanProvider(new StaticOptionsMonitor<SubscriptionOption>(Shipped)));

  private EntitlementService Entitlement() => new(Plans(),
    new HabitRepository(_db, NullLogger<HabitRepository>.Instance), new FreezePolicy(), null!);

  private VacationService Vacations(IAllowanceService? allowance = null) =>
    new(new VacationRepository(_db, NullLogger<VacationRepository>.Instance), allowance!, Entitlement());

  private Task<Result<VacationPrincipal>> Book(string user, DateOnly start, DateOnly end) =>
    Vacations().Create(user, new VacationRecord { StartDate = start, EndDate = end, Timezone = "Asia/Singapore" });

  private Task<Result<HabitExecutionPrincipal>> SkipHabit(string user, string tz, Guid versionId)
  {
    var today = Today(tz);
    var start = new DateOnly(today.Year, today.Month, 1);
    var month = new UserMonthWindow(tz, start, start.AddMonths(1).AddDays(-1), DateTime.UtcNow);
    var service = new HabitService(new HabitRepository(_db, NullLogger<HabitRepository>.Instance), Entitlement(),
      Checks.Service(_db), null!, null!, NullLogger<HabitService>.Instance);
    return service.SkipHabit(user, versionId, null, month);
  }

  private static void ShouldBeRefused<T>(Result<T> res, string key, int limit)
  {
    res.IsFailure().Should().BeTrue();
    var problem = res.FailureOrDefault().Should().BeOfType<DomainProblemException>().Subject.Problem;
    var tier = problem.Should().BeOfType<TierInsufficient>().Subject;
    tier.LimitKey.Should().Be(key);
    tier.LimitValue.Should().Be(limit);
  }

  private static DateOnly D(int y, int m, int d) => new(y, m, d);

  private static DateOnly Today(string tz) =>
    DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(tz)));

  private Task<int> CountVacations(string user) => _db.VacationPeriods.AsNoTracking().CountAsync(v => v.UserId == user);

  private Task<int> SkipsOf(string user) => _db.HabitExecutions.AsNoTracking().CountAsync(e =>
    e.HabitVersion!.Habit!.UserId == user && e.Status == HabitExecutionStatusData.Skipped);

  private async Task<string> SeedUser(string tier, string timezone = "Asia/Singapore")
  {
    var userId = $"user-{Guid.NewGuid():N}";
    _db.Users.Add(new UserData { Id = userId, Username = userId, Email = $"{userId}@test.local" });
    _db.Configurations.Add(new ConfigurationData { UserId = userId, Timezone = timezone });
    if (tier != SubscriptionService.FreeTier)
      _db.UserSubscriptions.Add(new UserSubscriptionData
      {
        UserId = userId, Tier = tier, Status = (int)SubscriptionStatus.Active,
        PeriodStart = DateTime.UtcNow.AddDays(-1), PeriodEnd = DateTime.UtcNow.AddMonths(1),
        CancelAtPeriodEnd = false
      });
    await _db.SaveChangesAsync();
    return userId;
  }

  private async Task SeedVacation(string userId, DateOnly start, DateOnly end)
  {
    _db.VacationPeriods.Add(new VacationPeriodData
    {
      UserId = userId, StartDate = start, EndDate = end, Timezone = "Asia/Singapore"
    });
    await _db.SaveChangesAsync();
  }

  // A user with `used` skips today, each on a habit of its own. The timezone keeps the test clear
  // of the user's midnight.
  private async Task<(string User, string Tz)> UserWithSkips(string tier, int used)
  {
    var tz = DateTime.UtcNow.Hour is >= 1 and <= 22 ? "UTC" : "Asia/Singapore";
    var user = await SeedUser(tier, tz);
    for (var i = 0; i < used; i++)
      _db.HabitExecutions.Add(new HabitExecutionData
      {
        HabitVersionId = await SeedHabitVersion(user, tz), Date = Today(tz),
        Status = HabitExecutionStatusData.Skipped, PaymentProcessed = false
      });
    await _db.SaveChangesAsync();
    return (user, tz);
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
      NotificationTime = new TimeOnly(19, 0), Timezone = timezone,
      StakeCents = 0, StakeCurrency = "USD", RatioBasisPoints = 10000
    };
    _db.HabitVersions.Add(version);
    await _db.SaveChangesAsync();
    return version.Id;
  }

  private sealed class TodayIs(DateOnly today) : IAllowanceService
  {
    public Task<Result<UserMonthWindow>> GetUserMonthWindow(string userId, DateTime? utcNow = null)
      => throw new NotImplementedException();

    public Task<Result<(string Timezone, DateOnly Today)>> GetUserToday(string userId, DateTime? utcNow = null)
      => Task.FromResult<Result<(string, DateOnly)>>(("Asia/Singapore", today));
  }
}
