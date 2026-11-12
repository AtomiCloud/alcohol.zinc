using System.Net.Mime;
using App.Error.V1;
using App.Modules.Common;
using App.StartUp.Services.Auth;
using App.Utility;
using Asp.Versioning;
using CSharp_Result;
using Domain.Friend;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace App.Modules.Friend.API.V1;

[ApiVersion(1.0)]
[ApiController]
[Consumes(MediaTypeNames.Application.Json)]
[Route("api/v{version:apiVersion}/[controller]")]
public class FriendController(
  IFriendService service,
  IAuthHelper authHelper,
  SendFriendRequestReqValidator sendRequestValidator
) : AtomiControllerBase(authHelper)
{
  // Usernames starting with q (case-insensitive); never email or name.
  [Authorize, HttpGet("{userId}/search")]
  public async Task<ActionResult<List<FriendCandidateRes>>> Search(string userId, [FromQuery] string? q,
    [FromQuery] int limit = FriendService.MaxSearchLimit)
  {
    var result = await this.GuardAsync(userId)
      .ThenAwait(_ => service.Search(userId, q ?? "", limit))
      .Then(xs => xs.Select(x => x.ToRes()).ToList(), Errors.MapNone);

    return this.ReturnResult(result);
  }

  [Authorize, HttpGet("{userId}")]
  public async Task<ActionResult<List<FriendRes>>> ListFriends(string userId)
  {
    var result = await this.GuardAsync(userId)
      .ThenAwait(_ => service.ListFriends(userId))
      .Then(xs => xs.Select(x => x.ToRes()).ToList(), Errors.MapNone);

    return this.ReturnResult(result);
  }

  [Authorize, HttpDelete("{userId}/{friendUserId}")]
  public async Task<ActionResult> Remove(string userId, string friendUserId)
  {
    var result = await this.GuardAsync(userId)
      .ThenAwait(_ => service.Remove(userId, friendUserId));

    return this.ReturnUnitNullableResult(result,
      new EntityNotFound("Friend Not Found", typeof(FriendshipPrincipal), friendUserId));
  }

  [Authorize, HttpGet("{userId}/requests")]
  public async Task<ActionResult<FriendRequestsRes>> ListRequests(string userId)
  {
    var result = await this.GuardAsync(userId)
      .ThenAwait(_ => service.ListRequests(userId))
      .Then(r => r.ToRes(), Errors.MapNone);

    return this.ReturnResult(result);
  }

  // Asking someone who already asked you accepts their request (relation "friend").
  [Authorize, HttpPost("{userId}/requests")]
  public async Task<ActionResult<FriendRelationRes>> SendRequest(string userId, [FromBody] SendFriendRequestReq req)
  {
    var result = await this.GuardAsync(userId)
      .ThenAwait(_ => sendRequestValidator.ValidateAsyncResult(req, "Invalid SendFriendRequestReq"))
      .ThenAwait(r => service.SendRequest(userId, r.ToUserId))
      .Then(r => new FriendRelationRes(r.ToRes()), Errors.MapNone);

    return this.ReturnResult(result);
  }

  [Authorize, HttpPost("{userId}/requests/{fromUserId}/accept")]
  public async Task<ActionResult<FriendRes>> Accept(string userId, string fromUserId)
  {
    var result = await this.GuardAsync(userId)
      .ThenAwait(_ => service.Accept(userId, fromUserId))
      .Then(f => f?.ToRes(), Errors.MapNone);

    return this.ReturnNullableResult(result,
      new EntityNotFound("Friend Request Not Found", typeof(FriendRequestPrincipal), fromUserId));
  }

  // Declines an incoming request or cancels an outgoing one.
  [Authorize, HttpDelete("{userId}/requests/{otherUserId}")]
  public async Task<ActionResult> DeleteRequest(string userId, string otherUserId)
  {
    var result = await this.GuardAsync(userId)
      .ThenAwait(_ => service.DeleteRequest(userId, otherUserId));

    return this.ReturnUnitNullableResult(result,
      new EntityNotFound("Friend Request Not Found", typeof(FriendRequestPrincipal), otherUserId));
  }

  // The user's permanent invite: the same code on every call.
  [Authorize, HttpPost("{userId}/invites")]
  public async Task<ActionResult<FriendInviteRes>> CreateInvite(string userId)
  {
    var result = await this.GuardAsync(userId)
      .ThenAwait(_ => service.GetOrCreateInvite(userId))
      .Then(i => i.ToRes(), Errors.MapNone);

    return this.ReturnResult(result);
  }

  // Sends the code's owner a friend request, like POST requests: "outgoing", or "friend" when
  // the owner had already asked you.
  [Authorize, HttpPost("{userId}/invites/{code}/accept")]
  public async Task<ActionResult<FriendRelationRes>> AcceptInvite(string userId, string code)
  {
    var result = await this.GuardAsync(userId)
      .ThenAwait(_ => service.AcceptInvite(userId, code))
      .Then(r => r is { } relation ? new FriendRelationRes(relation.ToRes()) : null, Errors.MapNone);

    return this.ReturnNullableResult(result,
      new EntityNotFound("Friend Invite Not Found", typeof(FriendInvitePrincipal), code));
  }
}
