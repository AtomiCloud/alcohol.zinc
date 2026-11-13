using System.Security.Claims;
using App.Modules.Charities.Data;
using App.Modules.Configurations.API.V1;
using App.Modules.Configurations.Data;
using App.Modules.Entitlement;
using App.Modules.Habit.API.V1;
using App.Modules.Habit.Data;
using App.Modules.Protection;
using App.Modules.Users.Data;
using App.Modules.Vacation.Data;
using App.StartUp.Database;
using App.StartUp.Options;
using CSharp_Result;
using Domain.Configuration;
using Domain.Habit;
using Domain.Subscription;
using IntTest.Check;
using IntTest.Penalty;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace IntTest.Validation;

// Bad requests get a 400 ValidationError, not a 500 or a silently stored value. Each test calls
// the real controller action with its real validators and repositories over Postgres
// (PENALTY_TEST_DB); only the plan limits are stubbed.
[Collection(DbIntegrationCollection.Name)]
public class RequestValidationTests : IAsyncLifetime
{
  private const string SkipReason = "PENALTY_TEST_DB not set; skipping DB-backed request validation tests.";
  private const string Tz = "Asia/Singapore";
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

  private async Task<(string UserId, Guid CharityId, Guid ConfigId)> SeedUser()
  {
    var userId = $"user-{Guid.NewGuid():N}";
    var charityId = Guid.NewGuid();
    var configId = Guid.NewGuid();
    _db.Users.Add(new UserData { Id = userId, Username = userId, Email = $"{userId}@test.local" });
    _db.Charities.Add(new CharityData { Id = charityId, Name = $"Charity {charityId:N}" });
    _db.Configurations.Add(new ConfigurationData { Id = configId, UserId = userId, Timezone = Tz });
    await _db.SaveChangesAsync();
    return (userId, charityId, configId);
  }

  private static ControllerContext SignedIn(string userId) => new()
  {
    HttpContext = new DefaultHttpContext
    {
      User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, userId)], "test"))
    }
  };

  private HabitController Habits(string userId)
  {
    var repo = new HabitRepository(_db, NullLogger<HabitRepository>.Instance);
    var entitlement = new EntitlementService(new AnyPlan(), new VacationRepository(_db,
      NullLogger<VacationRepository>.Instance), repo, new FreezePolicy(), null!);
    var service = new HabitService(repo, entitlement, Checks.Service(_db), null!, NullLogger<HabitService>.Instance);
    var options = new StaticOptionsMonitor<HabitOption>(new HabitOption());
    return new HabitController(service, null!, null!, null!, entitlement, null!,
      new CreateHabitReqValidator(options), new UpdateHabitReqValidator(options), new(), new(), new(), new())
    {
      ControllerContext = SignedIn(userId)
    };
  }

  private ConfigurationController Configurations(string userId)
  {
    var service = new ConfigurationService(
      new ConfigurationRepository(_db, NullLogger<ConfigurationRepository>.Instance), Checks.Service(_db),
      new NoTransaction());
    return new ConfigurationController(service, new(), new(), null!, null!) { ControllerContext = SignedIn(userId) };
  }

  private static int? Status<T>(ActionResult<T> r) => (r.Result as IStatusCodeActionResult)?.StatusCode;

  // The field names of the ValidationError the controller answered with.
  // ValidationError is internal to App, so its Errors are read by name.
  private static IEnumerable<string> ErrorFields(ControllerBase controller)
  {
    var problem = controller.HttpContext.Items["DomainProblemContext"];
    problem!.GetType().Name.Should().Be("ValidationError");
    return ((IDictionary<string, string[]>)problem.GetType().GetProperty("Errors")!.GetValue(problem)!).Keys;
  }

  private static CreateHabitReq Habit(string[] days, string stake = "0", Guid? charityId = null) =>
    new("Run 5 km", days, "19:00:00", stake, charityId, Tz);

  private Task<List<string[]>> StoredDays(string userId) => _db.HabitVersions.AsNoTracking()
    .Where(v => v.Habit!.UserId == userId).Select(v => v.DaysOfWeek).ToListAsync();

  // ---- GET Habit/{userId}/executions ----

  [SkippableTheory]
  [InlineData("not-a-date", null, null)]
  [InlineData(null, -1, null)]
  [InlineData(null, null, -1)]
  [InlineData(null, int.MaxValue, null)]
  [InlineData(null, 101, null)]
  public async Task Executions_BadQuery_Is400(string? date, int? limit, int? skip)
  {
    Skip.If(_conn == null, SkipReason);
    var (userId, _, _) = await SeedUser();

    var res = await Habits(userId).GetDailyExecutions(userId, new SearchHabitExecutionQuery(null, date, limit, skip));

    Status(res).Should().Be(400);
  }

  [SkippableFact]
  public async Task Executions_ValidQuery_Is200()
  {
    Skip.If(_conn == null, SkipReason);
    var (userId, _, _) = await SeedUser();

    var res = await Habits(userId).GetDailyExecutions(userId, new SearchHabitExecutionQuery(null, "05-01-2026", 100, 0));

    res.Result.Should().BeOfType<OkObjectResult>();
  }

  // ---- POST / PUT Habit: weekdays ----

  [SkippableTheory]
  [InlineData("999")]
  [InlineData("1")]
  [InlineData("Monday,Tuesday")]
  [InlineData("Funday")]
  public async Task CreateHabit_NotADayName_Is400AndNotStored(string day)
  {
    Skip.If(_conn == null, SkipReason);
    var (userId, _, _) = await SeedUser();

    var res = await Habits(userId).Create(userId, Habit([day]));

    Status(res).Should().Be(400);
    (await StoredDays(userId)).Should().BeEmpty();
  }

  // Create and update both take day names in any case, and store them as "Friday".
  [SkippableFact]
  public async Task LowercaseDay_CreatesAndUpdatesUnchanged_StoredCanonical()
  {
    Skip.If(_conn == null, SkipReason);
    var (userId, _, _) = await SeedUser();
    var controller = Habits(userId);

    var created = await controller.Create(userId, Habit(["friday", "MONDAY"]));
    var habitId = created.Result.Should().BeOfType<OkObjectResult>().Subject.Value
      .Should().BeOfType<HabitVersionRes>().Subject.HabitId;
    var updated = await controller.Update(userId, habitId,
      new UpdateHabitReq("Run 5 km", ["friday", "MONDAY"], "19:00:00", "0", null, true, Tz));

    updated.Result.Should().BeOfType<OkObjectResult>();
    (await StoredDays(userId)).Should().AllSatisfy(d => d.Should().Equal("Friday", "Monday"));
  }

  // ---- POST Habit: stake ----

  [SkippableTheory]
  [InlineData("21474836.48")]
  [InlineData("79228162514264337593543950335")] // decimal.MaxValue
  [InlineData("10000.01")]
  [InlineData("0.001")]
  [InlineData("5.005")]
  public async Task CreateHabit_StakeTooLargeOrFinerThanACent_Is400(string stake)
  {
    Skip.If(_conn == null, SkipReason);
    var (userId, charityId, _) = await SeedUser();

    var controller = Habits(userId);

    var res = await controller.Create(userId, Habit(["Monday"], stake, charityId));

    Status(res).Should().Be(400);
    ErrorFields(controller).Should().Contain(nameof(CreateHabitReq.Stake));
    (await StoredDays(userId)).Should().BeEmpty();
  }

  [SkippableTheory]
  [InlineData("10000.00", 1_000_000)]
  [InlineData("0.10", 10)]
  [InlineData("7.500", 750)] // more digits, no lost cent
  public async Task CreateHabit_StakeUpToTheMax_IsStored(string stake, int cents)
  {
    Skip.If(_conn == null, SkipReason);
    var (userId, charityId, _) = await SeedUser();

    var res = await Habits(userId).Create(userId, Habit(["Monday"], stake, charityId));

    res.Result.Should().BeOfType<OkObjectResult>();
    (await _db.HabitVersions.AsNoTracking().SingleAsync(v => v.Habit!.UserId == userId)).StakeCents.Should().Be(cents);
  }

  // ---- POST / PUT Habit: charity ----

  [SkippableFact]
  public async Task UnknownCharity_OnCreateAndUpdate_Is400OnCharityId()
  {
    Skip.If(_conn == null, SkipReason);
    var (userId, _, _) = await SeedUser();
    var controller = Habits(userId);
    var unknown = Guid.NewGuid();

    var create = await controller.Create(userId, Habit(["Monday"], "1.00", unknown));
    Status(create).Should().Be(400);
    ErrorFields(controller).Should().Contain(nameof(CreateHabitReq.CharityId));
    (await StoredDays(userId)).Should().BeEmpty();

    var habitId = (await controller.Create(userId, Habit(["Monday"]))).Result.Should().BeOfType<OkObjectResult>()
      .Subject.Value.Should().BeOfType<HabitVersionRes>().Subject.HabitId;
    var update = await controller.Update(userId, habitId,
      new UpdateHabitReq("Run 5 km", ["Monday"], "19:00:00", "1.00", unknown, true, Tz));
    Status(update).Should().Be(400);
    ErrorFields(controller).Should().Contain(nameof(CreateHabitReq.CharityId));
    (await _db.Habits.AsNoTracking().SingleAsync(h => h.Id == habitId)).Version.Should().Be(1, "the update rolled back");
  }

  // ---- PUT Configuration/{userId}/{id}: timezone ----

  [SkippableTheory]
  [InlineData("Mars/Olympus")]
  [InlineData("")]
  [InlineData("   ")]
  public async Task UpdateConfiguration_NotATimezone_Is400AndNotStored(string timezone)
  {
    Skip.If(_conn == null, SkipReason);
    var (userId, _, configId) = await SeedUser();

    var res = await Configurations(userId).UpdateForUser(userId, configId, new UpdateConfigurationReq(timezone));

    Status(res).Should().Be(400);
    (await _db.Configurations.AsNoTracking().SingleAsync(c => c.Id == configId)).Timezone.Should().Be(Tz);
  }

  [SkippableFact]
  public async Task UpdateConfiguration_RealTimezone_IsStored()
  {
    Skip.If(_conn == null, SkipReason);
    var (userId, _, configId) = await SeedUser();

    var res = await Configurations(userId).UpdateForUser(userId, configId,
      new UpdateConfigurationReq("America/New_York"));

    res.Result.Should().BeOfType<OkObjectResult>();
    (await _db.Configurations.AsNoTracking().SingleAsync(c => c.Id == configId)).Timezone
      .Should().Be("America/New_York");
  }

  private sealed class AnyPlan : ISubscriptionService
  {
    public Task<Result<string>> GetUserTier(string userId) => Task.FromResult<Result<string>>("ultimate");
    public Task<Result<int>> GetLimitForTier(string tier, string key) => Task.FromResult<Result<int>>(100);
  }

  private sealed class NoTransaction : Domain.ITransactionManager
  {
    public Task<Result<T>> Start<T>(Func<Task<Result<T>>> func) => func();
  }
}
