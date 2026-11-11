using Domain.Notification;

namespace App.Modules.Notification.Data;

public static class InboxMapper
{
  public static InboxNotificationData ToData(this InboxNotificationRecord record, string userId)
  {
    return new InboxNotificationData
    {
      UserId = userId,
      Kind = record.Kind,
      Title = record.Title,
      Body = record.Body,
      ActorUserId = record.ActorUserId,
      Data = record.Data,
      DedupeKey = record.DedupeKey
    };
  }

  public static InboxNotificationRecord ToRecord(this InboxNotificationData data)
  {
    return new InboxNotificationRecord
    {
      Kind = data.Kind,
      Title = data.Title,
      Body = data.Body,
      ActorUserId = data.ActorUserId,
      Data = data.Data,
      DedupeKey = data.DedupeKey
    };
  }

  public static InboxNotificationPrincipal ToPrincipal(this InboxNotificationData data)
  {
    return new InboxNotificationPrincipal
    {
      Id = data.Id,
      UserId = data.UserId,
      CreatedAtUtc = data.CreatedAtUtc,
      ReadAtUtc = data.ReadAtUtc,
      Record = data.ToRecord()
    };
  }
}
