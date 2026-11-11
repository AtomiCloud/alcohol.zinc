namespace App.Modules.FriendStake.API.V1;

// The friends to tell when the habit's day ends Missed; an empty list turns it off.
public record SetFriendWatchersReq(
  List<string> FriendUserIds
);

public record FriendWatcherRes(
  string UserId,
  string Username
);

public record FriendStakeRes(
  Guid HabitId,
  List<FriendWatcherRes> Friends
);
