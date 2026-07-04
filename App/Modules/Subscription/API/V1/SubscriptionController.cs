using System.Net.Mime;
using App.Modules.Common;
using App.StartUp.Services.Auth;
using App.Utility;
using Asp.Versioning;
using CSharp_Result;
using Domain.Subscription;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace App.Modules.Subscription.API.V1;

[ApiVersion(1.0)]
[ApiController]
[Consumes(MediaTypeNames.Application.Json)]
[Route("api/v{version:apiVersion}/[controller]")]
public class SubscriptionController(
  ISubscriptionRepository subscriptionRepository,
  ISubscriptionManagementService managementService,
  IAuthHelper authHelper,
  SubscribeReqValidator subscribeValidator,
  ChangeTierReqValidator changeTierValidator
) : AtomiControllerBase(authHelper)
{
  [Authorize, HttpGet("{userId}")]
  public async Task<ActionResult<SubscriptionRes>> Get(string userId)
  {
    var result = await this.GuardAsync(userId)
      .ThenAwait(_ => subscriptionRepository.GetByUserId(userId))
      .Then(sub => sub?.ToRes() ?? SubscriptionMapper.ToFreeRes(userId), Errors.MapNone);

    return this.ReturnResult(result);
  }

  [Authorize, HttpPost("{userId}/subscribe")]
  public async Task<ActionResult<SubscriptionRes>> Subscribe(string userId, [FromBody] SubscribeReq req)
  {
    var result = await this.GuardAsync(userId)
      .ThenAwait(_ => subscribeValidator.ValidateAsyncResult(req, "Invalid SubscribeReq"))
      .ThenAwait(r => managementService.Subscribe(userId, r.Tier))
      .Then(sub => sub.ToRes(), Errors.MapNone);

    return this.ReturnResult(result);
  }

  [Authorize, HttpPost("{userId}/cancel")]
  public async Task<ActionResult<SubscriptionRes>> Cancel(string userId)
  {
    var result = await this.GuardAsync(userId)
      .ThenAwait(_ => managementService.Cancel(userId))
      .Then(sub => sub.ToRes(), Errors.MapNone);

    return this.ReturnResult(result);
  }

  [Authorize, HttpPost("{userId}/change-tier")]
  public async Task<ActionResult<SubscriptionRes>> ChangeTier(string userId, [FromBody] ChangeTierReq req)
  {
    var result = await this.GuardAsync(userId)
      .ThenAwait(_ => changeTierValidator.ValidateAsyncResult(req, "Invalid ChangeTierReq"))
      .ThenAwait(r => managementService.ChangeTier(userId, r.Tier))
      .Then(sub => sub.ToRes(), Errors.MapNone);

    return this.ReturnResult(result);
  }
}
