using System.Net.Mime;
using App.Error.V1;
using App.Modules.Common;
using App.StartUp.Registry;
using App.StartUp.Services.Auth;
using App.Utility;
using Asp.Versioning;
using CSharp_Result;
using Domain.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace App.Modules.Configurations.API.V1;

[ApiVersion(1.0)]
[ApiController]
[Consumes(MediaTypeNames.Application.Json)]
[Route("api/v{version:apiVersion}/[controller]")]
public class ConfigurationController(
  IConfigurationService service,
  CreateConfigurationReqValidator createConfigurationReqValidator,
  UpdateConfigurationReqValidator updateConfigurationReqValidator,
  IAuthHelper h
) : AtomiControllerBase(h)
{
  [Authorize, HttpGet("{id:guid}")]
  public async Task<ActionResult<ConfigurationRes>> GetById(Guid id)
  {
    var userId = this.Sub();
    if (userId == null)
    {
      Result<ConfigurationRes> error = new Unauthenticated("You are not authenticated").ToException();
      return this.ReturnResult(error);
    }

    // Admin can access any configuration, regular users only their own
    var result = h.HasAny(HttpContext.User, AuthRoles.Field, AuthRoles.Admin) 
      ? await service.GetById(id).Then(config => config?.ToRes(), Errors.MapAll)
      : await service.GetById(id, userId).Then(config => config?.ToRes(), Errors.MapAll);
      
    return this.ReturnNullableResult(result, new EntityNotFound("Configuration Not Found", typeof(Configuration), id.ToString()));
  }

  [Authorize, HttpPost]
  public async Task<ActionResult<ConfigurationPrincipalRes>> Create([FromBody] CreateConfigurationReq req)
  {
    var userId = this.Sub();
    if (userId == null)
    {
      Result<ConfigurationPrincipalRes> error = new Unauthenticated("You are not authenticated").ToException();
      return this.ReturnResult(error);
    }

    var result = await createConfigurationReqValidator
      .ValidateAsyncResult(req, "Invalid CreateConfigurationReq")
      .ThenAwait(r => service.Create(userId, r.ToRecord()))
      .Then(config => config.ToRes().ToResult());
    return this.ReturnResult(result);
  }

  [Authorize, HttpPut("{id:guid}")]
  public async Task<ActionResult<ConfigurationPrincipalRes>> Update(Guid id, [FromBody] UpdateConfigurationReq req)
  {
    var userId = this.Sub();
    if (userId == null)
    {
      Result<ConfigurationPrincipalRes> error = new Unauthenticated("You are not authenticated").ToException();
      return this.ReturnResult(error);
    }

    var result = await updateConfigurationReqValidator
      .ValidateAsyncResult(req, "Invalid UpdateConfigurationReq")
      .ThenAwait(r => service.Update(id, userId, r.ToRecord()))
      .Then(config => (config?.ToRes()).ToResult());
    return this.ReturnNullableResult(result, new EntityNotFound("Configuration Not Found", typeof(ConfigurationPrincipal), id.ToString()));
  }

  [Authorize, HttpDelete]
  public async Task<ActionResult<Unit>> Delete()
  {
    var userId = this.Sub();
    if (userId == null)
    {
      Result<Unit> error = new Unauthenticated("You are not authenticated").ToException();
      return this.ReturnResult(error);
    }

    var result = await service.Delete(userId)
      .Then(unit => unit != null ? new Unit().ToResult() : 
        new EntityNotFound("Configuration Not Found", typeof(Configuration), userId).ToException());
    return this.ReturnNullableResult(result, new EntityNotFound("Configuration Not Found", typeof(Configuration), userId));
  }
}
