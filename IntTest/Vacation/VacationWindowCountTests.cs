using App.Error;
using App.Error.V1;
using App.Modules.Entitlement;
using App.Modules.Protection;
using App.Modules.Users.Data;
using App.Modules.Vacation.Data;
using App.StartUp.Database;
using App.StartUp.Registry;
using CSharp_Result;
using Domain.Subscription;
using Domain.Vacation;
using IntTest.Penalty;
using Microsoft.Extensions.Logging.Abstractions;

namespace IntTest.Vacation;

// DB-backed tests for how vacations use the yearly vacation windows: a vacation uses exactly
// one window, in the year it starts, even when it spans New Year. Listing by year still shows
// a spanning vacation under both years. Real VacationRepository over a real Postgres
// (PENALTY_TEST_DB); unset -> tests Skip.
[Collection(DbIntegrationCollection.Name)]
public class VacationWindowCountTests : IAsyncLifetime
{
  private const string SkipReason = "PENALTY_TEST_DB not set; skipping DB-backed vacation window tests.";
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

  private VacationRepository Repo() => new(_db, NullLogger<VacationRepository>.Instance);

  // Real EntitlementService over the real VacationRepository; only the tier is stubbed.
  private EntitlementService Entitlement(int vacationLimit)
    => new(new VacationLimit(vacationLimit), Repo(), null!, new FreezePolicy(), null!);

  private async Task<string> SeedUser()
  {
    var userId = $"user-{Guid.NewGuid():N}";
    _db.Users.Add(new UserData { Id = userId, Username = userId, Email = $"{userId}@test.local" });
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

  private static DateOnly D(int y, int m, int d) => new(y, m, d);

  [SkippableFact]
  public async Task CountWindowsForYear_VacationSpanningNewYear_CountsOnlyInStartYear()
  {
    Skip.If(_conn == null, SkipReason);
    var user = await SeedUser();
    await SeedVacation(user, D(2026, 12, 28), D(2027, 1, 3));

    (await Repo().CountWindowsForYear(user, 2026)).Get().Should().Be(1);
    (await Repo().CountWindowsForYear(user, 2027)).Get().Should().Be(0, "the vacation starts in 2026");
  }

  [SkippableFact]
  public async Task CountWindowsForYear_VacationInsideOneYear_CountsOnce()
  {
    Skip.If(_conn == null, SkipReason);
    var user = await SeedUser();
    await SeedVacation(user, D(2026, 6, 1), D(2026, 6, 10));

    (await Repo().CountWindowsForYear(user, 2026)).Get().Should().Be(1);
    (await Repo().CountWindowsForYear(user, 2025)).Get().Should().Be(0);
    (await Repo().CountWindowsForYear(user, 2027)).Get().Should().Be(0);
  }

  [SkippableFact]
  public async Task EnsureVacationWindowAllowed_SpanningVacationFromLastYear_DoesNotUseThisYearsWindow()
  {
    Skip.If(_conn == null, SkipReason);
    var user = await SeedUser();
    await SeedVacation(user, D(2026, 12, 28), D(2027, 1, 3));

    var res = await Entitlement(1).EnsureVacationWindowAllowed(user, D(2027, 3, 1));

    res.IsSuccess().Should().BeTrue("2027's only window is still unused");
  }

  [SkippableFact]
  public async Task EnsureVacationWindowAllowed_StartYearFull_IsRefused()
  {
    Skip.If(_conn == null, SkipReason);
    var user = await SeedUser();
    await SeedVacation(user, D(2026, 6, 1), D(2026, 6, 10));

    var svc = Entitlement(1);
    var inside = await svc.EnsureVacationWindowAllowed(user, D(2026, 9, 1));
    // A vacation that starts in 2026 and ends in 2027 still uses a 2026 window.
    var spanning = await svc.EnsureVacationWindowAllowed(user, D(2026, 12, 30));

    foreach (var res in new[] { inside, spanning })
    {
      res.IsFailure().Should().BeTrue();
      var problem = res.FailureOrDefault().Should().BeOfType<DomainProblemException>().Subject.Problem;
      var tier = problem.Should().BeOfType<TierInsufficient>().Subject;
      tier.LimitKey.Should().Be(EntitlementKeys.VacationWindowsYearly);
      tier.LimitValue.Should().Be(1);
    }
  }

  [SkippableFact]
  public async Task Search_ByYear_StillReturnsSpanningVacationInBothYears()
  {
    Skip.If(_conn == null, SkipReason);
    var user = await SeedUser();
    await SeedVacation(user, D(2026, 12, 28), D(2027, 1, 3));

    foreach (var year in new[] { 2026, 2027 })
    {
      var found = (await Repo().Search(new VacationSearch { UserId = user, Year = year, Limit = 10, Skip = 0 })).Get();
      found.Should().ContainSingle(year.ToString())
        .Which.Record.StartDate.Should().Be(D(2026, 12, 28));
    }

    var none = (await Repo().Search(new VacationSearch { UserId = user, Year = 2028, Limit = 10, Skip = 0 })).Get();
    none.Should().BeEmpty();
  }

  private sealed class VacationLimit(int limit) : ISubscriptionService
  {
    public Task<Result<string>> GetUserTier(string userId) => Task.FromResult<Result<string>>("plus");

    public Task<Result<int>> GetLimitForTier(string tier, string key)
      => Task.FromResult<Result<int>>(key == EntitlementKeys.VacationWindowsYearly
        ? limit
        : throw new InvalidOperationException($"unexpected limit {key}"));
  }
}
