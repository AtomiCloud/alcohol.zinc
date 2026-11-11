using System.Net.Mime;
using App.Modules.Common;
using App.StartUp.Services.Auth;
using App.Utility;
using Asp.Versioning;
using CSharp_Result;
using Domain.FriendStake;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace App.Modules.FriendStake.API.V1;

// "Tell my friends if I miss": which friends hear when a habit's day ends Missed.
[ApiVersion(1.0)]
[ApiController]
[Consumes(MediaTypeNames.Application.Json)]
[Route("api/v{version:apiVersion}/[controller]")]
public class FriendStakeController(
  IFriendStakeService service,
  IAuthHelper authHelper,
  SetFriendWatchersReqValidator setValidator
) : AtomiControllerBase(authHelper)
{
  [Authorize, HttpGet("{userId}/{habitId:guid}")]
  public async Task<ActionResult<FriendStakeRes>> Get(string userId, Guid habitId)
  {
    var result = await this.GuardAsync(userId)
      .ThenAwait(_ => service.GetWatchers(userId, habitId))
      .Then(w => w.ToRes(habitId), Errors.MapNone);

    return this.ReturnResult(result);
  }

  // Replaces the habit's watchers; an empty list turns it off.
  [Authorize, HttpPut("{userId}/{habitId:guid}")]
  public async Task<ActionResult<FriendStakeRes>> Set(string userId, Guid habitId, [FromBody] SetFriendWatchersReq req)
  {
    var result = await this.GuardAsync(userId)
      .ThenAwait(_ => setValidator.ValidateAsyncResult(req, "Invalid SetFriendWatchersReq"))
      .ThenAwait(r => service.SetWatchers(userId, habitId, r.FriendUserIds))
      .Then(w => w.ToRes(habitId), Errors.MapNone);

    return this.ReturnResult(result);
  }
}
