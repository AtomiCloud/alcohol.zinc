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
      DedupeKey = record.DedupeKey,
      FriendRequestId = record.FriendRequestId
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
      DedupeKey = data.DedupeKey,
      FriendRequestId = data.FriendRequestId
    };
  }

  public static InboxNotificationPrincipal ToPrincipal(this InboxNotificationData data)
  {
    InboxFriendRequest? request = null;
    if (data.Kind is InboxKinds.FriendRequest or InboxKinds.FriendRequestResolved)
    {
      var linked = data.FriendRequest;
      request = linked != null && linked.FromUserId == data.ActorUserId && linked.ToUserId == data.UserId
        ? new InboxFriendRequest(linked.Id, linked.Status.ToString().ToLowerInvariant(), linked.ResolvedAtUtc)
        : new InboxFriendRequest(null, "unavailable", null);
    }
    return new InboxNotificationPrincipal
    {
      Id = data.Id,
      UserId = data.UserId,
      CreatedAtUtc = data.CreatedAtUtc,
      ReadAtUtc = data.ReadAtUtc,
      Record = data.ToRecord() with
      {
        Kind = request == null ? data.Kind
          : request.Status == "pending" ? InboxKinds.FriendRequest : InboxKinds.FriendRequestResolved
      },
      FriendRequest = request
    };
  }
}
