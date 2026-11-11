using Domain.Friend;

namespace App.Modules.Friend.Data;

public static class FriendMapper
{
  public static FriendInvitePrincipal ToPrincipal(this FriendInviteData data) =>
    new(data.Code, data.UserId, data.ExpiresAtUtc);
}
