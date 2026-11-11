using CSharp_Result;

namespace Domain.Notification;

// What a notification is about; the app picks its icon and tap action from this.
public static class InboxKinds
{
  public const string FriendRequest = "friend_request"; // Actor asked to be your friend
  public const string FriendAccepted = "friend_accepted"; // Actor accepted your request or invite
  public const string FriendMissed = "friend_missed"; // Actor missed a habit you watch
}

public record InboxNotificationRecord
{
  public required string Kind { get; init; }
  public required string Title { get; init; }
  public required string Body { get; init; }

  // The user who caused it (null for system notifications). Deleting their account deletes it.
  public string? ActorUserId { get; init; }

  // JSON the app uses for the tap action, e.g. {"habitId":"…","date":"2026-01-05"}.
  public string? Data { get; init; }

  // Posting the same key to the same user twice records it once (outbox retries).
  public string? DedupeKey { get; init; }
}

public record InboxNotificationPrincipal
{
  public required Guid Id { get; init; }
  public required string UserId { get; init; }
  public required DateTime CreatedAtUtc { get; init; }
  public DateTime? ReadAtUtc { get; init; }
  public required InboxNotificationRecord Record { get; init; }
}

public record InboxSearch
{
  public required string UserId { get; init; }
  public bool UnreadOnly { get; init; }
  public int Limit { get; init; } = 20;
  public int Skip { get; init; }
}

// The in-app inbox. Post is the one way any feature notifies a user, so push delivery
// (Firebase) can later hook in here without touching the callers.
public interface IInboxService
{
  // The posted notification, or the existing one when its DedupeKey was already posted.
  Task<Result<InboxNotificationPrincipal>> Post(string userId, InboxNotificationRecord record);

  // Newest first.
  Task<Result<List<InboxNotificationPrincipal>>> Search(InboxSearch search);

  Task<Result<int>> UnreadCount(string userId);

  // Null when the notification isn't the user's.
  Task<Result<Unit?>> MarkRead(string userId, Guid id);

  Task<Result<int>> MarkAllRead(string userId);
}

public interface IInboxRepository
{
  Task<Result<InboxNotificationPrincipal>> Create(string userId, InboxNotificationRecord record);
  Task<Result<List<InboxNotificationPrincipal>>> Search(InboxSearch search);
  Task<Result<int>> UnreadCount(string userId);
  Task<Result<Unit?>> MarkRead(string userId, Guid id, DateTime atUtc);
  Task<Result<int>> MarkAllRead(string userId, DateTime atUtc);
}
