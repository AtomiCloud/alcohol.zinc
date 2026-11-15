using System.Data.Common;
using System.Security.Claims;
using App.Error.V1;
using App.Modules.Friend.API.V1;
using App.Modules.Friend.Data;
using App.Modules.FriendStake.Data;
using App.Modules.Habit.Data;
using App.Modules.Notification.Data;
using App.Modules.Users.Data;
using App.StartUp.Database;
using CSharp_Result;
using Domain.Exceptions;
using Domain.Friend;
using Domain.Notification;
using EntityFramework.Exceptions.Common;
using IntTest.Penalty;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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

  private static FriendService Service(MainDbContext db) => new(
    new FriendRepository(db, NullLogger<FriendRepository>.Instance),
    new InboxService(new InboxRepository(db, NullLogger<InboxRepository>.Instance)),
    NullLogger<FriendService>.Instance);

  private static FriendController Controller(IFriendService service, string caller) => new(service, null!, new())
  {
    ControllerContext = new ControllerContext
    {
      HttpContext = new DefaultHttpContext
      {
        User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, caller)], "test"))
      }
    }
  };

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

  [SkippableTheory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task PairOnlyMutation_RequiresOccurrence_WithoutWrites(bool accept)
  {
    Skip.If(_conn == null, SkipReason);
    var alice = await SeedUser("alice");
    var bob = await SeedUser("bob");
    var service = Service(new RecordingInbox());
    await service.SendRequest(alice, bob);
    var id = await _db.FriendRequests.Select(x => x.Id).SingleAsync();

    var failed = accept
      ? (await service.Accept(bob, alice)).IsFailure()
      : (await service.DeleteRequest(bob, alice)).IsFailure();

    failed.Should().BeTrue("pair-only callers must update rather than resolve an unidentified occurrence");
    (await _db.FriendRequests.AsNoTracking().Select(x => x.Id).SingleAsync()).Should().Be(id);
    (await _db.Friendships.CountAsync()).Should().Be(0);
  }

  [SkippableFact]
  public async Task DeclineResendAccept_FreshInboxKeepsBothOutcomes_StaleIdCannotConsumeNewRequest()
  {
    Skip.If(_conn == null, SkipReason);
    var alice = await SeedUser("alice");
    var bob = await SeedUser("bob");
    var service = Service(_db);
    await service.SendRequest(alice, bob);
    var first = (await service.ListRequests(bob)).Get().Incoming.Single();
    var declined = (await service.ResolveRequest(bob, first.Id, "decline")).Get();
    await service.SendRequest(alice, bob);
    var second = (await service.ListRequests(bob)).Get().Incoming.Single();
    second.Id.Should().NotBe(first.Id);

    (await service.ResolveRequest(bob, first.Id, "accept")).FailureOrDefault()
      .Should().BeOfType<FriendRequestException>().Which.Error.Should().Be(FriendRequestError.Resolved);
    (await service.ResolveRequest(bob, Guid.NewGuid(), "accept")).FailureOrDefault().Should().BeOfType<NotFoundException>();
    (await service.GetRequest(bob, first.Id)).Get().Should().Be(declined);
    (await service.ListRequests(bob)).Get().Incoming.Single().Id.Should().Be(second.Id);
    (await service.AreFriends(alice, bob)).Get().Should().BeFalse();

    var accepted = (await service.ResolveRequest(bob, second.Id, "accept")).Get();
    accepted.ResolvedAtUtc.Should().NotBeNull();
    await service.Remove(alice, bob);
    (await service.ResolveRequest(bob, second.Id, "accept")).Get().Should().Be(accepted);
    (await service.AreFriends(alice, bob)).Get().Should().BeFalse("a retry must not recreate an ended friendship");

    await using var fresh = PenaltyIntegrationTests.NewContext(_conn!);
    var inbox = new InboxService(new InboxRepository(fresh, NullLogger<InboxRepository>.Instance));
    var page = (await inbox.Search(new InboxSearch { UserId = bob })).Get();
    page.Select(x => (x.FriendRequest!.RequestId, x.FriendRequest.Status)).Should()
      .BeEquivalentTo([(first.Id, "declined"), (second.Id, "accepted")]);
    page.Should().OnlyContain(x => x.Record.Kind == InboxKinds.FriendRequestResolved && x.ReadAtUtc == null);
    page.Select(x => x.CreatedAtUtc).Should().BeInDescendingOrder();
    var rows = await fresh.FriendRequests.AsNoTracking().OrderBy(x => x.CreatedAtUtc).ToListAsync();
    rows.Select(x => (x.Id, x.Status)).Should().Equal(
      (first.Id, FriendRequestStatus.Declined), (second.Id, FriendRequestStatus.Accepted));
    (await fresh.InboxNotifications.AsNoTracking().Where(x => x.UserId == bob).ToListAsync())
      .Should().OnlyContain(x => x.Kind == InboxKinds.FriendRequest && x.ReadAtUtc == null);
    (await fresh.InboxNotifications.CountAsync(x => x.UserId == alice && x.Kind == InboxKinds.FriendAccepted))
      .Should().Be(1);
  }

  [SkippableTheory]
  [InlineData("sender", "accept", 403, typeof(FriendRequestWrongRole))]
  [InlineData("sender", "decline", 403, typeof(FriendRequestWrongRole))]
  [InlineData("recipient", "cancel", 403, typeof(FriendRequestWrongRole))]
  [InlineData("outsider", "accept", 404, typeof(EntityNotFound))]
  public async Task Resolve_PublicProblemMatrix_WritesNothing(string actor, string decision, int status, Type problem)
  {
    Skip.If(_conn == null, SkipReason);
    var alice = await SeedUser("alice");
    var bob = await SeedUser("bob");
    var outsider = await SeedUser("outsider");
    var service = Service(_db);
    await service.SendRequest(alice, bob);
    var id = (await service.ListRequests(bob)).Get().Incoming.Single().Id;
    var caller = actor == "sender" ? alice : actor == "recipient" ? bob : outsider;
    var controller = Controller(service, caller);
    var response = await controller.ResolveRequest(caller, id, new ResolveFriendRequestReq { Decision = decision });
    ((StatusCodeResult)response.Result!).StatusCode.Should().Be(status);
    controller.HttpContext.Items["DomainProblemContext"].Should().BeOfType(problem);
    (await service.GetRequest(bob, id)).Get().Status.Should().Be(FriendRequestStatus.Pending);
    (await _db.Friendships.CountAsync()).Should().Be(0);
  }

  [SkippableTheory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task LegacyController_ReturnsDefinedUpdateRequired409_AndHonorsGuard(bool accept)
  {
    Skip.If(_conn == null, SkipReason);
    var alice = await SeedUser("alice");
    var bob = await SeedUser("bob");
    var service = Service(_db);
    await service.SendRequest(alice, bob);
    var controller = Controller(service, bob);
    var response = accept ? (await controller.Accept(bob, alice)).Result! : await controller.DeleteRequest(bob, alice);
    ((StatusCodeResult)response).StatusCode.Should().Be(409);
    var problem = (FriendRequestIdRequired)controller.HttpContext.Items["DomainProblemContext"]!;
    problem.Id.Should().Be("friend_request_id_required");
    problem.Detail.Should().Be("Update LazyTax to respond to this friend request.");
    var forbidden = accept ? (await controller.Accept(alice, bob)).Result! : await controller.DeleteRequest(alice, bob);
    ((StatusCodeResult)forbidden).StatusCode.Should().Be(403);
    (await service.ListRequests(bob)).Get().Incoming.Should().HaveCount(1);
    (await _db.Friendships.CountAsync()).Should().Be(0);
  }

  // Hold the first transactional occurrence read until both readers have their snapshots.
  // Correct pair locks prevent the second read until the first commits, so the bounded wait
  // opens for that case. Without serialization both decisions see Pending and both succeed.
  private sealed class OccurrenceGate : DbCommandInterceptor
  {
    private readonly TaskCompletionSource open = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly HashSet<Guid> seen = [];

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
      CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
      if (command.CommandText.Contains("FROM \"FriendRequests\"") && eventData.Context?.Database.CurrentTransaction != null)
      {
        bool first;
        lock (seen)
        {
          first = seen.Add(eventData.Context.ContextId.InstanceId);
          if (seen.Count == 2) open.TrySetResult();
        }
        if (first)
        {
          await Task.WhenAny(open.Task, Task.Delay(TimeSpan.FromSeconds(1.5), cancellationToken));
          open.TrySetResult();
        }
      }
      return result;
    }
  }

  [SkippableFact]
  public async Task ConcurrentAcceptDecline_OneRetainedWinner_OneConflict()
  {
    Skip.If(_conn == null, SkipReason);
    var alice = await SeedUser("alice");
    var bob = await SeedUser("bob");
    var service = Service(_db);
    await service.SendRequest(alice, bob);
    var id = (await service.ListRequests(bob)).Get().Incoming.Single().Id;
    var gate = new OccurrenceGate();
    var results = await Task.WhenAll(new[] { "accept", "decline" }.Select(async decision =>
    {
      await using var db = PenaltyIntegrationTests.NewContext(_conn!, gate);
      return await Service(db).ResolveRequest(bob, id, decision);
    }));
    var winner = results.Single(x => x.IsSuccess()).Get();
    results.Single(x => x.IsFailure()).FailureOrDefault().Should().BeOfType<FriendRequestException>()
      .Which.Error.Should().Be(FriendRequestError.Resolved);
    await using var fresh = PenaltyIntegrationTests.NewContext(_conn!);
    (await Service(fresh).GetRequest(bob, id)).Get().Should().Be(winner);
    (await fresh.FriendRequests.CountAsync()).Should().Be(1);
    (await fresh.Friendships.CountAsync()).Should().Be(winner.Status == FriendRequestStatus.Accepted ? 2 : 0);
    (await fresh.InboxNotifications.CountAsync(x => x.Kind == InboxKinds.FriendAccepted))
      .Should().Be(winner.Status == FriendRequestStatus.Accepted ? 1 : 0);
  }

  [SkippableTheory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task OppositeSend_RacingSendOrAccept_OneAcceptedOccurrenceAndNotice(bool explicitAccept)
  {
    Skip.If(_conn == null, SkipReason);
    var alice = await SeedUser("alice");
    var bob = await SeedUser("bob");
    Guid id = default;
    if (explicitAccept)
    {
      await Service(_db).SendRequest(alice, bob);
      id = (await Service(_db).ListRequests(bob)).Get().Incoming.Single().Id;
    }
    var gate = new OccurrenceGate();
    await Task.WhenAll(Enumerable.Range(0, 2).Select(async i =>
    {
      await using var db = PenaltyIntegrationTests.NewContext(_conn!, gate);
      if (explicitAccept && i == 0) (await Service(db).ResolveRequest(bob, id, "accept")).Get();
      else (await Service(db).SendRequest(i == 0 ? alice : bob, i == 0 ? bob : alice)).Get();
    }));
    await using var fresh = PenaltyIntegrationTests.NewContext(_conn!);
    (await fresh.FriendRequests.Select(x => x.Status).SingleAsync()).Should().Be(FriendRequestStatus.Accepted);
    (await fresh.Friendships.CountAsync()).Should().Be(2);
    (await fresh.InboxNotifications.CountAsync(x => x.Kind == InboxKinds.FriendAccepted)).Should().Be(1);
    var notes = (await new InboxService(new InboxRepository(fresh, NullLogger<InboxRepository>.Instance))
      .Search(new InboxSearch { UserId = bob })).Get();
    notes.Where(x => x.FriendRequest != null).Should().OnlyContain(x => x.FriendRequest!.Status == "accepted");
  }

  [SkippableTheory]
  [InlineData(false, false)]
  [InlineData(false, true)]
  [InlineData(true, false)]
  [InlineData(true, true)]
  public async Task MigratedOppositePending_NewAcceptanceReconcilesOnlyNow_RetryLeavesResendAlone(
    bool otherDirection, bool explicitAccept)
  {
    Skip.If(_conn == null, SkipReason);
    await using var db = PenaltyIntegrationTests.NewContext(
      string.Join(';', _conn!.Split(';').Where(p => !p.Trim().StartsWith("Database=", StringComparison.OrdinalIgnoreCase)))
      + ";Database=friends_occurrence_migrate_test");
    await db.Database.EnsureDeletedAsync();
    try
    {
      var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();
      await migrator.MigrateAsync("20261113065122_HabitSortOrder");
      var actor = otherDirection ? "z" : "A";
      var other = otherDirection ? "A" : "z";
      db.Users.AddRange(new UserData { Id = actor, Username = actor, Email = "a@test.local" },
        new UserData { Id = other, Username = other, Email = "b@test.local" });
      await db.SaveChangesAsync();
      var incoming = Guid.NewGuid();
      var outgoing = Guid.NewGuid();
      var created = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
      await db.Database.ExecuteSqlAsync($"""
        INSERT INTO "FriendRequests" ("Id", "FromUserId", "ToUserId", "CreatedAtUtc") VALUES
          ({incoming}, {other}, {actor}, {created}), ({outgoing}, {actor}, {other}, {created});
        """);
      await migrator.MigrateAsync();
      var migrated = await db.FriendRequests.AsNoTracking().ToListAsync();
      migrated.Select(x => x.Id).Should().BeEquivalentTo([incoming, outgoing]);
      migrated.Should().OnlyContain(x => x.Status == FriendRequestStatus.Pending && x.ResolvedAtUtc == null && x.CreatedAtUtc == created);
      var before = DateTime.UtcNow;
      var service = Service(db);
      if (explicitAccept) (await service.ResolveRequest(actor, incoming, "accept")).Get();
      else (await service.SendRequest(actor, other)).Get().Should().Be(FriendRelation.Friend);
      var accepted = (await service.GetRequest(actor, incoming)).Get();
      var cancelled = (await service.GetRequest(actor, outgoing)).Get();
      accepted.Status.Should().Be(FriendRequestStatus.Accepted);
      cancelled.Status.Should().Be(FriendRequestStatus.Cancelled);
      accepted.ResolvedAtUtc.Should().BeOnOrAfter(before.AddTicks(-10)).And.BeOnOrBefore(DateTime.UtcNow);
      cancelled.ResolvedAtUtc.Should().Be(accepted.ResolvedAtUtc);
      (await db.Friendships.CountAsync()).Should().Be(2);
      (await service.ResolveRequest(other, outgoing, "accept")).FailureOrDefault()
        .Should().BeOfType<FriendRequestException>().Which.Error.Should().Be(FriendRequestError.Resolved);
      await service.Remove(actor, other);
      await service.SendRequest(actor, other);
      var resend = (await service.ListRequests(actor)).Get().Outgoing.Single();
      (await service.ResolveRequest(actor, incoming, "accept")).Get().Should().Be(accepted);
      (await service.GetRequest(actor, resend.Id)).Get().Status.Should().Be(FriendRequestStatus.Pending);
      (await db.Friendships.CountAsync()).Should().Be(0);
      (await db.InboxNotifications.CountAsync(x => x.Kind == InboxKinds.FriendAccepted)).Should().Be(1);
    }
    finally
    {
      await db.Database.EnsureDeletedAsync();
    }
  }

  [SkippableFact]
  public async Task LegacyOppositePending_ConcurrentAccepts_OnlyFirstAcceptsAndReconciles()
  {
    Skip.If(_conn == null, SkipReason);
    var alice = await SeedUser("alice");
    var bob = await SeedUser("bob");
    var aToB = new FriendRequestData { FromUserId = alice, ToUserId = bob };
    var bToA = new FriendRequestData { FromUserId = bob, ToUserId = alice };
    _db.FriendRequests.AddRange(aToB, bToA);
    await _db.SaveChangesAsync();
    var gate = new OccurrenceGate();
    var results = await Task.WhenAll(new[] { (bob, aToB.Id), (alice, bToA.Id) }.Select(async pair =>
    {
      await using var db = PenaltyIntegrationTests.NewContext(_conn!, gate);
      return await Service(db).ResolveRequest(pair.Item1, pair.Id, "accept");
    }));
    results.Count(x => x.IsSuccess()).Should().Be(1);
    results.Single(x => x.IsFailure()).FailureOrDefault().Should().BeOfType<FriendRequestException>()
      .Which.Error.Should().Be(FriendRequestError.Resolved);
    var rows = await _db.FriendRequests.AsNoTracking().ToListAsync();
    rows.Select(x => x.Status).Should().BeEquivalentTo([FriendRequestStatus.Accepted, FriendRequestStatus.Cancelled]);
    rows.Select(x => x.ResolvedAtUtc).Distinct().Should().HaveCount(1);
    (await _db.Friendships.CountAsync()).Should().Be(2);
    (await _db.InboxNotifications.CountAsync(x => x.Kind == InboxKinds.FriendAccepted)).Should().Be(1);
  }

  [SkippableTheory]
  [InlineData("decline")]
  [InlineData("cancel")]
  public async Task OppositePending_DeclineOrCancel_ResolvesOnlyAddressedOccurrence(string decision)
  {
    Skip.If(_conn == null, SkipReason);
    var alice = await SeedUser("alice");
    var bob = await SeedUser("bob");
    var aToB = new FriendRequestData { FromUserId = alice, ToUserId = bob };
    var bToA = new FriendRequestData { FromUserId = bob, ToUserId = alice };
    _db.FriendRequests.AddRange(aToB, bToA);
    await _db.SaveChangesAsync();
    var service = Service(_db);
    (await service.ResolveRequest(decision == "decline" ? bob : alice, aToB.Id, decision)).Get();
    (await service.GetRequest(alice, bToA.Id)).Get().Status.Should().Be(FriendRequestStatus.Pending);
    (await _db.Friendships.CountAsync()).Should().Be(0);
  }

  [SkippableFact]
  public async Task RequestThenAccept_BothAreFriends_OutcomeRetained_OneNotificationEachWay()
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

    var requestId = bobRequests.Incoming.Single().Id;
    var acceptedState = (await service.ResolveRequest(bob, requestId, "accept")).Get();

    acceptedState.Status.Should().Be(FriendRequestStatus.Accepted);
    (await service.ListFriends(alice)).Get().Select(f => f.Friend.Username).Should().Equal("bob");
    (await service.ListFriends(bob)).Get().Select(f => f.Friend.Username).Should().Equal("alice");
    (await FriendshipRows()).Should().BeEquivalentTo([(alice, bob), (bob, alice)]);
    (await _db.FriendRequests.CountAsync()).Should().Be(1);
    (await service.ListRequests(bob)).Get().Incoming.Should().BeEmpty();
    (await service.AreFriends(bob, alice)).Get().Should().BeTrue();

    inbox.Posts.Should().HaveCount(2);
    var request = inbox.Posts[0];
    request.UserId.Should().Be(bob);
    request.Record.Kind.Should().Be(InboxKinds.FriendRequest);
    request.Record.Body.Should().Be("alice wants to be friends");
    request.Record.ActorUserId.Should().Be(alice);
    request.Record.Data.Should().Be($$"""{"requestId":"{{requestId}}","fromUserId":"{{alice}}"}""");
    request.Record.FriendRequestId.Should().Be(requestId);
    request.Record.DedupeKey.Should().Be($"friend_request:{requestId}");
    var accepted = inbox.Posts[1];
    accepted.UserId.Should().Be(alice);
    accepted.Record.Kind.Should().Be(InboxKinds.FriendAccepted);
    accepted.Record.ActorUserId.Should().Be(bob);

    (await service.ResolveRequest(bob, requestId, "accept")).Get().Should().Be(acceptedState);
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
    (await _db.FriendRequests.Select(x => x.Status).SingleAsync()).Should().Be(FriendRequestStatus.Accepted);
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
    await service.ResolveRequest(carol, (await service.ListRequests(carol)).Get().Incoming.Single().Id, "accept");
    var before = inbox.Posts.Count;
    (await service.SendRequest(alice, carol)).Get().Should().Be(FriendRelation.Friend);
    (await service.SendRequest(carol, alice)).Get().Should().Be(FriendRelation.Friend);
    inbox.Posts.Should().HaveCount(before);
    (await service.ListRequests(carol)).Get().Incoming.Should().BeEmpty();
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
  public async Task DeclineAndCancel_RetainOutcomesSilently()
  {
    Skip.If(_conn == null, SkipReason);
    var alice = await SeedUser("alice");
    var bob = await SeedUser("bob");
    var carol = await SeedUser("carol");
    var inbox = new RecordingInbox();
    var service = Service(inbox);
    await service.SendRequest(alice, bob);
    await service.SendRequest(alice, carol);

    var bobId = (await service.ListRequests(bob)).Get().Incoming.Single().Id;
    var carolId = (await service.ListRequests(carol)).Get().Incoming.Single().Id;
    (await service.ResolveRequest(bob, bobId, "decline")).Get().Status.Should().Be(FriendRequestStatus.Declined);
    (await service.ResolveRequest(alice, carolId, "cancel")).Get().Status.Should().Be(FriendRequestStatus.Cancelled);
    (await service.ResolveRequest(alice, carolId, "cancel")).Get().Status.Should().Be(FriendRequestStatus.Cancelled);

    (await _db.FriendRequests.CountAsync()).Should().Be(2);
    (await _db.Friendships.CountAsync()).Should().Be(0);
    (await service.ResolveRequest(bob, bobId, "accept")).FailureOrDefault().Should().BeOfType<FriendRequestException>();
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
    await service.ResolveRequest(bob, (await service.ListRequests(bob)).Get().Incoming.Single().Id, "accept");
    await service.SendRequest(alice, carol);
    await service.ResolveRequest(carol, (await service.ListRequests(carol)).Get().Incoming.Single().Id, "accept");

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
    await service.ResolveRequest(friend, (await service.ListRequests(friend)).Get().Incoming.Single().Id, "accept");
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
    (await throwing.ResolveRequest(bob, (await failing.ListRequests(bob)).Get().Incoming.Single().Id, "accept"))
      .Get().Status.Should().Be(FriendRequestStatus.Accepted);
    (await failing.AreFriends(alice, bob)).Get().Should().BeTrue();

    var invite = (await throwing.GetOrCreateInvite(alice)).Get();
    (await throwing.AcceptInvite(carol, invite.Code)).Get().Should().Be(FriendRelation.Outgoing);
    (await failing.ListRequests(alice)).Get().Incoming.Select(r => r.From.UserId).Should().Equal(carol);
  }
}
