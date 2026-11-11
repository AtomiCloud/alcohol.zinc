using Domain.Notification;

namespace App.Modules.Notification.API.V1;

public static class InboxMapper
{
  public static InboxSearch ToDomain(this SearchInboxQuery query, string userId) =>
    new()
    {
      UserId = userId,
      UnreadOnly = query.UnreadOnly ?? false,
      Limit = query.Limit ?? 20,
      Skip = query.Skip ?? 0
    };

  public static InboxNotificationRes ToRes(this InboxNotificationPrincipal p) =>
    new(p.Id, p.Record.Kind, p.Record.Title, p.Record.Body, p.Record.ActorUserId, p.Record.Data,
      p.CreatedAtUtc, p.ReadAtUtc);
}
