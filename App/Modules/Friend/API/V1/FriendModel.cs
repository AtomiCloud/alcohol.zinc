using System.ComponentModel.DataAnnotations;

namespace App.Modules.Friend.API.V1;

public record SendFriendRequestReq(string ToUserId);

// Relation, lowercase: none | friend | outgoing (you asked them) | incoming (they asked you).
public record FriendCandidateRes(string UserId, string Username, string Relation);

public record FriendRelationRes(string Relation);

public record FriendRes(string UserId, string Username, DateTime SinceUtc);

// UserId and Username are the other person: the sender for incoming, the recipient for outgoing.
public record FriendRequestRes(Guid RequestId, string UserId, string Username, DateTime CreatedAtUtc);

public record ResolveFriendRequestReq
{
  // accept | decline | cancel
  [Required] public required string Decision { get; init; }
}

// Status: pending | accepted | declined | cancelled
public record FriendRequestStateRes(Guid RequestId,
  [property: Required] string Status,
  DateTime CreatedAtUtc, DateTime? ResolvedAtUtc);

public record FriendRequestsRes(List<FriendRequestRes> Incoming, List<FriendRequestRes> Outgoing);

// The app builds the link: lazytax.club/f/{Code}. It never expires.
public record FriendInviteRes(string Code);
