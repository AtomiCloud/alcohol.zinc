using System.Net.Mime;
using App.Error.V1;
using App.Modules.Charities.Sync;
using App.Modules.Common;
using App.StartUp.Registry;
using App.StartUp.Services.Auth;
using App.Utility;
using Asp.Versioning;
using CSharp_Result;
using Domain.Charity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace App.Modules.Charities.API.V1;

[ApiVersion(1.0)]
[ApiController]
[Consumes(MediaTypeNames.Application.Json)]
[Route("api/v{version:apiVersion}/[controller]")]
public class CharityController(
  ICharityService service,
  CreateCharityReqValidator createCharityReqValidator,
  UpdateCharityReqValidator updateCharityReqValidator,
  CharitySearchReqValidator charitySearchReqValidator,
  SetCharityCausesReqValidator setCharityCausesReqValidator,
  IAuthHelper h
) : AtomiControllerBase(h)
{
  [HttpGet]
  public async Task<ActionResult<IEnumerable<CharityPrincipalRes>>> Search([FromQuery] CharitySearchReq req)
  {
    var result = await charitySearchReqValidator
      .ValidateAsyncResult(req, "Invalid CharitySearchReq")
      .ThenAwait(r => service.Search(r.ToDomain()))
      .Then(charities => charities.Select(c => c.ToRes()), Errors.MapNone);
    return this.ReturnResult(result);
  }

  [HttpGet("{id:guid}")]
  public async Task<ActionResult<CharityRes>> GetById(Guid id)
  {
    var result = await service.Get(id)
      .Then(charity => charity?.ToRes(), Errors.MapNone);
    return this.ReturnNullableResult(result, new EntityNotFound("Charity Not Found", typeof(Charity), id.ToString()));
  }

  [Authorize(Policy = AuthPolicies.OnlyAdmin)]
  [HttpPut("{id:guid}/causes")]
  public async Task<ActionResult> SetCauses(Guid id, [FromBody] SetCharityCausesReq req)
  {
    var result = await setCharityCausesReqValidator
      .ValidateAsyncResult(req, "Invalid SetCharityCausesReq")
      .ThenAwait(r => service.SetCauses(id, r.Keys))
      .Then(_ => (Unit?)new Unit(), Errors.MapNone);
    return this.ReturnUnitNullableResult(result, new EntityNotFound("Charity Not Found", typeof(Charity), id.ToString()));
  }

  [Authorize(Policy = AuthPolicies.OnlyAdmin), HttpPost]
  public async Task<ActionResult<CharityPrincipalRes>> Create([FromBody] CreateCharityReq req)
  {
    var result = await createCharityReqValidator
      .ValidateAsyncResult(req, "Invalid CreateCharityReq")
      .ThenAwait(r => service.Create(r.ToRecord()))
      .Then(charity => charity.ToRes(), Errors.MapNone);
    return this.ReturnResult(result);
  }

  [Authorize(Policy = AuthPolicies.OnlyAdmin), HttpPut("{id:guid}")]
  public async Task<ActionResult<CharityPrincipalRes>> Update(Guid id, [FromBody] UpdateCharityReq req)
  {
    var result = await updateCharityReqValidator
      .ValidateAsyncResult(req, "Invalid UpdateCharityReq")
      .ThenAwait(r => service.Update(id, r.ToRecord()))
      .Then(charity => (charity?.ToRes()), Errors.MapNone);
    return this.ReturnNullableResult(result, new EntityNotFound("Charity Not Found", typeof(CharityPrincipal), id.ToString()));
  }

  [Authorize(Policy = AuthPolicies.OnlyAdmin), HttpDelete("{id:guid}")]
  public async Task<ActionResult> Delete(Guid id)
  {
    var result = await service.Delete(id);
    
    return this.ReturnUnitNullableResult(result, new EntityNotFound("Charity Not Found", typeof(Charity), id.ToString()));
  }

  [HttpGet("supported-countries")]
  public ActionResult<IEnumerable<string>> GetSupportedCountries()
  {
    return this.Ok(PledgeSyncService.SupportedCountries.AsEnumerable());
  }
}
