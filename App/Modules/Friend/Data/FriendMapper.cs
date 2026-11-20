using Domain.Friend;

namespace App.Modules.Friend.Data;

public static class FriendMapper
{
  public static FriendRequestPrincipal ToPrincipal(this FriendRequestData data) =>
    new(data.Id, new FriendUser(data.FromUserId, data.From!.Username),
      new FriendUser(data.ToUserId, data.To!.Username), data.CreatedAtUtc, data.Status, data.ResolvedAtUtc);

  public static FriendInvitePrincipal ToPrincipal(this FriendInviteData data) =>
    new(data.Code, data.UserId);
}
