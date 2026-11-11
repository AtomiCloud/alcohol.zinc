namespace App.Modules.Friend.API.V1;

public record SendFriendRequestReq(string ToUserId);

// Relation, lowercase: none | friend | outgoing (you asked them) | incoming (they asked you).
public record FriendCandidateRes(string UserId, string Username, string Relation);

public record FriendRelationRes(string Relation);

public record FriendRes(string UserId, string Username, DateTime SinceUtc);

// UserId and Username are the other person: the sender for incoming, the recipient for outgoing.
public record FriendRequestRes(string UserId, string Username, DateTime CreatedAtUtc);

public record FriendRequestsRes(List<FriendRequestRes> Incoming, List<FriendRequestRes> Outgoing);

// The app builds the link: lazytax.club/f/{Code}.
public record FriendInviteRes(string Code, DateTime ExpiresAtUtc);
