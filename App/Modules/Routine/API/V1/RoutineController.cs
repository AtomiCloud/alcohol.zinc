using System.Net.Mime;
using App.Error.V1;
using App.Modules.Common;
using App.StartUp.Registry;
using App.StartUp.Services.Auth;
using App.Utility;
using Asp.Versioning;
using CSharp_Result;
using Domain.Routine;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace App.Modules.Routine.API.V1;

[ApiVersion(1.0)]
[ApiController]
[Consumes(MediaTypeNames.Application.Json)]
[Route("api/v{version:apiVersion}/[controller]")]
public class RoutineController(
  IRoutineService service,
  IAuthHelper authHelper,
  CreateRoutineReqValidator createValidator,
  UpdateRoutineReqValidator updateValidator,
  SearchRoutineQueryValidator searchValidator
) : AtomiControllerBase(authHelper)
{
  [Authorize, HttpGet("{userId}")]
  public async Task<ActionResult<List<RoutineRes>>> Search(string userId, [FromQuery] SearchRoutineQuery query)
  {
    var result = await this.GuardOrAnyAsync(userId, AuthRoles.Field, AuthRoles.Admin)
      .ThenAwait(_ => searchValidator.ValidateAsyncResult(query, "Invalid SearchRoutineQuery"))
      .ThenAwait(_ => service.Search(query.ToDomain(userId)))
      .Then(list => list.Select(r => r.ToRes()).ToList(), Errors.MapNone);

    return this.ReturnResult(result);
  }

  [Authorize, HttpGet("{userId}/{id:guid}")]
  public async Task<ActionResult<RoutineRes>> Get(string userId, Guid id)
  {
    var result = await this.GuardOrAnyAsync(userId, AuthRoles.Field, AuthRoles.Admin)
      .ThenAwait(_ => service.Get(id, userId))
      .Then(r => r?.ToRes(), Errors.MapNone);

    return this.ReturnNullableResult(result,
      new EntityNotFound("Routine Not Found", typeof(RoutinePrincipal), id.ToString()));
  }

  [Authorize, HttpPost("{userId}")]
  public async Task<ActionResult<RoutineRes>> Create(string userId, [FromBody] CreateRoutineReq req)
  {
    var result = await this.GuardOrAnyAsync(userId, AuthRoles.Field, AuthRoles.Admin)
      .ThenAwait(_ => createValidator.ValidateAsyncResult(req, "Invalid CreateRoutineReq"))
      .ThenAwait(r => service.Create(userId, r.ToRecord()))
      .Then(r => r.ToRes(), Errors.MapNone);

    return this.ReturnResult(result);
  }

  [Authorize, HttpPut("{userId}/{id:guid}")]
  public async Task<ActionResult<RoutineRes>> Update(string userId, Guid id, [FromBody] UpdateRoutineReq req)
  {
    var result = await this.GuardOrAnyAsync(userId, AuthRoles.Field, AuthRoles.Admin)
      .ThenAwait(_ => updateValidator.ValidateAsyncResult(req, "Invalid UpdateRoutineReq"))
      // Ownership: only update a routine that belongs to the route's user
      .ThenAwait(_ => service.Get(id, userId))
      .ThenAwait(existing => existing == null
        ? Task.FromResult<Result<RoutinePrincipal?>>((RoutinePrincipal?)null)
        : service.Update(id, req.ToRecord()))
      .Then(r => r?.ToRes(), Errors.MapNone);

    return this.ReturnNullableResult(result,
      new EntityNotFound("Routine Not Found", typeof(RoutinePrincipal), id.ToString()));
  }

  [Authorize, HttpDelete("{userId}/{id:guid}")]
  public async Task<ActionResult> Delete(string userId, Guid id)
  {
    var result = await this.GuardOrAnyAsync(userId, AuthRoles.Field, AuthRoles.Admin)
      .ThenAwait(_ => service.Delete(id, userId));

    return this.ReturnUnitNullableResult(result,
      new EntityNotFound("Routine Not Found", typeof(RoutinePrincipal), id.ToString()));
  }
}
