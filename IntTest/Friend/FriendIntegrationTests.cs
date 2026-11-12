using App.Modules.Friend.Data;
using App.Modules.FriendStake.Data;
using App.Modules.Habit.Data;
using App.Modules.Users.Data;
using App.StartUp.Database;
using CSharp_Result;
using Domain.Exceptions;
using Domain.Friend;
using Domain.Notification;
using EntityFramework.Exceptions.Common;
using IntTest.Penalty;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IntTest.Friend;

// Friends against a real Postgres (PENALTY_TEST_DB): the real FriendService over the real
// FriendRepository, with a recording inbox standing in for IInboxService.
[Collection(DbIntegrationCollection.Name)]
public class FriendIntegrationTests : IAsyncLifetime
{
  private const string SkipReason = "PENALTY_TEST_DB not set; skipping DB-backed friend tests.";
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

  private sealed class RecordingInbox(bool fail = false, bool throws = false) : IInboxService
  {
    public List<(string UserId, InboxNotificationRecord Record)> Posts { get; } = [];

    public Task<Result<InboxNotificationPrincipal>> Post(string userId, InboxNotificationRecord record)
    {
      Posts.Add((userId, record));
      if (throws) throw new InvalidOperationException("inbox down");
      if (fail) return Task.FromResult<Result<InboxNotificationPrincipal>>(new InvalidOperationException("inbox down"));
      return Task.FromResult<Result<InboxNotificationPrincipal>>(new InboxNotificationPrincipal
      {
        Id = Guid.NewGuid(), UserId = userId, CreatedAtUtc = DateTime.UtcNow, Record = record
      });
    }

    public Task<Result<List<InboxNotificationPrincipal>>> Search(InboxSearch search) => throw new NotImplementedException();
    public Task<Result<int>> UnreadCount(string userId) => throw new NotImplementedException();
    public Task<Result<Unit?>> MarkRead(string userId, Guid id) => throw new NotImplementedException();
    public Task<Result<int>> MarkAllRead(string userId) => throw new NotImplementedException();
  }

  private FriendRepository Repo() => new(_db, NullLogger<FriendRepository>.Instance);

  private FriendService Service(IInboxService inbox) => new(Repo(), inbox, NullLogger<FriendService>.Instance);

  private async Task<string> SeedUser(string username)
  {
    var userId = $"user-{Guid.NewGuid():N}";
    _db.Users.Add(new UserData { Id = userId, Username = username, Email = $"{userId}@test.local" });
    await _db.SaveChangesAsync();
    return userId;
  }

  private async Task<Guid> SeedHabit(string ownerId)
  {
    var habitId = Guid.NewGuid();
    _db.Habits.Add(new HabitData { Id = habitId, UserId = ownerId, Version = 1, Enabled = true });
    await _db.SaveChangesAsync();
    return habitId;
  }

  private async Task Watch(Guid habitId, string friendUserId)
  {
    _db.HabitFriendWatchers.Add(new HabitFriendWatcherData { HabitId = habitId, FriendUserId = friendUserId });
    await _db.SaveChangesAsync();
  }

  private async Task<List<(string, string)>> FriendshipRows() =>
    (await _db.Friendships.AsNoTracking().ToListAsync()).Select(x => (x.UserId, x.FriendUserId)).ToList();

  [SkippableFact]
  public async Task RequestThenAccept_BothAreFriends_RequestGone_OneNotificationEachWay()
  {
    Skip.If(_conn == null, SkipReason);
    var alice = await SeedUser("alice");
    var bob = await SeedUser("bob");
    var inbox = new RecordingInbox();
    var service = Service(inbox);

    (await service.SendRequest(alice, bob)).Get().Should().Be(FriendRelation.Outgoing);
    (await service.AreFriends(alice, bob)).Get().Should().BeFalse("a request alone is not a friendship");
    var bobRequests = (await service.ListRequests(bob)).Get();
    bobRequests.Incoming.Select(r => r.From.Username).Should().Equal("alice");
    bobRequests.Outgoing.Should().BeEmpty();
    (await service.ListRequests(alice)).Get().Outgoing.Select(r => r.To.Username).Should().Equal("bob");

    var friendship = (await service.Accept(bob, alice)).Get();

    friendship!.Friend.Should().Be(new FriendUser(alice, "alice"));
    (await service.ListFriends(alice)).Get().Select(f => f.Friend.Username).Should().Equal("bob");
    (await service.ListFriends(bob)).Get().Select(f => f.Friend.Username).Should().Equal("alice");
    (await FriendshipRows()).Should().BeEquivalentTo([(alice, bob), (bob, alice)]);
    (await _db.FriendRequests.CountAsync()).Should().Be(0);
    (await service.AreFriends(bob, alice)).Get().Should().BeTrue();

    inbox.Posts.Should().HaveCount(2);
    var request = inbox.Posts[0];
    request.UserId.Should().Be(bob);
    request.Record.Kind.Should().Be(InboxKinds.FriendRequest);
    request.Record.Body.Should().Be("alice wants to be friends");
    request.Record.ActorUserId.Should().Be(alice);
    request.Record.Data.Should().Be($$"""{"fromUserId":"{{alice}}"}""");
    var accepted = inbox.Posts[1];
    accepted.UserId.Should().Be(alice);
    accepted.Record.Kind.Should().Be(InboxKinds.FriendAccepted);
    accepted.Record.ActorUserId.Should().Be(bob);

    // Accepting again: no request any more.
    (await service.Accept(bob, alice)).Get().Should().BeNull();
    inbox.Posts.Should().HaveCount(2);
  }

  [SkippableFact]
  public async Task CrossedRequests_BecomeFriendship()
  {
    Skip.If(_conn == null, SkipReason);
    var alice = await SeedUser("alice");
    var bob = await SeedUser("bob");
    var inbox = new RecordingInbox();
    var service = Service(inbox);

    await service.SendRequest(alice, bob);
    var relation = (await service.SendRequest(bob, alice)).Get();

    relation.Should().Be(FriendRelation.Friend);
    (await FriendshipRows()).Should().BeEquivalentTo([(alice, bob), (bob, alice)]);
    (await _db.FriendRequests.CountAsync()).Should().Be(0);
    inbox.Posts.Select(p => (p.UserId, p.Record.Kind)).Should()
      .Equal((bob, InboxKinds.FriendRequest), (alice, InboxKinds.FriendAccepted));
  }

  [SkippableFact]
  public async Task DuplicateRequest_AndRequestToFriend_AreNoOps_WithoutNotifications()
  {
    Skip.If(_conn == null, SkipReason);
    var alice = await SeedUser("alice");
    var bob = await SeedUser("bob");
    var carol = await SeedUser("carol");
    var inbox = new RecordingInbox();
    var service = Service(inbox);

    (await service.SendRequest(alice, bob)).Get().Should().Be(FriendRelation.Outgoing);
    (await service.SendRequest(alice, bob)).Get().Should().Be(FriendRelation.Outgoing);
    (await _db.FriendRequests.CountAsync()).Should().Be(1);
    inbox.Posts.Should().HaveCount(1);

    await service.SendRequest(alice, carol);
    await service.Accept(carol, alice);
    var before = inbox.Posts.Count;
    (await service.SendRequest(alice, carol)).Get().Should().Be(FriendRelation.Friend);
    (await service.SendRequest(carol, alice)).Get().Should().Be(FriendRelation.Friend);
    inbox.Posts.Should().HaveCount(before);
    (await _db.FriendRequests.CountAsync(r => r.FromUserId == carol || r.ToUserId == carol)).Should().Be(0);
  }

  [SkippableFact]
  public async Task SendRequest_RefusesSelfAndUnknownUser()
  {
    Skip.If(_conn == null, SkipReason);
    var alice = await SeedUser("alice");
    var service = Service(new RecordingInbox());

    (await service.SendRequest(alice, alice)).FailureOrDefault().Should().BeOfType<InvalidRequestException>();
    (await service.SendRequest(alice, "nobody")).FailureOrDefault().Should().BeOfType<NotFoundException>();
    (await _db.FriendRequests.CountAsync()).Should().Be(0);
  }

  [SkippableFact]
  public async Task DeclineAndCancel_DeleteTheRequestSilently()
  {
    Skip.If(_conn == null, SkipReason);
    var alice = await SeedUser("alice");
    var bob = await SeedUser("bob");
    var carol = await SeedUser("carol");
    var inbox = new RecordingInbox();
    var service = Service(inbox);
    await service.SendRequest(alice, bob);
    await service.SendRequest(alice, carol);

    (await service.DeleteRequest(bob, alice)).Get().Should().NotBeNull("bob declines");
    (await service.DeleteRequest(alice, carol)).Get().Should().NotBeNull("alice cancels");
    (await service.DeleteRequest(alice, carol)).Get().Should().BeNull("already gone");

    (await _db.FriendRequests.CountAsync()).Should().Be(0);
    (await _db.Friendships.CountAsync()).Should().Be(0);
    (await service.Accept(bob, alice)).Get().Should().BeNull();
    inbox.Posts.Should().HaveCount(2, "only the two requests notified");
  }

  [SkippableFact]
  public async Task Remove_DeletesBothDirections_AndMutualWatchers_KeepsThirdPartyWatchers()
  {
    Skip.If(_conn == null, SkipReason);
    var alice = await SeedUser("alice");
    var bob = await SeedUser("bob");
    var carol = await SeedUser("carol");
    var service = Service(new RecordingInbox());
    await service.SendRequest(alice, bob);
    await service.Accept(bob, alice);
    await service.SendRequest(alice, carol);
    await service.Accept(carol, alice);

    var aliceHabit = await SeedHabit(alice);
    var bobHabit = await SeedHabit(bob);
    var carolHabit = await SeedHabit(carol);
    await Watch(aliceHabit, bob); // gone
    await Watch(bobHabit, alice); // gone
    await Watch(aliceHabit, carol); // stays
    await Watch(carolHabit, alice); // stays
    await Watch(carolHabit, bob); // stays
    await Watch(bobHabit, carol); // stays

    (await service.Remove(alice, bob)).Get().Should().NotBeNull();

    (await FriendshipRows()).Should().BeEquivalentTo([(alice, carol), (carol, alice)]);
    var watchers = (await _db.HabitFriendWatchers.AsNoTracking().ToListAsync())
      .Select(x => (x.HabitId, x.FriendUserId)).ToList();
    watchers.Should().BeEquivalentTo([(aliceHabit, carol), (carolHabit, alice), (carolHabit, bob), (bobHabit, carol)]);
    (await service.Remove(bob, alice)).Get().Should().BeNull("no longer friends");
  }

  [SkippableFact]
  public async Task Search_PrefixCaseInsensitive_ExcludesSelf_WithRelations()
  {
    Skip.If(_conn == null, SkipReason);
    var me = await SeedUser("alfred");
    var friend = await SeedUser("Alice");
    var outgoing = await SeedUser("alan");
    var incoming = await SeedUser("ALBERT");
    var none = await SeedUser("alma");
    await SeedUser("malcolm"); // contains "al" but doesn't start with it
    await SeedUser("bob");
    var service = Service(new RecordingInbox());
    await service.SendRequest(me, friend);
    await service.Accept(friend, me);
    await service.SendRequest(me, outgoing);
    await service.SendRequest(incoming, me);

    var found = (await service.Search(me, "  aL ", 20)).Get();

    found.Select(c => (c.User.UserId, c.Relation)).Should().BeEquivalentTo([
      (outgoing, FriendRelation.Outgoing),
      (incoming, FriendRelation.Incoming),
      (friend, FriendRelation.Friend),
      (none, FriendRelation.None),
    ]);
    found.Select(c => c.User.Username).Should().Equal("alan", "ALBERT", "Alice", "alma");
    (await service.Search(me, "al", 2)).Get().Should().HaveCount(2);
  }

  [SkippableFact]
  public async Task Search_EscapesLikeWildcards()
  {
    Skip.If(_conn == null, SkipReason);
    var me = await SeedUser("me_myself");
    var underscore = await SeedUser("a_b");
    var percent = await SeedUser("a%c");
    var backslash = await SeedUser(@"a\d");
    await SeedUser("axb");
    await SeedUser("abc");
    var leadingUnderscore = await SeedUser("_zed");
    var repo = Repo();
    var service = Service(new RecordingInbox());

    (await service.Search(me, "a_", 20)).Get().Select(c => c.User.UserId).Should().Equal(underscore);
    (await service.Search(me, "a%", 20)).Get().Select(c => c.User.UserId).Should().Equal(percent);
    (await service.Search(me, @"a\", 20)).Get().Select(c => c.User.UserId).Should().Equal(backslash);
    (await repo.Search(me, "_", 20)).Get().Select(c => c.User.UserId).Should().Equal(leadingUnderscore);
    (await repo.Search(me, "%", 20)).Get().Should().BeEmpty();
    (await service.Search(me, "__", 20)).Get().Should().BeEmpty();
  }

  [SkippableFact]
  public async Task Search_ValidatesQueryAndLimit()
  {
    Skip.If(_conn == null, SkipReason);
    var me = await SeedUser("alfred");
    var service = Service(new RecordingInbox());

    (await service.Search(me, " a ", 20)).FailureOrDefault().Should().BeOfType<InvalidRequestException>();
    (await service.Search(me, "al", 0)).FailureOrDefault().Should().BeOfType<InvalidRequestException>();
    (await service.Search(me, "al", 21)).FailureOrDefault().Should().BeOfType<InvalidRequestException>();
    (await service.Search(me, "al", 20)).IsSuccess().Should().BeTrue();
  }

  // One permanent code per user. Accepting it asks the owner, like a friend request: nobody is
  // friends until the owner accepts, unless the owner had already asked the person accepting.
  [SkippableFact]
  public async Task Invite_OneCodePerUser_AcceptSendsTheOwnerARequest()
  {
    Skip.If(_conn == null, SkipReason);
    var alice = await SeedUser("alice");
    var bob = await SeedUser("bob");
    var carol = await SeedUser("carol");
    var inbox = new RecordingInbox();
    var service = Service(inbox);

    var invite = (await service.GetOrCreateInvite(alice)).Get();
    invite.Code.Should().HaveLength(FriendService.InviteCodeLength).And.MatchRegex("^[23456789abcdefghjkmnpqrstuvwxyz]+$");
    (await service.GetOrCreateInvite(alice)).Get().Code.Should().Be(invite.Code);
    (await service.GetOrCreateInvite(bob)).Get().Code.Should().NotBe(invite.Code);
    (await _db.FriendInvites.CountAsync(x => x.UserId == alice)).Should().Be(1);

    (await service.AcceptInvite(alice, invite.Code)).FailureOrDefault().Should().BeOfType<InvalidRequestException>();
    (await service.AcceptInvite(bob, "nosuchcode")).Get().Should().BeNull();

    // bob uses alice's link: alice gets a request to approve, they aren't friends yet.
    (await service.AcceptInvite(bob, invite.Code)).Get().Should().Be(FriendRelation.Outgoing);
    (await service.AreFriends(alice, bob)).Get().Should().BeFalse();
    var requests = (await service.ListRequests(alice)).Get();
    requests.Incoming.Select(r => r.From).Should().Equal(new FriendUser(bob, "bob"));
    inbox.Posts.Select(p => (p.UserId, p.Record.Kind, p.Record.ActorUserId)).Should()
      .Equal((alice, InboxKinds.FriendRequest, bob));

    // Again: still just the one request and notification.
    (await service.AcceptInvite(bob, invite.Code.ToUpperInvariant())).Get().Should().Be(FriendRelation.Outgoing);
    (await _db.FriendRequests.CountAsync()).Should().Be(1);
    inbox.Posts.Should().HaveCount(1);

    // alice had already asked carol: carol using alice's link accepts that request.
    await service.SendRequest(alice, carol);
    inbox.Posts.Clear();
    (await service.AcceptInvite(carol, invite.Code)).Get().Should().Be(FriendRelation.Friend);
    (await service.AreFriends(alice, carol)).Get().Should().BeTrue();
    inbox.Posts.Select(p => (p.UserId, p.Record.Kind, p.Record.ActorUserId)).Should()
      .Equal((alice, InboxKinds.FriendAccepted, carol));
  }

  // Codes never expire: a link made long ago still works.
  [SkippableFact]
  public async Task Invite_OldCodeStillWorks()
  {
    Skip.If(_conn == null, SkipReason);
    var alice = await SeedUser("alice");
    var bob = await SeedUser("bob");
    _db.FriendInvites.Add(new FriendInviteData { Code = "oldcode234", UserId = alice, CreatedAtUtc = DateTime.UtcNow.AddYears(-2) });
    await _db.SaveChangesAsync();

    (await Service(new RecordingInbox()).GetOrCreateInvite(alice)).Get().Code.Should().Be("oldcode234");
    (await Service(new RecordingInbox()).AcceptInvite(bob, "oldcode234")).Get().Should().Be(FriendRelation.Outgoing);
  }

  // Two first calls race: the other request stores its invite between our check and our insert.
  // We get the code that was stored, never an error or a second code.
  [SkippableFact]
  public async Task Invite_LosingTheFirstCallRace_ReturnsTheStoredCode()
  {
    Skip.If(_conn == null, SkipReason);
    var alice = await SeedUser("alice");
    await using var db = PenaltyIntegrationTests.NewContext(_conn!);
    var raced = false;
    db.SavingChanges += (_, _) =>
    {
      if (raced) return;
      raced = true;
      using var other = PenaltyIntegrationTests.NewContext(_conn!);
      other.FriendInvites.Add(new FriendInviteData { Code = "winner2345", UserId = alice });
      other.SaveChanges();
    };
    var service = new FriendService(new FriendRepository(db, NullLogger<FriendRepository>.Instance),
      new RecordingInbox(), NullLogger<FriendService>.Instance);

    (await service.GetOrCreateInvite(alice)).Get().Code.Should().Be("winner2345");
    raced.Should().BeTrue();
    (await _db.FriendInvites.AsNoTracking().Where(x => x.UserId == alice).Select(x => x.Code).ToListAsync())
      .Should().Equal("winner2345");
  }

  // Before this change a user could make several invites. The migration keeps each user's
  // oldest code (the one most likely shared) and then allows only one per user.
  [SkippableFact]
  public async Task Migration_KeepsEachUsersOldestInvite_ThenOnlyOne()
  {
    Skip.If(_conn == null, SkipReason);
    await using var db = PenaltyIntegrationTests.NewContext(
      string.Join(';', _conn!.Split(';').Where(p => !p.Trim().StartsWith("Database=", StringComparison.OrdinalIgnoreCase)))
      + ";Database=friends_migrate_test");
    await db.Database.EnsureDeletedAsync();
    try
    {
      var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();
      await migrator.MigrateAsync("20261111091657_AddFriendsInboxAndFriendStake");
      var t = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
      db.Users.AddRange(new UserData { Id = "a", Username = "alice", Email = "a@test.local" },
        new UserData { Id = "b", Username = "bob", Email = "b@test.local" });
      await db.SaveChangesAsync();
      await db.Database.ExecuteSqlRawAsync("""
        INSERT INTO "FriendInvites" ("Code", "UserId", "ExpiresAtUtc", "CreatedAtUtc") VALUES
          ('alicenew22', 'a', {0}, {1}), ('aliceold22', 'a', {0}, {2}), ('alicemid22', 'a', {0}, {3}),
          ('bobonly222', 'b', {0}, {1});
        """, t.AddDays(7), t.AddDays(2), t, t.AddDays(1));

      await migrator.MigrateAsync();

      (await db.FriendInvites.AsNoTracking().OrderBy(x => x.UserId).Select(x => x.Code).ToListAsync())
        .Should().Equal("aliceold22", "bobonly222");
      db.FriendInvites.Add(new FriendInviteData { Code = "second2222", UserId = "a" });
      await FluentActions.Awaiting(() => db.SaveChangesAsync()).Should().ThrowAsync<UniqueConstraintException>();
    }
    finally
    {
      await db.Database.EnsureDeletedAsync();
    }
  }

  [SkippableFact]
  public async Task FailedNotification_DoesNotUndoRequestOrFriendship()
  {
    Skip.If(_conn == null, SkipReason);
    var alice = await SeedUser("alice");
    var bob = await SeedUser("bob");
    var carol = await SeedUser("carol");
    var failing = Service(new RecordingInbox(fail: true));
    var throwing = Service(new RecordingInbox(throws: true));

    (await failing.SendRequest(alice, bob)).Get().Should().Be(FriendRelation.Outgoing);
    (await _db.FriendRequests.CountAsync()).Should().Be(1);
    (await throwing.Accept(bob, alice)).Get().Should().NotBeNull();
    (await failing.AreFriends(alice, bob)).Get().Should().BeTrue();

    var invite = (await throwing.GetOrCreateInvite(alice)).Get();
    (await throwing.AcceptInvite(carol, invite.Code)).Get().Should().Be(FriendRelation.Outgoing);
    (await failing.ListRequests(alice)).Get().Incoming.Select(r => r.From.UserId).Should().Equal(carol);
  }
}
