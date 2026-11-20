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

    var created = await repo.CreateRequest(userId, toUserId);
    if (created.IsFailure()) return created.FailureOrDefault();
    var change = created.Get();
    if (!change.Changed) return change.Relation;
    var request = change.Request!;
    if (request.Status == FriendRequestStatus.Accepted)
    {
      await NotifyAccepted(sender, request);
      return change.Relation;
    }

    await Notify(toUserId, new InboxNotificationRecord
    {
      Kind = InboxKinds.FriendRequest,
      Title = "Friend request",
      Body = $"{sender.Username} wants to be friends",
      ActorUserId = userId,
      Data = JsonSerializer.Serialize(new { requestId = request.Id, fromUserId = userId }),
      FriendRequestId = request.Id,
      DedupeKey = $"{InboxKinds.FriendRequest}:{request.Id}",
    });
    return change.Relation;
  }

  public Task<Result<FriendRequests>> ListRequests(string userId) => repo.ListRequests(userId);

  public Task<Result<FriendshipPrincipal?>> Accept(string userId, string fromUserId) =>
    Task.FromResult<Result<FriendshipPrincipal?>>(new FriendRequestException(FriendRequestError.IdRequired));

  public Task<Result<Unit?>> DeleteRequest(string userId, string otherUserId) =>
    Task.FromResult<Result<Unit?>>(new FriendRequestException(FriendRequestError.IdRequired));

  public Task<Result<FriendRequestPrincipal>> GetRequest(string userId, Guid requestId) =>
    repo.GetRequest(userId, requestId);

  public async Task<Result<FriendRequestPrincipal>> ResolveRequest(string userId, Guid requestId, string decision)
  {
    var status = decision switch
    {
      "accept" => FriendRequestStatus.Accepted,
      "decline" => FriendRequestStatus.Declined,
      "cancel" => FriendRequestStatus.Cancelled,
      _ => FriendRequestStatus.Pending,
    };
    if (status == FriendRequestStatus.Pending)
      return new InvalidRequestException("decision", "Decision must be accept, decline or cancel");
    var resolved = await repo.ResolveRequest(userId, requestId, status);
    if (resolved.IsFailure()) return resolved.FailureOrDefault();
    var change = resolved.Get();
    if (change.Changed && status == FriendRequestStatus.Accepted)
      await NotifyAccepted(change.Request!.To, change.Request);
    return change.Request!;
  }

  public Task<Result<List<FriendshipPrincipal>>> ListFriends(string userId) => repo.ListFriends(userId);

  public async Task<Result<Unit?>> Remove(string userId, string friendUserId)
  {
    var deleted = await repo.DeleteFriendship(userId, friendUserId);
    if (deleted.IsFailure()) return deleted.FailureOrDefault();
    return deleted.Get() ? new Unit() : (Unit?)null;
  }

  public Task<Result<FriendInvitePrincipal>> GetOrCreateInvite(string userId) =>
    repo.GetOrCreateInvite(userId, RandomNumberGenerator.GetString(InviteAlphabet, InviteCodeLength));

  public async Task<Result<FriendRelation?>> AcceptInvite(string userId, string code)
  {
    var invite = await repo.GetInvite((code ?? "").Trim().ToLowerInvariant());
    if (invite.IsFailure()) return invite.FailureOrDefault();
    if (invite.Get() is not { } i) return (FriendRelation?)null;
    if (i.UserId == userId) return new InvalidRequestException("code", "You can't accept your own invite");

    // The owner still decides: a link only saves searching for their username.
    var sent = await SendRequest(userId, i.UserId);
    if (sent.IsFailure()) return sent.FailureOrDefault();
    return sent.Get();
  }

  public async Task<Result<bool>> AreFriends(string userId, string otherUserId)
  {
    var relation = await repo.Relation(userId, otherUserId);
    if (relation.IsFailure()) return relation.FailureOrDefault();
    return relation.Get() == FriendRelation.Friend;
  }

  private Task NotifyAccepted(FriendUser accepter, FriendRequestPrincipal request) =>
    Notify(request.From.UserId, new InboxNotificationRecord
    {
      Kind = InboxKinds.FriendAccepted,
      Title = "New friend",
      Body = $"{accepter.Username} is now your friend",
      ActorUserId = accepter.UserId,
      Data = JsonSerializer.Serialize(new { friendUserId = accepter.UserId }),
      DedupeKey = $"{InboxKinds.FriendAccepted}:{request.Id}",
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
