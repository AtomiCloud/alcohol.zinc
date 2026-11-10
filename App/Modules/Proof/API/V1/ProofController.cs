using System.Net.Mime;
using App.Error.V1;
using App.Modules.Common;
using App.StartUp.Services.Auth;
using App.Utility;
using Asp.Versioning;
using CSharp_Result;
using Domain.Proof;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace App.Modules.Proof.API.V1;

[ApiVersion(1.0)]
[ApiController]
[Consumes(MediaTypeNames.Application.Json)]
[Route("api/v{version:apiVersion}/[controller]")]
public class ProofController(
  IProofService service,
  IAuthHelper authHelper,
  BindProofReqValidator bindValidator
) : AtomiControllerBase(authHelper)
{
  // The proof plugins this server offers; the app renders a binding form from SettingsSchema.
  [Authorize, HttpGet("plugins")]
  public ActionResult<List<PluginManifestRes>> ListPlugins() =>
    this.Ok(service.ListPlugins().Select(m => m.ToRes()).ToList());

  [Authorize, HttpGet("{userId}/{habitId:guid}")]
  public async Task<ActionResult<ProofBindingRes>> Get(string userId, Guid habitId)
  {
    var result = await this.GuardAsync(userId)
      .ThenAwait(_ => service.GetBinding(userId, habitId))
      .Then(b => b?.ToRes() ?? new ProofBindingRes(habitId, ManualProofPlugin.Key, ManualProofPlugin.Version, "{}"), Errors.MapNone);

    return this.ReturnResult(result);
  }

  [Authorize, HttpPut("{userId}/{habitId:guid}")]
  public async Task<ActionResult<ProofBindingRes>> Bind(string userId, Guid habitId, [FromBody] BindProofReq req)
  {
    var result = await this.GuardAsync(userId)
      .ThenAwait(_ => bindValidator.ValidateAsyncResult(req, "Invalid BindProofReq"))
      .ThenAwait(r => service.Bind(userId, habitId, r.PluginKey, r.Settings))
      .Then(b => b.ToRes(), Errors.MapNone);

    return this.ReturnResult(result);
  }

  // Back to manual only.
  [Authorize, HttpDelete("{userId}/{habitId:guid}")]
  public async Task<ActionResult> Unbind(string userId, Guid habitId)
  {
    var result = await this.GuardAsync(userId)
      .ThenAwait(_ => service.Unbind(userId, habitId));

    return this.ReturnUnitNullableResult(result,
      new EntityNotFound("Proof Binding Not Found", typeof(ProofBindingPrincipal), habitId.ToString()));
  }
}
