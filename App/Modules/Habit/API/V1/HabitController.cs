using System.Net.Mime;
using App.Error.V1;
using App.Modules.Common;
using App.StartUp.Registry;
using App.StartUp.Services.Auth;
using App.Utility;
using Asp.Versioning;
using CSharp_Result;
using Domain.Habit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace App.Modules.Habit.API.V1;

[ApiVersion(1.0)]
[ApiController]
[Consumes(MediaTypeNames.Application.Json)]
[Route("api/v{version:apiVersion}/[controller]")]
public class HabitController(
    IHabitService service,
    IAuthHelper authHelper,
    CreateHabitReqValidator createHabitReqValidator,
    UpdateHabitReqValidator updateHabitReqValidator,
    MarkDailyFailuresReqValidator markDailyFailuresReqValidator,
    SearchHabitQueryValidator searchHabitQueryValidator
) : AtomiControllerBase(authHelper)
{
    [Authorize, HttpGet("{userId}")]
    public async Task<ActionResult<List<HabitVersionRes>>> SearchHabits(string userId, [FromQuery] SearchHabitQuery query)
    {
        var result = await this.GuardAsync(userId)
          .ThenAwait(_ => searchHabitQueryValidator.ValidateAsyncResult(query, "Invalid Search Habit Query"))
          .ThenAwait(_ => service.SearchHabits(query.ToDomain()))
          .Then(habits => habits.Select(h => h.ToRes()).ToList(), Errors.MapNone);
        
        return this.ReturnResult(result);
    }

    [Authorize, HttpGet("{userId}/{id:guid}")]
    public async Task<ActionResult<HabitVersionRes>> GetCurrentHabitVersion(string userId, Guid id)
    {
        var result = await this.GuardAsync(userId)
          .ThenAwait(_ => service.GetCurrentHabitVersion(userId, id))
          .Then(x => x?.ToRes(), Errors.MapNone);
        
        return this.ReturnNullableResult(result, new EntityNotFound(
            "Habit Not Found", typeof(HabitVersionPrincipal), id.ToString()));
    }

    [Authorize, HttpPost("{userId}")]
    public async Task<ActionResult<HabitVersionRes>> Create(string userId, [FromBody] CreateHabitReq req)
    {
        var result = await this.GuardAsync(userId)
          .ThenAwait(_ => createHabitReqValidator.ValidateAsyncResult(req, "Invalid CreateHabitReq"))
          .ThenAwait(x => service.Create(userId, x.ToVersionRecord()))
          .Then(h => h.ToRes(), Errors.MapNone);
        
        return this.ReturnResult(result);
    }

    [Authorize, HttpPut("{userId}/{id:guid}")]
    public async Task<ActionResult<HabitVersionRes>> Update(string userId, Guid id, [FromBody] UpdateHabitReq req)
    {
        var result = await this.GuardAsync(userId)
            .ThenAwait(_ => updateHabitReqValidator.ValidateAsyncResult(req, "Invalid UpdateHabitReq"))
            .ThenAwait(x => service.Update(userId, id, x.ToVersionRecord(), req.Enabled))
            .Then(h => h?.ToRes(), Errors.MapNone);
        
        return this.ReturnNullableResult(result, new EntityNotFound(
            "Habit Not Found", typeof(HabitVersionPrincipal), id.ToString()));
    }

    [Authorize, HttpDelete("{userId}/{id:guid}")]
    public async Task<ActionResult> Delete(string userId, Guid id)
    {
      var result = await this.GuardAsync(userId)
        .ThenAwait(_ => service.Delete(id, userId));
        
        return this.ReturnUnitNullableResult(result, 
          new EntityNotFound("Habit Not Found", typeof(Domain.Habit.Habit), id.ToString()));
    }

    [Authorize, HttpPost("{userId}/{habitVersionId:guid}/executions")]
    public async Task<ActionResult<HabitExecutionRes>> CompleteHabit(string userId, Guid habitVersionId, [FromBody] CompleteHabitReq req)
    {
      var result = await this.GuardAsync(userId)
        .ThenAwait(_ => service.CompleteHabit(userId, habitVersionId, req.Notes))
        .Then(execution => execution.ToRes(), Errors.MapNone);

      return this.ReturnResult(result);
    }

    [Authorize, HttpGet("{userId}/executions")]
    public async Task<ActionResult<List<HabitExecutionRes>>> GetDailyExecutions(string userId, 
      [FromQuery] SearchHabitExecutionQuery searchHabitExecutionQuery)
    {
        var result = await this.GuardAsync(userId)
          .ThenAwait(_ => service.SearchHabitExecutions(userId, searchHabitExecutionQuery.ToDomain()))
          .Then(executions => executions.Select(e => e.ToRes()).ToList(), 
            Errors.MapNone);
        return this.ReturnResult(result);
    }

    [Authorize(Policy = AuthPolicies.OnlyAdmin), HttpPost("executions/mark-daily-failures")]
    public async Task<ActionResult<int>> MarkDailyFailures([FromBody] MarkDailyFailuresReq req)
    {
      // todo change input from userid to list of habit id
        var result = await markDailyFailuresReqValidator
            .ValidateAsyncResult(req, "Invalid MarkDailyFailuresReq")
            .ThenAwait(x => service.MarkDailyFailures(x.HabitIds, x.Date.ToDate()));
        
        return this.ReturnResult(result);
    }
}
