using System.Net.Mime;
using App.Modules.Common;
using App.StartUp.Registry;
using App.StartUp.Services.Auth;
using Asp.Versioning;
using CSharp_Result;
using Domain.Check;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace App.Modules.Habit.API.V1;

[ApiVersion(1.0)]
[ApiController]
[Consumes(MediaTypeNames.Application.Json)]
[Route("api/v{version:apiVersion}/habit-cron")]
public class HabitCronController(ICheckService service, IAuthHelper authHelper) : AtomiControllerBase(authHelper)
{
  // Admin-only manual trigger for one scheduler tick: opens upcoming checks, settles
  // checks past their deadline (vacation, freeze, then missed) and dispatches stakes.
  [Authorize(Policy = AuthPolicies.OnlyAdmin), HttpPost("mark-daily-failures")] 
  public async Task<ActionResult<MarkDailyFailuresCronRes>> MarkDailyFailuresCron()
  {
    var result = await service.Tick(DateTime.UtcNow)
      .Then(total => new MarkDailyFailuresCronRes(total), Errors.MapNone);
    return this.ReturnResult(result);
  }
}
