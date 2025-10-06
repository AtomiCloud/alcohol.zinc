using System.Net.Mime;
using App.Error.V1;
using App.Modules.Common;
using App.StartUp.Registry;
using App.StartUp.Services.Auth;
using App.Utility;
using Asp.Versioning;
using CSharp_Result;
using Domain.Cause;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace App.Modules.Causes.API.V1;

[ApiVersion(1.0)]
[ApiController]
[Consumes(MediaTypeNames.Application.Json)]
[Route("api/v{version:apiVersion}/[controller]")]
public class CausesController(
  ICauseService service,
  CreateCauseReqValidator createValidator,
  UpdateCauseReqValidator updateValidator,
  CauseSearchReqValidator searchValidator,
  IAuthHelper h
) : AtomiControllerBase(h)
{
  
  [HttpGet]
  public async Task<ActionResult<IEnumerable<CausePrincipalRes>>> Search([FromQuery] CauseSearchReq req)
  {
    var result = await searchValidator
      .ValidateAsyncResult(req, "Invalid CauseSearchReq")
      .ThenAwait(r => service.Search(r.ToDomain()))
      .Then(causes => causes.Select(c => c.ToRes()), Errors.MapNone);
    return this.ReturnResult(result);
  }

  [HttpGet("{id:guid}")]
  public async Task<ActionResult<CauseRes>> Get(Guid id)
  {
    var result = await service.Get(id)
      .Then(c => c?.ToRes(), Errors.MapNone);
    return this.ReturnNullableResult(result, new EntityNotFound("Cause Not Found", typeof(Cause), id.ToString()));
  }

  [HttpPost]
  [Authorize(Policy = AuthPolicies.OnlyAdmin)]
  public async Task<ActionResult<CausePrincipalRes>> Create([FromBody] CreateCauseReq req)
  {
    var result = await createValidator
      .ValidateAsyncResult(req, "Invalid CreateCauseReq")
      .ThenAwait(r => service.Create(r.ToRecord()))
      .Then(c => c.ToRes(), Errors.MapNone);
    return this.ReturnResult(result);
  }

  [HttpPut("{id:guid}")]
  [Authorize(Policy = AuthPolicies.OnlyAdmin)]
  public async Task<ActionResult<CausePrincipalRes>> Update(Guid id, [FromBody] UpdateCauseReq req)
  {
    var result = await updateValidator
      .ValidateAsyncResult(req, "Invalid UpdateCauseReq")
      .ThenAwait(r => service.Update(id, r.ToRecord()))
      .Then(c => c?.ToRes(), Errors.MapNone);
    return this.ReturnNullableResult(result, new EntityNotFound("Cause Not Found", typeof(CausePrincipal), id.ToString()));
  }

  [HttpDelete("{id:guid}")]
  [Authorize(Policy = AuthPolicies.OnlyAdmin)]
  public async Task<ActionResult> Delete(Guid id)
  {
    var result = await service.Delete(id);
    return this.ReturnUnitNullableResult(result, new EntityNotFound("Cause Not Found", typeof(Cause), id.ToString()));
  }
}

