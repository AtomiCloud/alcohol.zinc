using App.Modules.Notification.Data;
using App.Modules.Users.Data;
using App.StartUp.Database;
using CSharp_Result;
using Domain.Exceptions;
using Domain.Notification;
using IntTest.Penalty;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace IntTest.Notification;

// The in-app inbox against a real Postgres (PENALTY_TEST_DB): the real InboxService over the
// real InboxRepository, so the unique dedupe index and the user cascades are the database's.
[Collection(DbIntegrationCollection.Name)]
public class InboxIntegrationTests : IAsyncLifetime
{
  private const string SkipReason = "PENALTY_TEST_DB not set; skipping DB-backed inbox tests.";
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

  private static DateTime U(int d, int h) => new(2026, 1, d, h, 0, 0, DateTimeKind.Utc);

  private InboxService Service() => new(new InboxRepository(_db, NullLogger<InboxRepository>.Instance));

  private async Task<string> SeedUser()
  {
    var userId = $"user-{Guid.NewGuid():N}";
    _db.Users.Add(new UserData { Id = userId, Username = userId, Email = $"{userId}@test.local" });
    await _db.SaveChangesAsync();
    return userId;
  }

  private static InboxNotificationRecord Note(string title, string? actor = null, string? dedupeKey = null) => new()
  {
    Kind = InboxKinds.FriendMissed,
    Title = title,
    Body = $"{title} body",
    ActorUserId = actor,
    Data = """{"habitId":"h1"}""",
    DedupeKey = dedupeKey
  };

  // Rows with fixed creation times, to test ordering independent of insertion order.
  private async Task<Guid> SeedAt(string userId, string title, DateTime createdAtUtc, DateTime? readAtUtc = null)
  {
    var data = Note(title).ToData(userId);
    data.CreatedAtUtc = createdAtUtc;
    data.ReadAtUtc = readAtUtc;
    _db.InboxNotifications.Add(data);
    await _db.SaveChangesAsync();
    return data.Id;
  }

  private async Task<List<string>> Titles(string userId, bool unreadOnly = false, int limit = 20, int skip = 0) =>
    (await Service().Search(new InboxSearch { UserId = userId, UnreadOnly = unreadOnly, Limit = limit, Skip = skip }))
    .Get().Select(n => n.Record.Title).ToList();

  [SkippableFact]
  public async Task Post_SameDedupeKeyTwice_RecordsOnceAndReturnsTheFirst()
  {
    Skip.If(_conn == null, SkipReason);
    var userId = await SeedUser();
    var actor = await SeedUser();
    var otherUser = await SeedUser();
    var inbox = Service();

    var first = (await inbox.Post(userId, Note("jimmy wants to be friends", actor, "friend-request:1"))).Get();
    var retry = (await inbox.Post(userId, Note("a changed title", actor, "friend-request:1"))).Get();
    // The same key means nothing for another recipient, and keyless posts never dedupe.
    var othersCopy = (await inbox.Post(otherUser, Note("jimmy wants to be friends", actor, "friend-request:1"))).Get();
    var keyless1 = (await inbox.Post(userId, Note("system"))).Get();
    var keyless2 = (await inbox.Post(userId, Note("system"))).Get();

    retry.Id.Should().Be(first.Id);
    retry.Record.Title.Should().Be("jimmy wants to be friends", "the first post is kept, not overwritten");
    retry.CreatedAtUtc.Should().Be(first.CreatedAtUtc);
    first.UserId.Should().Be(userId);
    first.Record.Should().Be(Note("jimmy wants to be friends", actor, "friend-request:1"));
    othersCopy.Id.Should().NotBe(first.Id);
    keyless1.Id.Should().NotBe(keyless2.Id);
    await using var verify = PenaltyIntegrationTests.NewContext(_conn!);
    (await verify.InboxNotifications.CountAsync(x => x.UserId == userId)).Should().Be(3);
    (await verify.InboxNotifications.CountAsync(x => x.UserId == otherUser)).Should().Be(1);
  }

  [SkippableFact]
  public async Task Search_NewestFirst_PagesWithoutOverlap_AndOnlyTheUsersOwn()
  {
    Skip.If(_conn == null, SkipReason);
    var userId = await SeedUser();
    var otherUser = await SeedUser();
    // Inserted out of time order, with another user's rows in between.
    await SeedAt(userId, "c", U(3, 9));
    await SeedAt(userId, "a", U(1, 9));
    await SeedAt(otherUser, "x", U(4, 9));
    await SeedAt(userId, "e", U(5, 9));
    await SeedAt(userId, "b", U(2, 9));
    await SeedAt(userId, "d", U(4, 8));

    (await Titles(userId)).Should().Equal("e", "d", "c", "b", "a");
    (await Titles(userId, limit: 2)).Should().Equal("e", "d");
    (await Titles(userId, limit: 2, skip: 2)).Should().Equal("c", "b");
    (await Titles(userId, limit: 2, skip: 4)).Should().Equal("a");
    (await Titles(userId, limit: 2, skip: 6)).Should().BeEmpty();
  }

  [SkippableFact]
  public async Task UnreadOnlyAndCount_ExcludeReadAndOtherUsers_AndMarkAllReadCountsWhatItRead()
  {
    Skip.If(_conn == null, SkipReason);
    var userId = await SeedUser();
    var otherUser = await SeedUser();
    var readAt = U(3, 12);
    await SeedAt(userId, "old-unread", U(1, 9));
    await SeedAt(userId, "read", U(2, 9), readAt);
    var newest = await SeedAt(userId, "new-unread", U(3, 9));
    await SeedAt(userId, "newest-unread", U(4, 9));
    await SeedAt(otherUser, "theirs", U(2, 10));
    var inbox = Service();

    (await Titles(userId, unreadOnly: true)).Should().Equal("newest-unread", "new-unread", "old-unread");
    (await Titles(userId, unreadOnly: true, limit: 1, skip: 1)).Should().Equal("new-unread");
    (await Titles(userId)).Should().HaveCount(4);
    (await inbox.UnreadCount(userId)).Get().Should().Be(3);

    (await inbox.MarkRead(userId, newest)).Get().Should().NotBeNull();
    (await inbox.UnreadCount(userId)).Get().Should().Be(2);
    (await Titles(userId, unreadOnly: true)).Should().Equal("newest-unread", "old-unread");

    (await inbox.MarkAllRead(userId)).Get().Should().Be(2);
    (await inbox.UnreadCount(userId)).Get().Should().Be(0);
    (await inbox.MarkAllRead(userId)).Get().Should().Be(0);
    (await inbox.UnreadCount(otherUser)).Get().Should().Be(1, "another user's inbox is untouched");
    await using var verify = PenaltyIntegrationTests.NewContext(_conn!);
    (await verify.InboxNotifications.SingleAsync(x => x.Title == "read")).ReadAtUtc.Should()
      .Be(readAt, "marking all read keeps the first read time");
  }

  [SkippableFact]
  public async Task MarkRead_OnlyTheOwnersNotification_AndKeepsTheFirstReadTime()
  {
    Skip.If(_conn == null, SkipReason);
    var owner = await SeedUser();
    var stranger = await SeedUser();
    var id = (await Service().Post(owner, Note("jimmy missed Run today"))).Get().Id;

    (await Service().MarkRead(stranger, id)).Get().Should().BeNull();
    (await Service().MarkRead(owner, Guid.NewGuid())).Get().Should().BeNull();
    await using (var verify = PenaltyIntegrationTests.NewContext(_conn!))
      (await verify.InboxNotifications.SingleAsync(x => x.Id == id)).ReadAtUtc.Should().BeNull();

    (await Service().MarkRead(owner, id)).Get().Should().NotBeNull();
    DateTime? firstRead;
    await using (var verify = PenaltyIntegrationTests.NewContext(_conn!))
      firstRead = (await verify.InboxNotifications.SingleAsync(x => x.Id == id)).ReadAtUtc;
    firstRead.Should().NotBeNull();

    await Task.Delay(20);
    (await Service().MarkRead(owner, id)).Get().Should().NotBeNull("reading again is still the owner's success");
    await using (var verify = PenaltyIntegrationTests.NewContext(_conn!))
      (await verify.InboxNotifications.SingleAsync(x => x.Id == id)).ReadAtUtc.Should().Be(firstRead);
  }

  [SkippableFact]
  public async Task DeletingAUser_CascadesTheNotificationsTheyCausedAndReceived()
  {
    Skip.If(_conn == null, SkipReason);
    var recipient = await SeedUser();
    var jimmy = await SeedUser();
    var sam = await SeedUser();
    var inbox = Service();
    await inbox.Post(recipient, Note("jimmy wants to be friends", jimmy));
    await inbox.Post(recipient, Note("jimmy missed Run today", jimmy, "missed:1"));
    await inbox.Post(recipient, Note("sam missed Run today", sam));
    await inbox.Post(recipient, Note("system"));
    await inbox.Post(jimmy, Note("for jimmy", sam));

    // Delete in the database (not EF's tracked cascade) so the FK's ON DELETE is what's tested.
    await _db.Users.Where(u => u.Id == jimmy).ExecuteDeleteAsync();

    _db.ChangeTracker.Clear();
    (await Titles(recipient)).Should().BeEquivalentTo("sam missed Run today", "system");
    (await inbox.UnreadCount(recipient)).Get().Should().Be(2);
    (await _db.InboxNotifications.AnyAsync(x => x.UserId == jimmy || x.ActorUserId == jimmy)).Should().BeFalse();
    (await _db.InboxNotifications.CountAsync(x => x.ActorUserId == sam)).Should().Be(1);
  }

  [SkippableFact]
  public async Task InvalidPagingOrText_Is400_AndPostsNothing()
  {
    Skip.If(_conn == null, SkipReason);
    var userId = await SeedUser();
    var inbox = Service();

    foreach (var (limit, skip) in new[] { (0, 0), (51, 0), (20, -1) })
      (await inbox.Search(new InboxSearch { UserId = userId, Limit = limit, Skip = skip })).FailureOrDefault()
        .Should().BeOfType<InvalidRequestException>($"limit {limit} skip {skip}");
    (await inbox.Search(new InboxSearch { UserId = userId, Limit = 50 })).IsSuccess().Should().BeTrue();

    var blankTitle = await inbox.Post(userId, Note(" "));
    var longBody = await inbox.Post(userId, Note("t") with { Body = new string('b', 1025) });
    var longTitle = await inbox.Post(userId, Note(new string('t', 257)));
    var atLimits = await inbox.Post(userId, Note(new string('t', 256)) with { Body = new string('b', 1024) });

    ((InvalidRequestException)blankTitle.FailureOrDefault()).Errors.Keys.Should().Equal("title");
    ((InvalidRequestException)longBody.FailureOrDefault()).Errors.Keys.Should().Equal("body");
    ((InvalidRequestException)longTitle.FailureOrDefault()).Errors.Keys.Should().Equal("title");
    atLimits.IsSuccess().Should().BeTrue();
    (await _db.InboxNotifications.CountAsync(x => x.UserId == userId)).Should().Be(1);
  }
}
