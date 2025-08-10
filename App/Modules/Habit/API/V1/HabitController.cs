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
    CreateHabitReqValidator createHabitReqValidator
) : AtomiControllerBase(authHelper)
{
    [Authorize, HttpGet("{userId}")]
    public async Task<ActionResult<List<HabitRes>>> List(string userId)
    {
        var result = await this
            .GuardOrAnyAsync(userId, AuthRoles.Field, AuthRoles.Admin)
            .ThenAwait(_ => service.List(userId))
            .Then(habits => habits.Select(h => h.ToRes()).ToList().ToResult());
        return this.ReturnResult(result);
    }

    [Authorize, HttpGet("{userId}/{id:guid}")]
    public async Task<ActionResult<HabitRes>> Get(string userId, Guid id)
    {
        var result = await this
            .GuardOrAnyAsync(userId, AuthRoles.Field, AuthRoles.Admin)
            .ThenAwait(_ => service.Get(userId, id))
            .Then(x => x?.ToRes(), Errors.MapAll);
        return this.ReturnNullableResult(result, new EntityNotFound(
            "Habit Not Found", typeof(HabitPrincipal), id.ToString()));
    }

    [Authorize, HttpPost("{userId}")]
    public async Task<ActionResult<HabitRes>> Create(string userId, [FromBody] CreateHabitReq req)
    {
        var result = await this
            .GuardOrAnyAsync(userId, AuthRoles.Field, AuthRoles.Admin)
            .ThenAwait(_ => createHabitReqValidator.ValidateAsyncResult(req, "Invalid CreateValidReq"))
            .ThenAwait(x => service.Create(userId, x.ToRecord(), x.CharityId))
            .Then(h => h.ToRes().ToResult());
        return this.ReturnResult(result);
    }

    [Authorize, HttpPut("{userId}/{id:guid}")]
    public async Task<ActionResult<HabitRes>> Update(string userId, Guid id, [FromBody] UpdateHabitReq req)
    {
        if (userId != req.UserId)
            return BadRequest("UserId in route and body must match.");

        var result = await this
            .GuardOrAnyAsync(userId, AuthRoles.Field, AuthRoles.Admin)
            .ThenAwait(_ => service.Update(req.ToPrincipal(id)))
            .Then(x => x?.ToRes(), Errors.MapAll);
        return this.ReturnNullableResult(result, new EntityNotFound(
            "Habit Not Found", typeof(HabitPrincipal), id.ToString()));
    }

    [Authorize, HttpDelete("{userId}/{id:guid}")]
    public async Task<ActionResult> Delete(string userId, Guid id)
    {
        var result = await this
            .GuardOrAnyAsync(userId, AuthRoles.Field, AuthRoles.Admin)
            .ThenAwait(_ => service.Delete(id, userId));
        return this.ReturnUnitNullableResult(result, new EntityNotFound(
            "Habit Not Found", typeof(HabitPrincipal), id.ToString()));
    }
}
