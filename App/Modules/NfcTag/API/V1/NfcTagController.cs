using System.Net.Mime;
using System.Text.RegularExpressions;
using App.Error.V1;
using App.Modules.Common;
using App.StartUp.Services.Auth;
using App.Utility;
using Asp.Versioning;
using CSharp_Result;
using Domain.NfcTag;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace App.Modules.NfcTag.API.V1;

[ApiVersion(1.0)]
[ApiController]
[Consumes(MediaTypeNames.Application.Json)]
[Route("api/v{version:apiVersion}/[controller]")]
public partial class NfcTagController(
  INfcTagService nfcTagService,
  IAuthHelper authHelper,
  LinkNfcTagReqValidator linkValidator
) : AtomiControllerBase(authHelper)
{
  // Tag ids are client-generated (UUID-ish) strings embedded in the tag URL.
  [GeneratedRegex("^[A-Za-z0-9-]{8,64}$")]
  private static partial Regex TagIdRegex();

  [Authorize, HttpPut("{userId}/{tagId}")]
  public async Task<ActionResult<NfcTagRes>> Link(string userId, string tagId, [FromBody] LinkNfcTagReq req)
  {
    var result = await this.GuardAsync(userId)
      .ThenAwait(_ => ValidateTagId(tagId))
      .ThenAwait(_ => linkValidator.ValidateAsyncResult(req, "Invalid LinkNfcTagReq"))
      .ThenAwait(r => nfcTagService.Link(userId, tagId, r.HabitId))
      .Then(t => t.ToRes(), Errors.MapNone);

    return this.ReturnResult(result);
  }

  [Authorize, HttpGet("{userId}/{tagId}")]
  public async Task<ActionResult<NfcTagResolutionRes>> Resolve(string userId, string tagId)
  {
    var result = await this.GuardAsync(userId)
      .ThenAwait(_ => ValidateTagId(tagId))
      .ThenAwait(_ => nfcTagService.Resolve(userId, tagId))
      .Then(r => r?.ToRes(), Errors.MapNone);

    return this.ReturnNullableResult(result, new EntityNotFound("NFC Tag Not Found", typeof(NfcTagPrincipal), tagId));
  }

  [Authorize, HttpDelete("{userId}/{tagId}")]
  public async Task<ActionResult> Unlink(string userId, string tagId)
  {
    var result = await this.GuardAsync(userId)
      .ThenAwait(_ => ValidateTagId(tagId))
      .ThenAwait(_ => nfcTagService.Unlink(userId, tagId));

    return this.ReturnUnitNullableResult(result, new EntityNotFound("NFC Tag Not Found", typeof(NfcTagPrincipal), tagId));
  }

  private static Task<Result<Unit>> ValidateTagId(string tagId)
  {
    if (!TagIdRegex().IsMatch(tagId))
    {
      return Task.FromResult((Result<Unit>)new ValidationError(
        "Invalid NFC tag id",
        new Dictionary<string, string[]> { ["TagId"] = ["Tag id must be 8-64 characters of letters, digits or hyphens"] }
      ).ToException());
    }
    return Task.FromResult((Result<Unit>)new Unit());
  }
}
