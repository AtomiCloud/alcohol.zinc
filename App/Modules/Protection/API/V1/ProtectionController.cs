using System.Net.Mime;
using App.Modules.Common;
using App.StartUp.Registry;
using App.StartUp.Services.Auth;
using App.Utility;
using Asp.Versioning;
using CSharp_Result;
using Domain.Protection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace App.Modules.Protection.API.V1;

[ApiVersion(1.0)]
[ApiController]
[Consumes(MediaTypeNames.Application.Json)]
[Route("api/v{version:apiVersion}/[controller]")]
public class ProtectionController(
  IAuthHelper authHelper,
  IFreezeCapService freezeCapService,
  Domain.Protection.IProtectionAwardService protectionAwardService
) : AtomiControllerBase(authHelper)
{
  public record ProtectionBalanceRes(string UserId, int Balance, int Cap);

  // Reading the balance cuts it to the current cap first, so Balance is never above Cap.
  [Authorize, HttpGet("{userId}")]
  public async Task<ActionResult<ProtectionBalanceRes>> GetBalance(string userId)
  {
    var result = await this.GuardAsync(userId)
      .ThenAwait(_ => freezeCapService.Reconcile(userId))
      .Then(b => new ProtectionBalanceRes(userId, b.Balance, b.Cap), Errors.MapNone);

    return this.ReturnResult(result);
  }

  public record AwardWeeklyReq(string UserId);
  public record AwardWeeklyRes(string UserId, int Awards);

  // Event-driven trigger: external system can POST to award weekly freezes for a user
  [Authorize(Policy = AuthPolicies.OnlyAdmin), HttpPost("award-weekly")]
  public async Task<ActionResult<AwardWeeklyRes>> AwardWeekly([FromBody] AwardWeeklyReq req)
  {
    var result = await this.GuardAsync(req.UserId)
      .ThenAwait(_ => protectionAwardService.AwardWeeklyFreezesForUser(req.UserId))
      .Then(total => new AwardWeeklyRes(req.UserId, total), Errors.MapNone);

    return this.ReturnResult(result);
  }
}
