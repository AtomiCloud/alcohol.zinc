using System.Security.Cryptography;
using System.Text.Json;
using CSharp_Result;
using Domain.Exceptions;
using Domain.Notification;
using Microsoft.Extensions.Logging;

namespace Domain.Friend;

public class FriendService(
  IFriendRepository repo,
  IInboxService inbox,
  ILogger<FriendService> logger
) : IFriendService
{
  public const int MinQueryLength = 2;
  public const int MaxSearchLimit = 20;
  public const int InviteCodeLength = 10;
  public static readonly TimeSpan InviteLifetime = TimeSpan.FromDays(7);

  // Lowercase letters and digits without the look-alikes 0/o, 1/l/i, so a code read aloud or
  // retyped from a screenshot still works.
  public const string InviteAlphabet = "23456789abcdefghjkmnpqrstuvwxyz";

  public async Task<Result<List<FriendCandidate>>> Search(string userId, string query, int limit)
  {
    var q = (query ?? "").Trim();
    if (q.Length < MinQueryLength)
      return new InvalidRequestException("q", $"Search needs at least {MinQueryLength} characters");
    if (limit is < 1 or > MaxSearchLimit)
      return new InvalidRequestException("limit", $"Limit must be between 1 and {MaxSearchLimit}");
    return await repo.Search(userId, q, limit);
  }

  public async Task<Result<FriendRelation>> SendRequest(string userId, string toUserId)
  {
    if (userId == toUserId) return new InvalidRequestException("toUserId", "You can't friend yourself");

    var me = await repo.GetUser(userId);
    if (me.IsFailure()) return me.FailureOrDefault();
    if (me.Get() is not { } sender) return new NotFoundException("User not found", typeof(FriendUser), userId);

    var target = await repo.GetUser(toUserId);
    if (target.IsFailure()) return target.FailureOrDefault();
    if (target.Get() == null) return new NotFoundException("User not found", typeof(FriendUser), toUserId);

    var relation = await repo.Relation(userId, toUserId);
    if (relation.IsFailure()) return relation.FailureOrDefault();
    switch (relation.Get())
    {
      case FriendRelation.Friend:
      case FriendRelation.Outgoing:
        return relation.Get();
      case FriendRelation.Incoming:
        // They already asked: asking back is accepting.
        var accepted = await AcceptRequest(sender, toUserId);
        if (accepted.IsFailure()) return accepted.FailureOrDefault();
        return FriendRelation.Friend;
    }

    var created = await repo.CreateRequest(userId, toUserId);
    if (created.IsFailure()) return created.FailureOrDefault();
    await Notify(toUserId, new InboxNotificationRecord
    {
      Kind = InboxKinds.FriendRequest,
      Title = "Friend request",
      Body = $"{sender.Username} wants to be friends",
      ActorUserId = userId,
      Data = JsonSerializer.Serialize(new { fromUserId = userId }),
    });
    return FriendRelation.Outgoing;
  }

  public Task<Result<FriendRequests>> ListRequests(string userId) => repo.ListRequests(userId);

  public async Task<Result<FriendshipPrincipal?>> Accept(string userId, string fromUserId)
  {
    var relation = await repo.Relation(userId, fromUserId);
    if (relation.IsFailure()) return relation.FailureOrDefault();
    if (relation.Get() != FriendRelation.Incoming) return (FriendshipPrincipal?)null;

    var me = await repo.GetUser(userId);
    if (me.IsFailure()) return me.FailureOrDefault();
    if (me.Get() is not { } accepter) return new NotFoundException("User not found", typeof(FriendUser), userId);

    var accepted = await AcceptRequest(accepter, fromUserId);
    if (accepted.IsFailure()) return accepted.FailureOrDefault();
    return accepted.Get();
  }

  public async Task<Result<Unit?>> DeleteRequest(string userId, string otherUserId)
  {
    var deleted = await repo.DeleteRequest(userId, otherUserId);
    if (deleted.IsFailure()) return deleted.FailureOrDefault();
    return deleted.Get() ? new Unit() : (Unit?)null;
  }

  public Task<Result<List<FriendshipPrincipal>>> ListFriends(string userId) => repo.ListFriends(userId);

  public async Task<Result<Unit?>> Remove(string userId, string friendUserId)
  {
    var deleted = await repo.DeleteFriendship(userId, friendUserId);
    if (deleted.IsFailure()) return deleted.FailureOrDefault();
    return deleted.Get() ? new Unit() : (Unit?)null;
  }

  public Task<Result<FriendInvitePrincipal>> CreateInvite(string userId)
  {
    var code = RandomNumberGenerator.GetString(InviteAlphabet, InviteCodeLength);
    return repo.CreateInvite(userId, code, DateTime.UtcNow.Add(InviteLifetime));
  }

  public async Task<Result<FriendshipPrincipal?>> AcceptInvite(string userId, string code)
  {
    var invite = await repo.GetInvite((code ?? "").Trim().ToLowerInvariant());
    if (invite.IsFailure()) return invite.FailureOrDefault();
    if (invite.Get() is not { } i || i.ExpiresAtUtc <= DateTime.UtcNow) return (FriendshipPrincipal?)null;
    if (i.UserId == userId) return new InvalidRequestException("code", "You can't accept your own invite");

    var relation = await repo.Relation(userId, i.UserId);
    if (relation.IsFailure()) return relation.FailureOrDefault();
    var wereFriends = relation.Get() == FriendRelation.Friend;

    var me = await repo.GetUser(userId);
    if (me.IsFailure()) return me.FailureOrDefault();
    if (me.Get() is not { } accepter) return new NotFoundException("User not found", typeof(FriendUser), userId);

    // Idempotent: already friends returns the existing friendship (and clears any stale request).
    var friendship = await repo.CreateFriendship(userId, i.UserId);
    if (friendship.IsFailure()) return friendship.FailureOrDefault();
    if (!wereFriends) await NotifyAccepted(accepter, i.UserId, friendship.Get());
    return friendship.Get();
  }

  public async Task<Result<bool>> AreFriends(string userId, string otherUserId)
  {
    var relation = await repo.Relation(userId, otherUserId);
    if (relation.IsFailure()) return relation.FailureOrDefault();
    return relation.Get() == FriendRelation.Friend;
  }

  private async Task<Result<FriendshipPrincipal>> AcceptRequest(FriendUser accepter, string requesterId)
  {
    var friendship = await repo.CreateFriendship(accepter.UserId, requesterId);
    if (friendship.IsFailure()) return friendship.FailureOrDefault();
    await NotifyAccepted(accepter, requesterId, friendship.Get());
    return friendship.Get();
  }

  private Task NotifyAccepted(FriendUser accepter, string toUserId, FriendshipPrincipal friendship) =>
    Notify(toUserId, new InboxNotificationRecord
    {
      Kind = InboxKinds.FriendAccepted,
      Title = "New friend",
      Body = $"{accepter.Username} is now your friend",
      ActorUserId = accepter.UserId,
      Data = JsonSerializer.Serialize(new { friendUserId = accepter.UserId }),
      // Two concurrent accepts of the same request end up with the same friendship row (and so
      // the same SinceUtc): only one notification. A later re-friending gets a new SinceUtc.
      DedupeKey = $"{InboxKinds.FriendAccepted}:{accepter.UserId}:{friendship.SinceUtc.Ticks}",
    });

  // Best effort, after the friendship change is committed: a failed notification (a failure
  // Result or a thrown exception) is logged and swallowed, never undoing or failing the request.
  // Retrying the call would be a no-op anyway (already asked / already friends), so failing it
  // would only show the user an error for something that worked.
  private async Task Notify(string userId, InboxNotificationRecord record)
  {
    try
    {
      var posted = await inbox.Post(userId, record);
      if (posted.IsFailure())
        logger.LogError(posted.FailureOrDefault(), "Inbox {Kind} to UserId={UserId} from ActorUserId={ActorUserId} failed",
          record.Kind, userId, record.ActorUserId);
    }
    catch (Exception e)
    {
      logger.LogError(e, "Inbox {Kind} to UserId={UserId} from ActorUserId={ActorUserId} threw",
        record.Kind, userId, record.ActorUserId);
    }
  }
}
