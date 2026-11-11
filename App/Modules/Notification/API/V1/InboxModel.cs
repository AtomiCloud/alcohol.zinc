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
  DateTime? ReadAtUtc
);

public record InboxCountRes(
  int Count
);
