using Domain.Friend;

namespace App.Modules.FriendStake.API.V1;

public static class FriendStakeMapper
{
  public static FriendStakeRes ToRes(this List<FriendUser> watchers, Guid habitId) =>
    new(habitId, watchers.Select(w => new FriendWatcherRes(w.UserId, w.Username)).ToList());
}
