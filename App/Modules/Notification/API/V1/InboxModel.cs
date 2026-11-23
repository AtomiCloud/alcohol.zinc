using System.ComponentModel.DataAnnotations;

namespace App.Modules.Notification.API.V1;

// Defaults: all notifications, 20 per page, from the newest.
public record SearchInboxQuery(
  bool? UnreadOnly,
  int? Limit,
  int? Skip
);

// Data is the JSON text the app uses for the tap action.
public record InboxNotificationRes(
  Guid Id,
  string Kind,
  string Title,
  string Body,
  string? ActorUserId,
  string? Data,
  DateTime CreatedAtUtc,
  DateTime? ReadAtUtc,
  InboxFriendRequestRes? FriendRequest
);

// Unlinked legacy or untrusted actor/recipient links use null ID and unavailable, never pair inference.
// Status: pending | accepted | declined | cancelled | unavailable
public record InboxFriendRequestRes(Guid? RequestId,
  [property: Required] string Status,
  DateTime? ResolvedAtUtc);

public record InboxCountRes(
  int Count
);
