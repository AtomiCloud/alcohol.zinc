using System.Net.Mime;
using App.Modules.Common;
using App.StartUp.Registry;
using App.StartUp.Services.Auth;
using Asp.Versioning;
using CSharp_Result;
using CSharp_Result;
using Domain.Habit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace App.Modules.Habit.API.V1;

[ApiVersion(1.0)]
[ApiController]
[Consumes(MediaTypeNames.Application.Json)]
[Route("api/v{version:apiVersion}/habit-cron")]
public class HabitCronController(IHabitService service, IAuthHelper authHelper) : AtomiControllerBase(authHelper)
{
  // Admin-only endpoint for scheduled invocation (e.g., every 15 minutes).
  // It scans timezones near local 23:59 and marks failures accordingly.
  [Authorize(Policy = AuthPolicies.OnlyAdmin), HttpPost("mark-daily-failures")] 
  public async Task<ActionResult<MarkDailyFailuresCronRes>> MarkDailyFailuresCron()
  {
    var result = await service.MarkDailyFailuresForTimezonesNearMidnight()
      .Then(total => new MarkDailyFailuresCronRes(total), Errors.MapNone);
    return this.ReturnResult(result);
  }
}
