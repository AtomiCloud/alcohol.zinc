using System.Net.Mime;
using App.Modules.Common;
using App.StartUp.Options;
using App.StartUp.Registry;
using App.StartUp.Services.Auth;
using Asp.Versioning;
using CSharp_Result;
using Domain.Disbursement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace App.Modules.Disbursement.API.V1;

// Admin-only manual trigger for the charity payout pass. The worker normally runs on a daily
// timer; this endpoint runs one pass on demand (reconcile + claim + donate) using the configured
// donor + thresholds — used for ops and end-to-end testing against the Pledge sandbox.
[ApiVersion(1.0)]
[ApiController]
[Consumes(MediaTypeNames.Application.Json)]
[Route("api/v{version:apiVersion}/disbursement")]
[Authorize(Policy = AuthPolicies.OnlyAdmin)]
public class DisbursementController(
  IDisbursementService service,
  IOptionsMonitor<DisbursementOption> options,
  IAuthHelper h) : AtomiControllerBase(h)
{
  public record RunDisbursementRes(int Completed);

  [HttpPost("run")]
  public async Task<ActionResult<RunDisbursementRes>> Run()
  {
    var opt = options.CurrentValue;
    var donor = new DonorIdentity
    {
      FirstName = opt.DonorFirstName,
      LastName = opt.DonorLastName,
      Email = opt.DonorEmail
    };
    var r = await service.ProcessPending(donor, opt.MinPayoutCents, opt.MaxGroupsPerRun)
      .Then(count => new RunDisbursementRes(count), Errors.MapNone);
    return this.ReturnResult(r);
  }
}
