using App.Error.V1;
using App.Modules.Common;
using App.StartUp.Services.Auth;
using App.Utility;
using Asp.Versioning;
using CSharp_Result;
using Domain.Notification;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace App.Modules.Notification.API.V1;

// The user's in-app inbox. Notifications are posted by other features through IInboxService.Post.
[ApiVersion(1.0)]
[ApiController]
[Route("api/v{version:apiVersion}/[controller]")]
public class InboxController(
  IInboxService service,
  IAuthHelper authHelper
) : AtomiControllerBase(authHelper)
{
  // Newest first. Limit 1..50 and Skip >= 0 are checked by the service (400 otherwise).
  [Authorize, HttpGet("{userId}")]
  public async Task<ActionResult<List<InboxNotificationRes>>> Search(string userId, [FromQuery] SearchInboxQuery query)
  {
    var result = await this.GuardAsync(userId)
      .ThenAwait(_ => service.Search(query.ToDomain(userId)))
      .Then(list => list.Select(n => n.ToRes()).ToList(), Errors.MapNone);

    return this.ReturnResult(result);
  }

  [Authorize, HttpGet("{userId}/unread-count")]
  public async Task<ActionResult<InboxCountRes>> UnreadCount(string userId)
  {
    var result = await this.GuardAsync(userId)
      .ThenAwait(_ => service.UnreadCount(userId))
      .Then(c => new InboxCountRes(c), Errors.MapNone);

    return this.ReturnResult(result);
  }

  // 204 (also when already read), 404 when it isn't this user's notification.
  [Authorize, HttpPost("{userId}/{id:guid}/read")]
  public async Task<ActionResult> MarkRead(string userId, Guid id)
  {
    var result = await this.GuardAsync(userId)
      .ThenAwait(_ => service.MarkRead(userId, id));

    return this.ReturnUnitNullableResult(result,
      new EntityNotFound("Inbox Notification Not Found", typeof(InboxNotificationPrincipal), id.ToString()));
  }

  // Count is how many were unread and are now read.
  [Authorize, HttpPost("{userId}/read-all")]
  public async Task<ActionResult<InboxCountRes>> MarkAllRead(string userId)
  {
    var result = await this.GuardAsync(userId)
      .ThenAwait(_ => service.MarkAllRead(userId))
      .Then(c => new InboxCountRes(c), Errors.MapNone);

    return this.ReturnResult(result);
  }
}
