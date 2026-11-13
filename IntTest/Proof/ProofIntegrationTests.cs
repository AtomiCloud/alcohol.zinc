using App.Modules.Charities.Data;
using App.Modules.Check.Data;
using App.Modules.Configurations.Data;
using App.Modules.Habit.Data;
using App.Modules.HabitVersion.Data;
using App.Modules.Proof.Data;
using App.Modules.Users.Data;
using App.StartUp.Database;
using CSharp_Result;
using Domain.Check;
using Domain.Entitlement;
using Domain.Exceptions;
using Domain.Habit;
using Domain.Proof;
using IntTest.Check;
using IntTest.Penalty;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace IntTest.Proof;

// The proof plugin framework against a real Postgres (PENALTY_TEST_DB): the router reporting
// plugin verdicts through the real CheckService, and binding through the real repository.
[Collection(DbIntegrationCollection.Name)]
public class ProofIntegrationTests : IAsyncLifetime
{
  private const string SkipReason = "PENALTY_TEST_DB not set; skipping DB-backed proof tests.";
  private static readonly string[] EveryDay =
    ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];
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

  private static DateTime U(int y, int m, int d, int h) => new(y, m, d, h, 0, 0, DateTimeKind.Utc);

  // A session-counting plugin like Strava v1: passes on an activity of a bound sport type.
  private sealed class RunSettings
  {
    public string[] SportTypes { get; set; } = [];
  }

  private sealed class RunEvent
  {
    public string SportType { get; set; } = "";
  }

  private sealed class RunPlugin(string? minTier = null) : ProofPlugin<RunSettings, RunEvent>
  {
    public List<string> Calls { get; } = [];

    public override PluginManifest Manifest { get; } = new()
    {
      Key = "run", Name = "Run", Version = 3, Platforms = ["ios"], SettingsSchema = "{}", MinTier = minTier
    };

    protected override Task<Result<Unit>> OnBind(ProofBinding binding, RunSettings settings)
    {
      Calls.Add($"bind:{string.Join(',', settings.SportTypes)}");
      return Task.FromResult<Result<Unit>>(new Unit());
    }

    protected override Task<Result<Unit>> OnUnbind(ProofBinding binding, RunSettings settings)
    {
      Calls.Add($"unbind:{string.Join(',', settings.SportTypes)}");
      return Task.FromResult<Result<Unit>>(new Unit());
    }

    protected override Task<Result<ProofVerdict?>> Evaluate(RunSettings settings, RunEvent payload, ProofEvent e,
      ProofWindow window) =>
      Task.FromResult<Result<ProofVerdict?>>(settings.SportTypes.Contains(payload.SportType)
        ? new ProofVerdict(EvidenceVerdict.Passed, new { payload.SportType })
        : null);
  }

  // Refuses plugins with a MinTier, standing in for a free user.
  private sealed class FreeUserEntitlements : IEntitlementService
  {
    public Task<Result<Unit>> EnsureProofPluginAllowed(string userId, PluginManifest plugin) =>
      Task.FromResult<Result<Unit>>(plugin.MinTier == null
        ? new Unit()
        : new InvalidOperationException($"{plugin.Key} needs {plugin.MinTier}"));

    public Task<Result<Quota>> HabitQuota(string userId) => throw new NotImplementedException();
    public Task<Result<Quota>> SkipQuota(string userId) => throw new NotImplementedException();
    public Task<Result<Quota>> VacationWindowQuota(string userId) => throw new NotImplementedException();
    public Task<Result<int>> GetFreezeCapForUser(string userId, int userMaxStreak) => throw new NotImplementedException();
    public Task<Result<Unit>> EnsureHabitsAllowed(string userId) => throw new NotImplementedException();
  }

  private ProofBindingRepository Bindings() => new(_db, NullLogger<ProofBindingRepository>.Instance);

  private ProofRouter Router(params IProofPlugin[] plugins) =>
    new(new ProofPluginRegistry([new ManualProofPlugin(), .. plugins]), Bindings(), Checks.Service(_db));

  private ProofService Service(IProofPlugin plugin) =>
    new(new ProofPluginRegistry([new ManualProofPlugin(), plugin]), Bindings(),
      new HabitRepository(_db, NullLogger<HabitRepository>.Instance), new FreeUserEntitlements());

  private async Task<string> SeedUser(string timezone = "UTC")
  {
    var userId = $"user-{Guid.NewGuid():N}";
    var charityId = Guid.NewGuid();
    _db.Users.Add(new UserData { Id = userId, Username = userId, Email = $"{userId}@test.local" });
    _db.Charities.Add(new CharityData { Id = charityId, Name = $"Charity {charityId:N}" });
    _db.Configurations.Add(new ConfigurationData { UserId = userId, Timezone = timezone, DefaultCharityId = charityId });
    await _db.SaveChangesAsync();
    return userId;
  }

  private async Task<Guid> SeedHabit(string userId)
  {
    var habitId = Guid.NewGuid();
    var charityId = Guid.NewGuid();
    _db.Charities.Add(new CharityData { Id = charityId, Name = $"Charity {charityId:N}" });
    _db.Habits.Add(new HabitData { Id = habitId, UserId = userId, Version = 1, Enabled = true });
    _db.HabitVersions.Add(new HabitVersionData
    {
      HabitId = habitId, CharityId = charityId, Version = 1, Task = "Run", DaysOfWeek = EveryDay,
      NotificationTime = new TimeOnly(19, 0), Timezone = "UTC",
      StakeCents = 0, StakeCurrency = "USD", RatioBasisPoints = 10000
    });
    await _db.SaveChangesAsync();
    return habitId;
  }

  private async Task Bind(Guid habitId, params string[] sportTypes)
  {
    _db.ProofBindings.Add(new ProofBindingData
    {
      HabitId = habitId, PluginKey = "run", PluginVersion = 3,
      Settings = $$"""{"sportTypes":[{{string.Join(',', sportTypes.Select(s => $"\"{s}\""))}}]}"""
    });
    await _db.SaveChangesAsync();
  }

  private static ProofEvent Activity(string userId, string sport, DateTime at, string? externalId = null) => new()
  {
    PluginKey = "run", UserId = userId, ExternalId = externalId ?? Guid.NewGuid().ToString(),
    OccurredAtUtc = at, Payload = $$"""{"sportType":"{{sport}}","distance":4990}"""
  };

  private Task<HabitCheckData?> CheckOn(Guid habitId, DateOnly date) =>
    _db.HabitChecks.AsNoTracking().SingleOrDefaultAsync(c => c.HabitId == habitId && c.LocalDate == date);

  // The Complete button, now routed through the manual plugin: done once, then 404 as before.
  [SkippableFact]
  public async Task CompleteHabit_ThroughManualPlugin_CompletesOnceThenNotCompletable()
  {
    Skip.If(_conn == null, SkipReason);
    // CompleteHabit reads the real clock: put the user where it is about noon, far from the
    // 23:59 deadline. Etc/GMT signs are inverted (Etc/GMT-5 is UTC+5).
    var nowUtc = DateTime.UtcNow;
    var offset = 12 - nowUtc.Hour;
    var userId = await SeedUser(offset == 0 ? "UTC" : $"Etc/GMT{(offset > 0 ? "-" : "+")}{Math.Abs(offset)}");
    var habitId = await SeedHabit(userId);
    var versionId = await _db.HabitVersions.Where(v => v.HabitId == habitId).Select(v => v.Id).SingleAsync();
    var checks = Checks.Service(_db);
    var habits = new HabitService(new HabitRepository(_db, NullLogger<HabitRepository>.Instance), null!, checks,
      Router(), NullLogger<HabitService>.Instance);

    var first = await habits.CompleteHabit(userId, versionId, "felt good");
    var second = await habits.CompleteHabit(userId, versionId, null);
    var stranger = await habits.CompleteHabit(await SeedUser(), versionId, null);

    first.Get().Record.Status.Should().Be(ExecutionStatus.Completed);
    first.Get().Record.Notes.Should().Be("felt good");
    second.FailureOrDefault().Should().BeOfType<NotFoundException>();
    stranger.FailureOrDefault().Should().BeOfType<NotFoundException>();
    var check = await CheckOn(habitId, DateOnly.FromDateTime(nowUtc.AddHours(offset)));
    check!.Status.Should().Be(HabitCheckStatusData.Done);
    check.SettledBy.Should().Be(ManualProofPlugin.Key);
  }

  // A user-wide event (a Strava activity) proves only the user's habits bound to that plugin.
  [SkippableFact]
  public async Task Route_UserWideEvent_ProvesOnlyBoundHabitsOfThatUser()
  {
    Skip.If(_conn == null, SkipReason);
    var userId = await SeedUser();
    var running = await SeedHabit(userId);
    var reading = await SeedHabit(userId); // unbound: manual only
    var otherUser = await SeedUser();
    var othersRun = await SeedHabit(otherUser);
    await Bind(running, "Run", "TrailRun");
    await Bind(othersRun, "Run");
    var now = U(2026, 1, 5, 18);
    var monday = new DateOnly(2026, 1, 5);

    var outcomes = (await Router(new RunPlugin()).Route(Activity(userId, "Run", U(2026, 1, 5, 7)), now)).Get();

    outcomes.Select(o => o.HabitId).Should().Equal(running);
    outcomes.Single().Execution!.Record.Status.Should().Be(ExecutionStatus.Completed);
    var evidence = await _db.HabitEvidence.AsNoTracking().SingleAsync(e => e.HabitId == running);
    evidence.ProofKey.Should().Be("run");
    evidence.Data.Should().Be("""{"sportType":"Run"}""");
    evidence.OccurredAtUtc.Should().Be(U(2026, 1, 5, 7));
    (await CheckOn(reading, monday)).Should().BeNull("an unbound habit is never evaluated");
    (await CheckOn(othersRun, monday)).Should().BeNull("another user's binding is never evaluated");
  }

  [SkippableFact]
  public async Task Route_SameEventTwice_CompletesOnce_AndIrrelevantEventLeavesCheckPending()
  {
    Skip.If(_conn == null, SkipReason);
    var userId = await SeedUser();
    var habitId = await SeedHabit(userId);
    await Bind(habitId, "Run");
    var router = Router(new RunPlugin());
    var now = U(2026, 1, 6, 18);
    var tuesday = new DateOnly(2026, 1, 6);

    var ride = (await router.Route(Activity(userId, "Ride", U(2026, 1, 6, 8)), now)).Get();
    ride.Single().Execution.Should().BeNull();
    (await CheckOn(habitId, tuesday))!.Status.Should().Be(HabitCheckStatusData.Pending);

    var run = Activity(userId, "Run", U(2026, 1, 6, 9), externalId: "strava-123");
    var first = (await router.Route(run, now)).Get();
    var replay = (await router.Route(run, now)).Get();

    first.Single().Execution.Should().NotBeNull();
    replay.Single().Execution.Should().BeNull();
    (await _db.HabitExecutions.CountAsync(e => e.HabitVersion!.HabitId == habitId)).Should().Be(1);
    (await CheckOn(habitId, tuesday))!.Status.Should().Be(HabitCheckStatusData.Done);
  }

  // A habit-targeted event of a plugin the habit isn't bound to does nothing.
  [SkippableFact]
  public async Task Route_TargetedEvent_ForUnboundPlugin_DoesNothing()
  {
    Skip.If(_conn == null, SkipReason);
    var userId = await SeedUser();
    var habitId = await SeedHabit(userId);

    var outcomes = (await Router(new RunPlugin()).Route(
      Activity(userId, "Run", U(2026, 1, 5, 7)) with { HabitId = habitId }, U(2026, 1, 5, 8))).Get();

    outcomes.Should().BeEmpty();
    (await CheckOn(habitId, new DateOnly(2026, 1, 5))).Should().BeNull();
  }

  [SkippableFact]
  public async Task Bind_ValidatesChecksTierAndReplaces_ThenUnbindReturnsToManual()
  {
    Skip.If(_conn == null, SkipReason);
    var userId = await SeedUser();
    var habitId = await SeedHabit(userId);
    var plugin = new RunPlugin();
    var service = Service(plugin);

    (await service.Bind(userId, habitId, "run", """{"sportTyps":["Run"]}""")).FailureOrDefault()
      .Should().BeOfType<InvalidProofBindingException>();
    (await service.Bind(userId, habitId, "manual", "{}")).FailureOrDefault()
      .Should().BeOfType<InvalidProofBindingException>();
    (await service.Bind(await SeedUser(), habitId, "run", """{"sportTypes":["Run"]}""")).FailureOrDefault()
      .Should().BeOfType<NotFoundException>();
    (await service.Bind(userId, habitId, "swim", "{}")).FailureOrDefault()
      .Should().BeOfType<NotFoundException>();
    (await Service(new RunPlugin(minTier: "pro")).Bind(userId, habitId, "run", """{"sportTypes":["Run"]}"""))
      .IsFailure().Should().BeTrue();
    (await _db.ProofBindings.CountAsync(b => b.HabitId == habitId)).Should().Be(0);
    (await service.GetBinding(userId, habitId)).Get().Should().BeNull();

    (await service.Bind(userId, habitId, "run", """{"sportTypes":["Run"]}""")).IsSuccess().Should().BeTrue();
    var rebound = (await service.Bind(userId, habitId, "run", """{"SportTypes":["Run","Walk"]}""")).Get();

    rebound.Record.PluginVersion.Should().Be(3);
    rebound.Record.Settings.Should().Be("""{"sportTypes":["Run","Walk"]}""");
    (await _db.ProofBindings.CountAsync(b => b.HabitId == habitId)).Should().Be(1);
    plugin.Calls.Should().Equal("bind:Run", "unbind:Run", "bind:Run,Walk");

    (await service.Unbind(userId, habitId)).Get().Should().NotBeNull();
    (await service.Unbind(userId, habitId)).Get().Should().BeNull();
    (await service.GetBinding(userId, habitId)).Get().Should().BeNull();
    plugin.Calls.Last().Should().Be("unbind:Run,Walk");
  }
}
