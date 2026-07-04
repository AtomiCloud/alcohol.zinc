using System.Net.Mime;
using App.Modules.Common;
using App.StartUp.Services.Auth;
using App.Utility;
using Asp.Versioning;
using CSharp_Result;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace App.Modules.Auth.API.V1;

[ApiVersion(1.0)]
[ApiController]
[Consumes(MediaTypeNames.Application.Json)]
[Route("api/v{version:apiVersion}/[controller]")]
public class AuthController(
  IWebHandoffService webHandoffService,
  IAuthHelper authHelper,
  WebHandoffReqValidator webHandoffValidator
) : AtomiControllerBase(authHelper)
{
  // Mints a Logto one-time token for the CALLER (identity comes exclusively
  // from the JWT sub — there is no way to request another user's token) and
  // returns the magic-link URL into the web billing portal.
  //
  // platform/storefront are client-asserted, so the neutral-region 403 is a
  // best-effort compliance backstop, not a security boundary: the token is
  // only ever minted for the caller's own account either way.
  [Authorize, HttpPost("web-handoff")]
  public async Task<ActionResult<WebHandoffRes>> WebHandoff([FromBody] WebHandoffReq req)
  {
    var sub = this.Sub();
    var result = await this.GuardAsync(sub)
      .ThenAwait(_ => webHandoffValidator.ValidateAsyncResult(req, "Invalid WebHandoffReq"))
      .ThenAwait(r => webHandoffService.CreateHandoff(sub!, r.Platform, r.Storefront))
      .Then(h => new WebHandoffRes(h.Url, h.ExpiresInSeconds), Errors.MapNone);

    return this.ReturnResult(result);
  }
}
