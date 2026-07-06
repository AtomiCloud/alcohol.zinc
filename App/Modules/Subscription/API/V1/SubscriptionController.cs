using System.Net.Mime;
using App.Modules.Auth;
using App.StartUp.Options;
using App.Modules.Auth.API.V1;
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
  IWebHandoffService webHandoffService,
  IAuthHelper authHelper,
  SubscribeReqValidator subscribeValidator,
  ChangeTierReqValidator changeTierValidator,
  WebHandoffReqValidator webHandoffValidator,
  Microsoft.Extensions.Options.IOptionsMonitor<SubscriptionOption> subscriptionOptions
) : AtomiControllerBase(authHelper)
{
  // Public plan catalog for the pricing page — the single source of truth for
  // tiers, caps and prices (marketing presentation stays in the frontend).
  // The literal "plans" segment wins over the {userId} route by ASP.NET
  // routing precedence.
  [AllowAnonymous, HttpGet("plans")]
  [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any)]
  public ActionResult<List<PlanRes>> Plans()
  {
    return this.Ok(subscriptionOptions.CurrentValue.ToPlansRes());
  }

  // Which subscription CTA the app may show for this platform + storefront.
  // Server-decided so steering rules can change per landscape without an app
  // release; clients treat unknown variants as neutral.
  [Authorize, HttpGet("{userId}/cta")]
  public async Task<ActionResult<SubscriptionCtaRes>> Cta(string userId,
    [FromQuery] string? platform, [FromQuery] string? storefront)
  {
    var result = await this.GuardAsync(userId)
      .ThenAwait(_ => webHandoffValidator.ValidateAsyncResult(
        new WebHandoffReq(platform ?? string.Empty, storefront), "Invalid CTA query"))
      .ThenAwait(q => webHandoffService.ResolveCta(userId, q.Platform, q.Storefront))
      .Then(c => new SubscriptionCtaRes(c.Variant, c.Tier), Errors.MapNone);

    return this.ReturnResult(result);
  }

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
