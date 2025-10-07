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
  IAuthManagement authManagement,
  IAuthHelper h
) : AtomiControllerBase(h)
{
  // New route shape: GET /configuration/{userId}/{id}
  [Authorize, HttpGet("{userId}/{id:guid}")]
  public async Task<ActionResult<ConfigurationRes>> GetByUserAndId(string userId, Guid id)
  {
    var guard = this.GuardOrAny(userId, AuthRoles.Field, AuthRoles.Admin);
    if (!guard.IsSuccess()) return this.ReturnUnitResult(guard);

    var privileged = h.HasAny(HttpContext.User, AuthRoles.Field, AuthRoles.Admin);
    var result = privileged
      ? await service.GetById(id).Then(config => config?.ToRes(), Errors.MapNone)
      : await service.GetById(id, userId).Then(config => config?.ToRes(), Errors.MapNone);

    return this.ReturnNullableResult(result, new EntityNotFound("Configuration Not Found", typeof(Configuration), id.ToString()));
  }

  // Convenience: GET /configuration/me -> configuration for the caller's sub
  [Authorize, HttpGet("me")]
  public async Task<ActionResult<ConfigurationRes>> GetMine()
  {
    var userId = this.Sub();
    if (userId == null)
    {
      Result<ConfigurationRes> error = new Unauthenticated("You are not authenticated").ToException();
      return this.ReturnResult(error);
    }

    var result = await service.GetByUserId(userId).Then(config => config?.ToRes(), Errors.MapNone);
    return this.ReturnNullableResult(result, new EntityNotFound("Configuration Not Found", typeof(Configuration), userId));
  }

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
      ? await service.GetById(id).Then(config => config?.ToRes(), Errors.MapNone)
      : await service.GetById(id, userId).Then(config => config?.ToRes(), Errors.MapNone);
      
    return this.ReturnNullableResult(result, new EntityNotFound("Configuration Not Found", typeof(Configuration), id.ToString()));
  }

  // New route shape: POST /configuration/{userId}
  [Authorize, HttpPost("{userId}")]
  public async Task<ActionResult<ConfigurationPrincipalRes>> CreateForUser(string userId, [FromBody] CreateConfigurationReq req)
  {
    var guard = this.GuardOrAny(userId, AuthRoles.Field, AuthRoles.Admin);
    if (!guard.IsSuccess()) return this.ReturnUnitResult(guard);

    var result = await createConfigurationReqValidator
      .ValidateAsyncResult(req, "Invalid CreateConfigurationReq")
      .ThenAwait(r => service.Create(userId, r.ToRecord(), config => authManagement.SetClaim(config.UserId, LogtoClaims.ConfigurationId, config.Id.ToString())))
      .Then(config => config.ToRes().ToResult());
    return this.ReturnResult(result);
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
      .ThenAwait(r => service.Create(userId, r.ToRecord(), config => authManagement.SetClaim(config.UserId, LogtoClaims.ConfigurationId, config.Id.ToString())))
      .Then(config => config.ToRes().ToResult());
    return this.ReturnResult(result);
  }

  // New route shape: PUT /configuration/{userId}/{id}
  [Authorize, HttpPut("{userId}/{id:guid}")]
  public async Task<ActionResult<ConfigurationPrincipalRes>> UpdateForUser(string userId, Guid id, [FromBody] UpdateConfigurationReq req)
  {
    var guard = this.GuardOrAny(userId, AuthRoles.Field, AuthRoles.Admin);
    if (!guard.IsSuccess()) return this.ReturnUnitResult(guard);

    var result = await updateConfigurationReqValidator
      .ValidateAsyncResult(req, "Invalid UpdateConfigurationReq")
      .ThenAwait(r => service.Update(id, userId, r.ToRecord()))
      .Then(config => (config?.ToRes()), Errors.MapNone);
    return this.ReturnNullableResult(result, new EntityNotFound("Configuration Not Found", typeof(ConfigurationPrincipal), id.ToString()));
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
      .Then(config => (config?.ToRes()), Errors.MapNone);
    return this.ReturnNullableResult(result, new EntityNotFound("Configuration Not Found", typeof(ConfigurationPrincipal), id.ToString()));
  }

}
