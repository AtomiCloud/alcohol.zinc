// using System.Net.Mime;
// using App.Error.V1;
// using App.Modules.Common;
// using App.StartUp.Services.Auth;
// using App.StartUp.Registry;
// using CSharp_Result;
// using Domain.Configuration;
// using Microsoft.AspNetCore.Authorization;
// using Microsoft.AspNetCore.Mvc;
// using Asp.Versioning;
//
// namespace App.Modules.Configurations.API.V1;
//
// [ApiVersion(1.0)]
// [ApiController]
// [Consumes(MediaTypeNames.Application.Json)]
// [Route("api/v{version:apiVersion}/[controller]")]
// public class ConfigurationController(
//     IConfigurationService service,
//     IAuthHelper authHelper
// ) : AtomiControllerBase(authHelper)
// {
//     [Authorize, HttpGet("{userId}")]
//     public async Task<ActionResult<ConfigurationModel>> Get(string userId)
//     {
//         var result = await this
//             .GuardOrAnyAsync(userId, AuthRoles.Field, AuthRoles.Admin)
//             .ThenAwait(_ => service.Get(userId));
//         return this.ReturnNullableResult(result, new EntityNotFound(
//             "Configuration Not Found", typeof(ConfigurationModel), userId));
//     }
//
//     [Authorize, HttpPost("{userId}")]
//     public async Task<ActionResult<ConfigurationModel>> Create(string userId, [FromBody] ConfigurationModel model)
//     {
//         if (userId != model.Sub)
//             return BadRequest("UserId in route and body must match.");
//
//         var result = await this
//             .GuardOrAnyAsync(userId, AuthRoles.Field, AuthRoles.Admin)
//             .ThenAwait(_ => service.Create(model));
//         return this.ReturnResult(result);
//     }
//
//     [Authorize, HttpPut("{userId}")]
//     public async Task<ActionResult<ConfigurationModel>> Update(string userId, [FromBody] ConfigurationModel model)
//     {
//         if (userId != model.Sub)
//             return BadRequest("UserId in route and body must match.");
//
//         var result = await this
//             .GuardOrAnyAsync(userId, AuthRoles.Field, AuthRoles.Admin)
//             .ThenAwait(_ => service.Update(model));
//         return this.ReturnNullableResult(result, new EntityNotFound(
//             "Configuration Not Found", typeof(ConfigurationModel), userId));
//     }
//
//     [Authorize, HttpDelete("{userId}")]
//     public async Task<ActionResult> Delete(string userId)
//     {
//         var result = await this
//             .GuardOrAnyAsync(userId, AuthRoles.Field, AuthRoles.Admin)
//             .ThenAwait(_ => service.Delete(userId));
//         return this.ReturnUnitNullableResult(result, new EntityNotFound(
//             "Configuration Not Found", typeof(ConfigurationModel), userId));
//     }
// }
