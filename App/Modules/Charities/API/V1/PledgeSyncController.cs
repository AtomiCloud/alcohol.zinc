using System.Net.Mime;
using App.Modules.Charities.Sync;
using App.Modules.Common;
using App.StartUp.Registry;
using App.StartUp.Services.Auth;
using Asp.Versioning;
using CSharp_Result;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace App.Modules.Charities.API.V1;

[ApiVersion(1.0)]
[ApiController]
[Consumes(MediaTypeNames.Application.Json)]
[Route("api/v{version:apiVersion}/charities/sync")]
[Authorize(Policy = AuthPolicies.OnlyAdmin)]
public class PledgeSyncController(IPledgeSyncService sync, IAuthHelper h) : AtomiControllerBase(h)
{
  public record PledgeSyncReq(int? MaxPages, int? PageSize, string[]? Countries, DateTimeOffset? UpdatedSince);
  public record PledgeSyncSummaryRes(int CausesUpserted, int CharitiesCreated, int CharitiesUpdated, int ExternalIdsLinked, int CharitiesProcessed);

  [HttpPost("pledge")]
  public async Task<ActionResult<PledgeSyncSummaryRes>> Sync([FromBody] PledgeSyncReq req)
  {
    var r = await sync.Sync(new PledgeSyncRequest(
      MaxPages: req.MaxPages ?? int.MaxValue,
      PageSize: req.PageSize ?? 100,
      Countries: req.Countries,
      UpdatedSince: req.UpdatedSince));
    var m = r.Then(x => new PledgeSyncSummaryRes(x.CausesUpserted, x.CharitiesCreated, x.CharitiesUpdated, x.ExternalIdsLinked, x.CharitiesProcessed), Errors.MapNone);
    return this.ReturnResult(m);
  }
}
