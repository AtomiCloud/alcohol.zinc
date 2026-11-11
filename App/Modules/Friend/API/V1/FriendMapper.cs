using Domain.Friend;

namespace App.Modules.Friend.API.V1;

public static class FriendMapper
{
  public static string ToRes(this FriendRelation r) => r.ToString().ToLowerInvariant();

  public static FriendCandidateRes ToRes(this FriendCandidate c) =>
    new(c.User.UserId, c.User.Username, c.Relation.ToRes());

  public static FriendRes ToRes(this FriendshipPrincipal f) => new(f.Friend.UserId, f.Friend.Username, f.SinceUtc);

  public static FriendRequestsRes ToRes(this FriendRequests r) =>
    new(r.Incoming.Select(x => new FriendRequestRes(x.From.UserId, x.From.Username, x.CreatedAtUtc)).ToList(),
      r.Outgoing.Select(x => new FriendRequestRes(x.To.UserId, x.To.Username, x.CreatedAtUtc)).ToList());

  public static FriendInviteRes ToRes(this FriendInvitePrincipal i) => new(i.Code, i.ExpiresAtUtc);
}
