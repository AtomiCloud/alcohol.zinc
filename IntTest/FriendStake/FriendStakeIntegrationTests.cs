using System.Text.Json;
using App.Modules.Charities.Data;
using App.Modules.Check.Data;
using App.Modules.Configurations.Data;
using App.Modules.Friend.Data;
using App.Modules.FriendStake.Data;
using App.Modules.Habit.Data;
using App.Modules.HabitVersion.Data;
using App.Modules.Protection.Data;
using App.Modules.Users.Data;
using App.StartUp.Database;
using CSharp_Result;
using Domain.Check;
using Domain.Exceptions;
using Domain.Friend;
using Domain.FriendStake;
using Domain.Notification;
using IntTest.Check;
using IntTest.Penalty;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace IntTest.FriendStake;

// "Tell my friends if I miss" against a real Postgres (PENALTY_TEST_DB): a missed check driven
// through the real CheckService outbox reaches the friends watching the habit, and only them.
[Collection(DbIntegrationCollection.Name)]
public class FriendStakeIntegrationTests : IAsyncLifetime
{
  private const string SkipReason = "PENALTY_TEST_DB not set; skipping DB-backed friend stake tests.";
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

  // Behaves like the inbox contract (one notification per user and DedupeKey) and records every
  // Post call. FailOnce makes the first post to those users fail, like a database blip.
  private sealed class RecordingInbox : IInboxService
  {
    public List<(string UserId, InboxNotificationRecord Record)> Calls { get; } = [];
    public List<(string UserId, InboxNotificationRecord Record)> Stored { get; } = [];
    public HashSet<string> FailOnce { get; } = [];

    public Task<Result<InboxNotificationPrincipal>> Post(string userId, InboxNotificationRecord record)
    {
      Calls.Add((userId, record));
      if (FailOnce.Remove(userId))
        return Task.FromResult<Result<InboxNotificationPrincipal>>(new Exception("inbox down"));
      if (!Stored.Any(s => s.UserId == userId && s.Record.DedupeKey == record.DedupeKey))
        Stored.Add((userId, record));
      return Task.FromResult<Result<InboxNotificationPrincipal>>(new InboxNotificationPrincipal
      {
        Id = Guid.NewGuid(), UserId = userId, CreatedAtUtc = DateTime.UtcNow, Record = record
      });
    }

    public List<InboxNotificationRecord> To(string userId) =>
      Stored.Where(s => s.UserId == userId).Select(s => s.Record).ToList();

    public Task<Result<List<InboxNotificationPrincipal>>> Search(InboxSearch search) => throw new NotImplementedException();
    public Task<Result<int>> UnreadCount(string userId) => throw new NotImplementedException();
    public Task<Result<Unit?>> MarkRead(string userId, Guid id) => throw new NotImplementedException();
    public Task<Result<int>> MarkAllRead(string userId) => throw new NotImplementedException();
  }

  // Friends are whatever the Friendships table says, as the real service would answer.
  private sealed class TableFriends(MainDbContext db) : IFriendService
  {
    public async Task<Result<bool>> AreFriends(string userId, string otherUserId) =>
      await db.Friendships.AsNoTracking().AnyAsync(f => f.UserId == userId && f.FriendUserId == otherUserId);

    public Task<Result<List<FriendCandidate>>> Search(string userId, string query, int limit) => throw new NotImplementedException();
    public Task<Result<FriendRelation>> SendRequest(string userId, string toUserId) => throw new NotImplementedException();
    public Task<Result<FriendRequests>> ListRequests(string userId) => throw new NotImplementedException();
    public Task<Result<FriendshipPrincipal?>> Accept(string userId, string fromUserId) => throw new NotImplementedException();
    public Task<Result<Unit?>> DeleteRequest(string userId, string otherUserId) => throw new NotImplementedException();
    public Task<Result<List<FriendshipPrincipal>>> ListFriends(string userId) => throw new NotImplementedException();
    public Task<Result<Unit?>> Remove(string userId, string friendUserId) => throw new NotImplementedException();
    public Task<Result<FriendInvitePrincipal>> CreateInvite(string userId) => throw new NotImplementedException();
    public Task<Result<FriendshipPrincipal?>> AcceptInvite(string userId, string code) => throw new NotImplementedException();
  }

  private FriendStakeRepository Repo() => new(_db, NullLogger<FriendStakeRepository>.Instance);

  private FriendStakeService Service() =>
    new(Repo(), new HabitRepository(_db, NullLogger<HabitRepository>.Instance), new TableFriends(_db));

  private FriendStakeBinding Binding(IInboxService inbox) =>
    new(Repo(),
      new CheckRepository(_db, NullLogger<CheckRepository>.Instance),
      new HabitRepository(_db, NullLogger<HabitRepository>.Instance),
      new UserRepository(_db, NullLogger<UserRepository>.Instance),
      inbox,
      NullLogger<FriendStakeBinding>.Instance);

  private async Task<string> SeedUser(string name, string? timezone = null, int freezes = 0)
  {
    var userId = $"user-{Guid.NewGuid():N}";
    _db.Users.Add(new UserData { Id = userId, Username = name, Email = $"{userId}@test.local" });
    if (timezone != null)
    {
      var charityId = Guid.NewGuid();
      _db.Charities.Add(new CharityData { Id = charityId, Name = $"Charity {charityId:N}" });
      _db.Configurations.Add(new ConfigurationData { UserId = userId, Timezone = timezone, DefaultCharityId = charityId });
    }

    if (freezes > 0) _db.UserProtections.Add(new UserProtectionData { UserId = userId, FreezeCurrent = freezes });
    await _db.SaveChangesAsync();
    return userId;
  }

  private static string Name(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..(prefix.Length + 8)];

  // The habit's own timezone is UTC on purpose: the day is the user's (Los Angeles).
  private async Task<Guid> SeedHabit(string userId, string task = "Run 5 km")
  {
    var habitId = Guid.NewGuid();
    var charityId = Guid.NewGuid();
    _db.Charities.Add(new CharityData { Id = charityId, Name = $"Charity {charityId:N}" });
    _db.Habits.Add(new HabitData { Id = habitId, UserId = userId, Version = 1, Enabled = true });
    _db.HabitVersions.Add(new HabitVersionData
    {
      HabitId = habitId, CharityId = charityId, Version = 1, Task = task, DaysOfWeek = ["Monday"],
      NotificationTime = new TimeOnly(19, 0), Timezone = "UTC",
      StakeCents = 1000, StakeCurrency = "USD", RatioBasisPoints = 10000
    });
    await _db.SaveChangesAsync();
    return habitId;
  }

  private async Task Befriend(string a, string b)
  {
    _db.Friendships.Add(new FriendshipData { UserId = a, FriendUserId = b });
    _db.Friendships.Add(new FriendshipData { UserId = b, FriendUserId = a });
    await _db.SaveChangesAsync();
  }

  private Task Unfriend(string a, string b) =>
    _db.Friendships.Where(f => (f.UserId == a && f.FriendUserId == b) || (f.UserId == b && f.FriendUserId == a))
      .ExecuteDeleteAsync();

  // Monday 5 Jan 2026 in Los Angeles ends Tue 6 Jan 07:59 UTC; at the 09:00 UTC close it is
  // already Tuesday in UTC and in LA, so only the check's own local date says Monday.
  private static readonly DateTime MondayMorningLa = U(2026, 1, 5, 17); // Mon 09:00 LA
  private static readonly DateTime AfterMondayLa = U(2026, 1, 6, 9); // Tue 01:00 LA

  [SkippableFact]
  public async Task MissedDay_TellsEachWatchingFriendOnce_NeverRemovedOrNonFriends()
  {
    Skip.If(_conn == null, SkipReason);
    var jimmy = Name("jimmy");
    var owner = await SeedUser(jimmy, LosAngeles);
    var alice = await SeedUser(Name("alice"));
    var bob = await SeedUser(Name("bob"));
    var carol = await SeedUser(Name("carol")); // watcher, then unfriended
    var dave = await SeedUser(Name("dave")); // friend, not watching
    var eve = await SeedUser(Name("eve")); // never a friend; a stale row exists anyway
    foreach (var f in new[] { alice, bob, carol, dave }) await Befriend(owner, f);
    var habitId = await SeedHabit(owner);
    var otherHabit = await SeedHabit(owner, "Read");
    // A friend watching another habit of the owner, and alice watching someone else's habit.
    var mallory = await SeedUser(Name("mal"), LosAngeles);
    await Befriend(mallory, alice);
    var mallorysHabit = await SeedHabit(mallory);

    (await Service().SetWatchers(owner, habitId, [alice, bob, carol])).Get().Should().HaveCount(3);
    (await Service().SetWatchers(owner, otherHabit, [dave])).IsSuccess().Should().BeTrue();
    (await Service().SetWatchers(mallory, mallorysHabit, [alice])).IsSuccess().Should().BeTrue();
    await Unfriend(owner, carol);
    _db.HabitFriendWatchers.Add(new HabitFriendWatcherData { HabitId = habitId, FriendUserId = eve });
    await _db.SaveChangesAsync();

    (await Service().GetWatchers(owner, habitId)).Get().Select(w => w.UserId)
      .Should().BeEquivalentTo([alice, bob], "a removed friend or a stale row is never a watcher");

    var inbox = new RecordingInbox();
    var svc = Checks.Service(_db, Binding(inbox));
    (await svc.OpenUpcoming(MondayMorningLa)).Get().Should().Be(3);
    (await svc.CloseDue(AfterMondayLa)).Get().Should().Be(3);
    (await svc.DispatchEvents(AfterMondayLa)).Get().Should().Be(3);
    (await svc.DispatchEvents(AfterMondayLa.AddHours(1))).Get().Should().Be(0);
    (await Checks.Service(_db, Binding(inbox)).DispatchEvents(AfterMondayLa.AddHours(2))).Get().Should().Be(0);

    var check = await _db.HabitChecks.AsNoTracking().SingleAsync(c => c.HabitId == habitId);
    check.Status.Should().Be(HabitCheckStatusData.Missed);
    check.LocalDate.Should().Be(new DateOnly(2026, 1, 5));

    inbox.To(alice).Where(r => r.DedupeKey == $"friend_missed:{check.Id}").Should().ContainSingle();
    inbox.To(bob).Should().ContainSingle();
    inbox.To(carol).Should().BeEmpty("carol is no longer a friend");
    inbox.To(eve).Should().BeEmpty("eve was never a friend");
    inbox.To(owner).Should().BeEmpty();
    // dave watches "Read" and mallory's habit tells alice: each check tells only its own watchers.
    inbox.To(dave).Should().ContainSingle().Which.Body.Should().Contain("\"Read\"");
    inbox.To(alice).Should().HaveCount(2);
    inbox.Calls.Should().HaveCount(4, "an event is delivered to the friends binding once");

    var r = inbox.To(bob).Single();
    r.Kind.Should().Be(InboxKinds.FriendMissed);
    r.Title.Should().Be($"{jimmy} missed a habit");
    r.Body.Should().Be($"{jimmy} missed \"Run 5 km\" on Mon 5 Jan");
    r.ActorUserId.Should().Be(owner);
    r.DedupeKey.Should().Be($"friend_missed:{check.Id}");
    using var data = JsonDocument.Parse(r.Data!);
    data.RootElement.GetProperty("habitId").GetGuid().Should().Be(habitId);
    data.RootElement.GetProperty("userId").GetString().Should().Be(owner);
    data.RootElement.GetProperty("localDate").GetString().Should().Be("2026-01-05");
    data.RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["habitId", "userId", "localDate"]);

    var ev = await _db.CheckEvents.AsNoTracking().SingleAsync(e => e.HabitId == habitId);
    ev.DispatchedAtUtc.Should().NotBeNull();
    (await _db.CheckEventDeliveries.AsNoTracking().Where(d => d.EventId == ev.Id).Select(d => d.BindingId).ToListAsync())
      .Should().BeEquivalentTo(["penalty", "friends"], "the charity penalty still applies alongside");

    // A crash between Deliver and RecordDelivery delivers the same event again: posted again
    // with the same key, so the inbox still holds one each.
    var again = await Binding(inbox).Deliver(new CheckEventPrincipal
    {
      Id = ev.Id, CheckId = ev.CheckId, HabitId = ev.HabitId, UserId = ev.UserId,
      Outcome = CheckStatus.Missed, ExecutionId = ev.ExecutionId, Attempts = ev.Attempts
    });
    again.IsSuccess().Should().BeTrue();
    inbox.Calls.Should().HaveCount(6);
    inbox.To(bob).Should().ContainSingle();
    inbox.To(alice).Should().HaveCount(2);
  }

  // A watcher whose post fails fails the delivery, so the outbox retries it; the others were
  // already told and the stable DedupeKey keeps them at one notification.
  [SkippableFact]
  public async Task PostFails_OutboxRetries_EachWatcherEndsWithOneNotification()
  {
    Skip.If(_conn == null, SkipReason);
    var owner = await SeedUser(Name("jimmy"), LosAngeles);
    var alice = await SeedUser(Name("alice"));
    var bob = await SeedUser(Name("bob"));
    await Befriend(owner, alice);
    await Befriend(owner, bob);
    var habitId = await SeedHabit(owner);
    await Service().SetWatchers(owner, habitId, [alice, bob]);

    var inbox = new RecordingInbox();
    inbox.FailOnce.Add(bob);
    var svc = Checks.Service(_db, Binding(inbox));
    await svc.OpenUpcoming(MondayMorningLa);
    await svc.CloseDue(AfterMondayLa);

    (await svc.DispatchEvents(AfterMondayLa)).Get().Should().Be(0, "bob's post failed");
    var ev = await _db.CheckEvents.AsNoTracking().SingleAsync(e => e.HabitId == habitId);
    ev.DispatchedAtUtc.Should().BeNull();
    ev.LastError.Should().Contain(bob);
    inbox.To(alice).Should().ContainSingle("alice is told even though bob's post failed");
    inbox.To(bob).Should().BeEmpty();

    (await svc.DispatchEvents(AfterMondayLa.AddMinutes(5))).Get().Should().Be(1);
    (await svc.DispatchEvents(AfterMondayLa.AddHours(1))).Get().Should().Be(0);

    inbox.Calls.Count(c => c.UserId == alice).Should().Be(2, "the retry posts to everyone again");
    inbox.Calls.Select(c => c.Record.DedupeKey).Distinct().Should().ContainSingle();
    inbox.To(alice).Should().ContainSingle();
    inbox.To(bob).Should().ContainSingle();
    (await _db.HabitChecks.AsNoTracking().SingleAsync(c => c.HabitId == habitId)).Status
      .Should().Be(HabitCheckStatusData.Missed);
  }

  [SkippableTheory]
  [InlineData("done")]
  [InlineData("skipped")]
  [InlineData("frozen")]
  public async Task DayNotMissed_TellsNoOne(string outcome)
  {
    Skip.If(_conn == null, SkipReason);
    var owner = await SeedUser(Name("jimmy"), LosAngeles, freezes: outcome == "frozen" ? 1 : 0);
    var alice = await SeedUser(Name("alice"));
    await Befriend(owner, alice);
    var habitId = await SeedHabit(owner);
    await Service().SetWatchers(owner, habitId, [alice]);
    var inbox = new RecordingInbox();
    var svc = Checks.Service(_db, Binding(inbox));
    await svc.OpenUpcoming(MondayMorningLa);

    if (outcome == "done")
      (await svc.Report(owner, habitId, Checks.Manual(MondayMorningLa), MondayMorningLa)).Get().Should().NotBeNull();
    if (outcome == "skipped")
      (await svc.Skip(owner, habitId, null, MondayMorningLa)).Get().Should().NotBeNull();
    await svc.CloseDue(AfterMondayLa);
    await svc.DispatchEvents(AfterMondayLa);

    var expected = outcome switch
    {
      "done" => HabitCheckStatusData.Done,
      "skipped" => HabitCheckStatusData.Skipped,
      _ => HabitCheckStatusData.Frozen
    };
    (await _db.HabitChecks.AsNoTracking().SingleAsync(c => c.HabitId == habitId)).Status.Should().Be(expected);
    var ev = await _db.CheckEvents.AsNoTracking().SingleAsync(e => e.HabitId == habitId);
    ev.DispatchedAtUtc.Should().NotBeNull();
    inbox.Calls.Should().BeEmpty();
    (await _db.CheckEventDeliveries.AnyAsync(d => d.EventId == ev.Id && d.BindingId == "friends")).Should().BeFalse();
  }

  [Fact]
  public void AppliesTo_OnlyMissed()
  {
    var binding = new FriendStakeBinding(null!, null!, null!, null!, null!, NullLogger<FriendStakeBinding>.Instance);
    foreach (var status in Enum.GetValues<CheckStatus>())
    {
      var e = new CheckEventPrincipal
      {
        Id = Guid.NewGuid(), CheckId = Guid.NewGuid(), HabitId = Guid.NewGuid(), UserId = "u",
        Outcome = status, ExecutionId = Guid.NewGuid(), Attempts = 0
      };
      binding.AppliesTo(e).Should().Be(status == CheckStatus.Missed, status.ToString());
    }

    binding.BindingId.Should().Be("friends");
  }

  [SkippableFact]
  public async Task SetWatchers_ValidatesOwnershipFriendsAndCap_DedupesAndClears()
  {
    Skip.If(_conn == null, SkipReason);
    var owner = await SeedUser(Name("jimmy"), LosAngeles);
    var alice = await SeedUser(Name("alice"));
    var bob = await SeedUser(Name("bob"));
    var stranger = await SeedUser(Name("zed"));
    var outsider = await SeedUser(Name("oscar"), LosAngeles);
    await Befriend(owner, alice);
    await Befriend(owner, bob);
    await Befriend(outsider, alice);
    // A friendship one way only is not a friendship.
    _db.Friendships.Add(new FriendshipData { UserId = stranger, FriendUserId = owner });
    await _db.SaveChangesAsync();
    var habitId = await SeedHabit(owner);
    var service = Service();

    (await service.SetWatchers(owner, habitId, [alice])).Get().Select(w => w.UserId).Should().Equal(alice);

    var rejected = (await service.SetWatchers(owner, habitId, [bob, stranger, "ghost"])).FailureOrDefault();
    var ire = rejected.Should().BeOfType<InvalidRequestException>().Subject;
    ire.Errors.Should().ContainKey("friendUserIds");
    var message = ire.Errors["friendUserIds"].Single();
    message.Should().Contain(stranger).And.Contain("ghost").And.NotContain(bob);
    (await service.GetWatchers(owner, habitId)).Get().Select(w => w.UserId)
      .Should().Equal([alice], "a rejected set changes nothing");

    (await service.SetWatchers(outsider, habitId, [alice])).FailureOrDefault()
      .Should().BeOfType<NotFoundException>("it is another user's habit");
    (await service.GetWatchers(outsider, habitId)).FailureOrDefault().Should().BeOfType<NotFoundException>();
    (await service.SetWatchers(owner, Guid.NewGuid(), [alice])).FailureOrDefault().Should().BeOfType<NotFoundException>();

    var tooMany = Enumerable.Range(0, FriendStakeService.MaxWatchers + 1).Select(i => $"friend-{i}").ToList();
    (await service.SetWatchers(owner, habitId, tooMany)).FailureOrDefault().Should().BeOfType<InvalidRequestException>();

    var deduped = (await service.SetWatchers(owner, habitId, [bob, alice, bob, alice, bob])).Get();
    deduped.Select(w => w.UserId).Should().BeEquivalentTo([alice, bob]);
    deduped.Single(w => w.UserId == bob).Username.Should().Be((await _db.Users.FindAsync(bob))!.Username);
    (await _db.HabitFriendWatchers.CountAsync(w => w.HabitId == habitId)).Should().Be(2);

    (await service.SetWatchers(owner, habitId, [])).Get().Should().BeEmpty();
    (await _db.HabitFriendWatchers.CountAsync(w => w.HabitId == habitId)).Should().Be(0);

    await service.SetWatchers(owner, habitId, [alice]);
    await _db.Habits.Where(h => h.Id == habitId)
      .ExecuteUpdateAsync(s => s.SetProperty(h => h.DeletedAt, DateTime.UtcNow));
    (await service.GetWatchers(owner, habitId)).FailureOrDefault().Should().BeOfType<NotFoundException>("deleted");
    (await service.SetWatchers(owner, habitId, [alice])).FailureOrDefault().Should().BeOfType<NotFoundException>();
    (await Repo().GetWatchers(habitId)).Get().Should().BeEmpty("a deleted habit tells no one");
  }
}
