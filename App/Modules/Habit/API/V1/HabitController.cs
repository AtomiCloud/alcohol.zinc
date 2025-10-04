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
    MarkDailyFailuresReqValidator markDailyFailuresReqValidator
) : AtomiControllerBase(authHelper)
{
    [Authorize, HttpGet("")]
    public async Task<ActionResult<List<HabitVersionRes>>> ListAllHabits()
    {
        var userId = this.Sub();
        if (userId == null) return Unauthorized();

        var result = await service.ListAllUserHabits(userId)
            .Then(habits => habits.Select(h => h.ToRes()).ToList().ToResult());
        return this.ReturnResult(result);
    }

    [Authorize, HttpGet("{id:guid}")]
    public async Task<ActionResult<HabitVersionRes>> GetCurrentHabitVersion(Guid id)
    {
        var userId = this.Sub();
        if (userId == null) return Unauthorized();

        var result = await service.GetCurrentHabitVersion(userId, id)
            .Then(x => x?.ToRes(), Errors.MapAll);
        return this.ReturnNullableResult(result, new EntityNotFound(
            "Habit Not Found", typeof(HabitVersionPrincipal), id.ToString()));
    }

    [Authorize, HttpPost]
    public async Task<ActionResult<HabitVersionRes>> Create([FromBody] CreateHabitReq req)
    {
        var userId = this.Sub();
        if (userId == null) return Unauthorized();

        var result = await createHabitReqValidator
            .ValidateAsyncResult(req, "Invalid CreateHabitReq")
            .ThenAwait(x => service.Create(userId, x.ToVersionRecord()))
            .Then(h => h.ToRes().ToResult());
        return this.ReturnResult(result);
    }

    [Authorize, HttpPut("{id:guid}")]
    public async Task<ActionResult<HabitVersionRes>> Update(Guid id, [FromBody] UpdateHabitReq req)
    {
        var userId = this.Sub();
        if (userId == null) return Unauthorized();

        var result = await updateHabitReqValidator
            .ValidateAsyncResult(req, "Invalid UpdateHabitReq")
            .ThenAwait(x => service.Update(userId, id, x.ToVersionRecord(0), req.Enabled)) // Version will be set by repository, pass enabled status
            .Then(h => h?.ToRes(), Errors.MapAll);
        return this.ReturnNullableResult(result, new EntityNotFound(
            "Habit Not Found", typeof(HabitVersionPrincipal), id.ToString()));
    }

    [Authorize, HttpDelete("{id:guid}")]
    public async Task<ActionResult<Unit>> Delete(Guid id)
    {
        var userId = this.Sub();
        if (userId == null) return Unauthorized();

        var result = await service.Delete(id, userId)
            .Then(unit => unit != null ? new Unit().ToResult() : 
                new EntityNotFound("Habit Not Found", typeof(HabitPrincipal), id.ToString()).ToException());
        return this.ReturnResult(result);
    }

    [Authorize, HttpPost("{habitVersionId:guid}/executions")]
    public async Task<ActionResult<HabitExecutionRes>> CompleteHabit(Guid habitVersionId, [FromBody] CompleteHabitReq req)
    {
        var userId = this.Sub();
        if (userId == null) return Unauthorized();

        var result = await service.CompleteHabit(userId, habitVersionId, req.Notes)
            .Then(execution => execution.ToRes().ToResult());
        return this.ReturnResult(result);
    }

    [Authorize, HttpGet("executions")]
    public async Task<ActionResult<List<HabitExecutionRes>>> GetDailyExecutions([FromQuery] string? date = null)
    {
        var userId = this.Sub();
        if (userId == null) return Unauthorized();

        DateOnly targetDate;
        if (string.IsNullOrEmpty(date))
        {
            targetDate = DateOnly.FromDateTime(DateTime.Today);
        }
        else
        {
            if (!DateOnly.TryParse(date, out targetDate))
            {
                return BadRequest($"Invalid date format: {date}. Expected format: YYYY-MM-DD");
            }
        }

        var result = await service.GetDailyExecutions(userId, targetDate)
            .Then(executions => executions.Select(e => e.ToRes()).ToList().ToResult());
        return this.ReturnResult(result);
    }

    [Authorize(Policy = AuthPolicies.OnlyAdmin), HttpPost("executions/mark-daily-failures")]
    public async Task<ActionResult<int>> MarkDailyFailures([FromBody] MarkDailyFailuresReq req)
    {
      //todo change input from userid to list of habit id
        var result = await markDailyFailuresReqValidator
            .ValidateAsyncResult(req, "Invalid MarkDailyFailuresReq")
            .ThenAwait(x => service.MarkDailyFailures(x.UserIds, x.Date.ToDate()));
        return this.ReturnResult(result);
    }
}
