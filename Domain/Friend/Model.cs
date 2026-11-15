using CSharp_Result;

namespace Domain.Friend;

// What another user may see of you: the username only, never email or name.
public record FriendUser(string UserId, string Username);

public enum FriendRelation
{
  None,
  Friend,
  Outgoing, // you asked them
  Incoming, // they asked you
}

public record FriendCandidate(FriendUser User, FriendRelation Relation);

public enum FriendRequestStatus
{
  Pending = 0,
  Accepted = 1,
  Declined = 2,
  Cancelled = 3,
}

public record FriendRequestPrincipal(Guid Id, FriendUser From, FriendUser To, DateTime CreatedAtUtc,
  FriendRequestStatus Status, DateTime? ResolvedAtUtc);

// Changed distinguishes the transaction that made the transition from an idempotent retry.
public record FriendRequestChange(FriendRequestPrincipal? Request, FriendRelation Relation, bool Changed);

public record FriendRequests(List<FriendRequestPrincipal> Incoming, List<FriendRequestPrincipal> Outgoing);

public record FriendshipPrincipal(FriendUser Friend, DateTime SinceUtc);

// A user's permanent invite link: lazytax.club/f/{Code}. It never expires.
public record FriendInvitePrincipal(string Code, string UserId);

public interface IFriendService
{
  // Usernames starting with query (case-insensitive), excluding the caller, with how each
  // relates to the caller.
  Task<Result<List<FriendCandidate>>> Search(string userId, string query, int limit);

  // Asks toUserId to be friends and notifies them. If they already asked the caller, this
  // accepts their request instead. Already friends or already asked: no-op, current relation.
  Task<Result<FriendRelation>> SendRequest(string userId, string toUserId);

  Task<Result<FriendRequests>> ListRequests(string userId);

  Task<Result<FriendRequestPrincipal>> GetRequest(string userId, Guid requestId);
  Task<Result<FriendRequestPrincipal>> ResolveRequest(string userId, Guid requestId, string decision);

  // Legacy pair-only mutations require an app update and never write.
  Task<Result<FriendshipPrincipal?>> Accept(string userId, string fromUserId);

  Task<Result<Unit?>> DeleteRequest(string userId, string otherUserId);

  Task<Result<List<FriendshipPrincipal>>> ListFriends(string userId);

  // Ends the friendship both ways, and stops each telling the other about missed habits.
  // Null when they weren't friends.
  Task<Result<Unit?>> Remove(string userId, string friendUserId);

  // The user's one invite: the same code every time, created on the first call.
  Task<Result<FriendInvitePrincipal>> GetOrCreateInvite(string userId);

  // Sends the invite's owner a friend request, exactly as SendRequest does: Outgoing, or Friend
  // when the owner had already asked the caller. Null for an unknown code.
  Task<Result<FriendRelation?>> AcceptInvite(string userId, string code);

  Task<Result<bool>> AreFriends(string userId, string otherUserId);
}

public interface IFriendRepository
{
  Task<Result<FriendUser?>> GetUser(string userId);
  Task<Result<List<FriendCandidate>>> Search(string userId, string query, int limit);
  Task<Result<FriendRelation>> Relation(string userId, string otherUserId);

  // Re-reads the pair under ordered user row locks. Asking back accepts the pending occurrence.
  Task<Result<FriendRequestChange>> CreateRequest(string fromUserId, string toUserId);
  Task<Result<FriendRequests>> ListRequests(string userId);
  Task<Result<FriendRequestPrincipal>> GetRequest(string userId, Guid requestId);
  Task<Result<FriendRequestChange>> ResolveRequest(string userId, Guid requestId, FriendRequestStatus status);

  Task<Result<List<FriendshipPrincipal>>> ListFriends(string userId);

  // Deletes both directions and the HabitFriendWatchers each had on the other's habits.
  Task<Result<bool>> DeleteFriendship(string userId, string friendUserId);

  // The user's invite, or a new one with this code when they have none (one per user).
  Task<Result<FriendInvitePrincipal>> GetOrCreateInvite(string userId, string code);
  Task<Result<FriendInvitePrincipal?>> GetInvite(string code);
}
