using App.Modules.Charities.Data;
using App.Modules.Check.Data;
using App.Modules.Configurations.Data;
using App.Modules.Habit.Data;
using App.Modules.HabitVersion.Data;
using App.Modules.Proof.Data;
using App.Modules.Strava.Data;
using App.Modules.System;
using App.Modules.Users.Data;
using App.StartUp.Database;
using App.StartUp.Options;
using CSharp_Result;
using Domain.Proof;
using Domain.Strava;
using IntTest.Check;
using IntTest.Penalty;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IntTest.Strava;

// The Strava plugin end to end against a real Postgres (PENALTY_TEST_DB), with Strava's HTTP API
// faked: webhook event -> fetch -> router -> check, plus disconnect and token refresh.
[Collection(DbIntegrationCollection.Name)]
public class StravaIntegrationTests : IAsyncLifetime
{
  private const string SkipReason = "PENALTY_TEST_DB not set; skipping DB-backed Strava tests.";
  private static readonly string[] EveryDay =
    ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];
  private readonly string? _conn = Environment.GetEnvironmentVariable("PENALTY_TEST_DB");
  private MainDbContext _db = null!;
  private readonly FakeStravaApi _api = new();
  private static readonly Encryptor Encryptor =
    new(Options.Create(new EncryptionOption { Secret = new string('k', 32) }));

  private static readonly DateTime Now = new(2026, 1, 5, 18, 0, 0, DateTimeKind.Utc);
  private static readonly DateTime Morning = new(2026, 1, 5, 7, 0, 0, DateTimeKind.Utc);
  private static readonly DateOnly Monday = new(2026, 1, 5);

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

  private StravaConnectionRepository Connections() =>
    new(_db, Encryptor, NullLogger<StravaConnectionRepository>.Instance);

  private StravaService Service()
  {
    var connections = Connections();
    var registry = new ProofPluginRegistry([new ManualProofPlugin(), new StravaProofPlugin(connections)]);
    var router = new ProofRouter(registry, new ProofBindingRepository(_db, NullLogger<ProofBindingRepository>.Instance),
      Checks.Service(_db));
    return new StravaService(_api, connections, router, NullLogger<StravaService>.Instance);
  }

  private async Task<string> SeedUser()
  {
    var userId = $"user-{Guid.NewGuid():N}";
    var charityId = Guid.NewGuid();
    _db.Users.Add(new UserData { Id = userId, Username = userId, Email = $"{userId}@test.local" });
    _db.Charities.Add(new CharityData { Id = charityId, Name = $"Charity {charityId:N}" });
    _db.Configurations.Add(new ConfigurationData { UserId = userId, Timezone = "UTC", DefaultCharityId = charityId });
    await _db.SaveChangesAsync();
    return userId;
  }

  private async Task<Guid> SeedStravaHabit(string userId, string sportTypes = "\"Run\"")
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
    _db.ProofBindings.Add(new ProofBindingData
    {
      HabitId = habitId, PluginKey = StravaProofPlugin.Key, PluginVersion = 1,
      Settings = $$"""{"sportTypes":[{{sportTypes}}]}"""
    });
    await _db.SaveChangesAsync();
    return habitId;
  }

  // Connects the user through the service, as the OAuth callback does.
  private async Task Connect(string userId, long athleteId, DateTime? expiresAtUtc = null)
  {
    _api.NextGrant = new StravaGrant(athleteId,
      new StravaTokens($"access-{athleteId}", $"refresh-{athleteId}", expiresAtUtc ?? Now.AddHours(6)));
    (await Service().Connect(userId, "code", "read,activity:read_all")).Get()
      .Should().Be(StravaConnectOutcome.Connected);
  }

  private static StravaWebhookEvent Activity(string aspect, long activityId, long athleteId) => new()
  {
    ObjectType = "activity", AspectType = aspect, ObjectId = activityId, OwnerId = athleteId, EventTimeUtc = Now
  };

  private Task<HabitCheckData?> CheckOn(Guid habitId) =>
    _db.HabitChecks.AsNoTracking().SingleOrDefaultAsync(c => c.HabitId == habitId && c.LocalDate == Monday);

  [SkippableFact]
  public async Task RecordedRun_CompletesTheHabitOnce_WithoutStoringStravaData()
  {
    Skip.If(_conn == null, SkipReason);
    var userId = await SeedUser();
    var habitId = await SeedStravaHabit(userId);
    await Connect(userId, athleteId: 1001);
    _api.Activities[555] = new StravaActivity { SportType = "Run", StartDate = Morning };

    (await Service().Process(Activity("create", 555, 1001), Now)).IsSuccess().Should().BeTrue();
    (await Service().Process(Activity("update", 555, 1001), Now)).IsSuccess().Should().BeTrue(); // replay

    (await CheckOn(habitId))!.Status.Should().Be(HabitCheckStatusData.Done);
    (await CheckOn(habitId))!.SettledBy.Should().Be(StravaProofPlugin.Key);
    var evidence = await _db.HabitEvidence.AsNoTracking().SingleAsync(e => e.HabitId == habitId);
    evidence.ExternalId.Should().Be("555");
    evidence.Data.Should().BeNull();
    evidence.OccurredAtUtc.Should().Be(Morning);
    (await _db.HabitExecutions.CountAsync(e => e.HabitVersion!.HabitId == habitId)).Should().Be(1);
    _api.Fetches.Should().Equal([("access-1001", 555L), ("access-1001", 555L)]);
  }

  [SkippableFact]
  public async Task ManualOrOtherSportActivity_LeavesTheCheckPending_AndUnknownAthleteIsIgnored()
  {
    Skip.If(_conn == null, SkipReason);
    var userId = await SeedUser();
    var habitId = await SeedStravaHabit(userId);
    await Connect(userId, athleteId: 1002);
    _api.Activities[1] = new StravaActivity { SportType = "Run", StartDate = Morning, Manual = true };
    _api.Activities[2] = new StravaActivity { SportType = "Ride", StartDate = Morning };

    await Service().Process(Activity("create", 1, 1002), Now);
    await Service().Process(Activity("create", 2, 1002), Now);
    (await Service().Process(Activity("create", 1, 9999), Now)).IsSuccess().Should().BeTrue();

    (await CheckOn(habitId))!.Status.Should().Be(HabitCheckStatusData.Pending);
    (await _db.HabitEvidence.CountAsync(e => e.HabitId == habitId)).Should().Be(0);
    _api.Fetches.Should().HaveCount(2, "an athlete nobody linked is never fetched");
  }

  [SkippableFact]
  public async Task DeletedActivity_RemovesItsEvidence_ButTheDayStaysDone()
  {
    Skip.If(_conn == null, SkipReason);
    var userId = await SeedUser();
    var habitId = await SeedStravaHabit(userId);
    await Connect(userId, athleteId: 1003);
    _api.Activities[777] = new StravaActivity { SportType = "Run", StartDate = Morning };
    await Service().Process(Activity("create", 777, 1003), Now);

    (await Service().Process(Activity("delete", 777, 1003), Now)).IsSuccess().Should().BeTrue();

    (await _db.HabitEvidence.CountAsync(e => e.HabitId == habitId)).Should().Be(0);
    (await CheckOn(habitId))!.Status.Should().Be(HabitCheckStatusData.Done);
  }

  [SkippableFact]
  public async Task ExpiredToken_IsRefreshedOnce_AndStoredEncrypted()
  {
    Skip.If(_conn == null, SkipReason);
    var userId = await SeedUser();
    await SeedStravaHabit(userId);
    await Connect(userId, athleteId: 1004, expiresAtUtc: Now.AddMinutes(2)); // inside the 5-minute margin
    _api.NextRefresh = new StravaTokens("access-new", "refresh-new", Now.AddHours(6));
    _api.Activities[1] = new StravaActivity { SportType = "Run", StartDate = Morning };
    _api.Activities[2] = new StravaActivity { SportType = "Run", StartDate = Morning.AddHours(1) };

    await Service().Process(Activity("create", 1, 1004), Now);
    await Service().Process(Activity("create", 2, 1004), Now);

    _api.Refreshes.Should().Equal("refresh-1004");
    _api.Fetches.Select(f => f.Token).Should().Equal("access-new", "access-new");
    var row = await _db.StravaConnections.AsNoTracking().SingleAsync(c => c.UserId == userId);
    row.RefreshToken.Should().NotContain("refresh-new");
    (await Connections().Get(userId)).Get()!.Record.Tokens.RefreshToken.Should().Be("refresh-new");
  }

  [SkippableFact]
  public async Task RejectedRefresh_OrDeauthorize_PurgesStravaData_ButKeepsDoneDays()
  {
    Skip.If(_conn == null, SkipReason);
    // Deauthorized in Strava's settings after a run counted.
    var alice = await SeedUser();
    var aliceHabit = await SeedStravaHabit(alice);
    await Connect(alice, athleteId: 2001);
    _api.Activities[10] = new StravaActivity { SportType = "Run", StartDate = Morning };
    await Service().Process(Activity("create", 10, 2001), Now);

    await Service().Process(new StravaWebhookEvent
    {
      ObjectType = "athlete", AspectType = "update", ObjectId = 2001, OwnerId = 2001, EventTimeUtc = Now,
      Deauthorized = true
    }, Now);

    // A refresh Strava refuses (revoked elsewhere).
    var bob = await SeedUser();
    var bobHabit = await SeedStravaHabit(bob);
    await Connect(bob, athleteId: 2002, expiresAtUtc: Now.AddMinutes(-1));
    _api.NextRefresh = null;
    _api.Activities[11] = new StravaActivity { SportType = "Run", StartDate = Morning };
    await Service().Process(Activity("create", 11, 2002), Now);

    foreach (var (user, habit) in new[] { (alice, aliceHabit), (bob, bobHabit) })
    {
      (await _db.StravaConnections.AnyAsync(c => c.UserId == user)).Should().BeFalse();
      (await _db.ProofBindings.AnyAsync(b => b.HabitId == habit)).Should().BeFalse();
      (await _db.HabitEvidence.AnyAsync(e => e.HabitId == habit)).Should().BeFalse();
    }
    (await CheckOn(aliceHabit))!.Status.Should().Be(HabitCheckStatusData.Done);
    (await CheckOn(bobHabit)).Should().BeNull("bob's run was never fetched");
  }

  [SkippableFact]
  public async Task Connect_RefusesMissingScopeAndAnAthleteLinkedToSomeoneElse_ThenDisconnectRevokes()
  {
    Skip.If(_conn == null, SkipReason);
    var alice = await SeedUser();
    var mallory = await SeedUser();
    await Connect(alice, athleteId: 3001);

    var noScope = await Service().Connect(mallory, "code", "read,activity:read");
    _api.NextGrant = new StravaGrant(3001, new StravaTokens("a", "mallory-refresh", Now.AddHours(6)));
    var taken = await Service().Connect(mallory, "code", "read,activity:read_all");

    noScope.Get().Should().Be(StravaConnectOutcome.ScopeMissing);
    taken.Get().Should().Be(StravaConnectOutcome.AthleteTaken);
    _api.Revoked.Should().Equal("mallory-refresh");
    (await Connections().GetByAthlete(3001)).Get()!.UserId.Should().Be(alice);

    (await Service().Disconnect(alice)).Get().Should().NotBeNull();
    (await Service().Disconnect(alice)).Get().Should().BeNull();
    _api.Revoked.Should().Equal("mallory-refresh", "refresh-3001");
  }

  private sealed class FakeStravaApi : IStravaApi
  {
    public StravaGrant? NextGrant { get; set; }
    public StravaTokens? NextRefresh { get; set; }
    public Dictionary<long, StravaActivity> Activities { get; } = [];
    public List<(string Token, long Id)> Fetches { get; } = [];
    public List<string> Refreshes { get; } = [];
    public List<string> Revoked { get; } = [];

    public Task<Result<StravaGrant>> ExchangeCode(string code) =>
      Task.FromResult<Result<StravaGrant>>(NextGrant ?? throw new InvalidOperationException("no grant set"));

    public Task<Result<StravaTokens?>> Refresh(string refreshToken)
    {
      Refreshes.Add(refreshToken);
      return Task.FromResult<Result<StravaTokens?>>(NextRefresh);
    }

    public Task<Result<StravaActivity?>> GetActivity(string accessToken, long activityId)
    {
      Fetches.Add((accessToken, activityId));
      return Task.FromResult<Result<StravaActivity?>>(Activities.GetValueOrDefault(activityId));
    }

    public Task<Result<Unit>> Revoke(string token)
    {
      Revoked.Add(token);
      return Task.FromResult<Result<Unit>>(new Unit());
    }
  }
}
