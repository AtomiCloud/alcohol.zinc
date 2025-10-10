using System.Net.Mime;
using App.Error.V1;
using App.Modules.Common;
using App.StartUp.Services.Auth;
using App.Utility;
using Asp.Versioning;
using CSharp_Result;
using Domain.Entitlement;
using Domain.Vacation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace App.Modules.Vacation.API.V1;

[ApiVersion(1.0)]
[ApiController]
[Consumes(MediaTypeNames.Application.Json)]
[Route("api/v{version:apiVersion}/[controller]")]
  public class VacationController(
  IVacationService vacationService,
  IVacationRepository vacationRepository,
  IEntitlementService entitlementService,
  IAuthHelper authHelper,
  CreateVacationReqValidator createValidator,
  SearchVacationQueryValidator searchValidator
) : AtomiControllerBase(authHelper)
{
  [Authorize, HttpPost("{userId}")]
  public async Task<ActionResult<VacationRes>> Create(string userId, [FromBody] CreateVacationReq req)
  {
    var result = await this.GuardAsync(userId)
      .ThenAwait(_ => createValidator.ValidateAsyncResult(req, "Invalid CreateVacationReq"))
      .Then(r => r.ToRecord(), Errors.MapNone)
      .ThenAwait(rec => entitlementService.EnsureVacationWindowAllowed(userId, rec.StartDate)
        .Then(_ => rec, Errors.MapNone))
      .ThenAwait(rec => vacationService.Create(userId, rec))
      .Then(v => v.ToRes(), Errors.MapNone);

    return this.ReturnResult(result);
  }

  // Enforcement is handled by IVacationService

  [Authorize, HttpGet("{userId}")]
  public async Task<ActionResult<List<VacationRes>>> List(string userId, [FromQuery] SearchVacationQuery query)
  {
    var result = await this.GuardAsync(userId)
      .ThenAwait(_ => searchValidator.ValidateAsyncResult(query, "Invalid SearchVacationQuery"))
      .ThenAwait(_ => vacationService.Search(query.ToDomain(userId)))
      .Then(list => list.Select(v => v.ToRes()).ToList(), Errors.MapNone);

    return this.ReturnResult(result);
  }

  [Authorize, HttpDelete("{userId}/{id:guid}")]
  public async Task<ActionResult> Delete(string userId, Guid id)
  {
    var result = await this.GuardAsync(userId)
      .ThenAwait(_ => vacationService.Delete(userId, id));

    return this.ReturnUnitNullableResult(result, new EntityNotFound("Vacation Not Found", typeof(VacationPrincipal), id.ToString()));
  }

  [Authorize, HttpPatch("{userId}/{id:guid}/end-today")]
  public async Task<ActionResult<VacationRes>> EndToday(string userId, Guid id)
  {
    var result = await this.GuardAsync(userId)
      .ThenAwait(_ => vacationService.EndToday(userId, id))
      .Then(x => x?.ToRes(), Errors.MapNone);

    return this.ReturnNullableResult(result, new EntityNotFound("Vacation Not Found", typeof(VacationPrincipal), id.ToString()));
  }
}
