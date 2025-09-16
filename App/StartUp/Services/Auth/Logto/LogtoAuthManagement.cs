using System.Net.Http.Headers;
using System.Text;
using App.StartUp.Registry;
using App.Utility;
using CSharp_Result;

namespace App.StartUp.Services.Auth.Logto;

public class LogtoAuthManagement(
  IHttpClientFactory httpClientsFactory,
  ILogtoAuthenticator authenticator,
  ILogger<IAuthManagement> logger
) : IAuthManagement
{
  private HttpClient HttpClient => httpClientsFactory.CreateClient(HttpClients.Logto);

  public Task<Result<Unit>> AssignRole(string userId, string roleId)
  {
    logger.LogInformation("Assigning role {RoleId} to user {UserId}", roleId, userId);
    return authenticator.BearerToken()
      .ThenAwait(async bearer =>
      {
        try
        {
          var request = new HttpRequestMessage
          {
            Method = HttpMethod.Post,
            RequestUri = new Uri($"api/users/{userId}/roles", UriKind.Relative),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", bearer), },
            Content = new StringContent(new LogtoAssignRoleReq { RoleIds = [roleId] }.ToJson(),
              Encoding.UTF8, "application/json")
          };
          using var response = await this.HttpClient.SendAsync(request);
          response.EnsureSuccessStatusCode();
          logger.LogInformation("Role {RoleId} assigned to user {UserId}", roleId, userId);
          return new Unit();
        }
        catch (Exception e)
        {
          logger.LogError(e, "Failed to assign role {RoleId} to user {UserId}", roleId, userId);
          throw;
        }
      }, Errors.MapNone);
  }

  public Task<Result<Unit>> RemoveRole(string userId, string roleId)
  {
    logger.LogInformation("Removing role {RoleId} from user {UserId}", roleId, userId);
    return authenticator.BearerToken()
      .ThenAwait(async bearer =>
      {
        try
        {
          var request = new HttpRequestMessage
          {
            Method = HttpMethod.Delete,
            RequestUri = new Uri($"api/users/{userId}/roles/{roleId}", UriKind.Relative),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", bearer), },
          };
          using var response = await this.HttpClient.SendAsync(request);
          response.EnsureSuccessStatusCode();
          logger.LogInformation("Role {RoleId} removed from user {UserId}", roleId, userId);
          return new Unit();
        }
        catch (Exception e)
        {
          logger.LogError(e, "Failed to remove role {RoleId} from user {UserId}", roleId, userId);
          throw;
        }
      }, Errors.MapNone);
  }

  public Task<Result<Unit>> SetClaim(string userId, string claimKey, string claimValue)
  {
    logger.LogInformation("Setting claim {ClaimKey} to {ClaimValue} for user {UserId}", claimKey, claimValue, userId);
    return authenticator.BearerToken()
      .ThenAwait(async bearer =>
      {
        try
        {
          var request = new HttpRequestMessage
          {
            Method = HttpMethod.Patch,
            RequestUri = new Uri($"api/users/{userId}/custom-data", UriKind.Relative),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", bearer), },
            Content = new StringContent(
              new ClaimsPatchReq { CustomData = new Dictionary<string, string?> { { claimKey, claimValue } } }.ToJson(),
              Encoding.UTF8, "application/json"),
          };
          using var response = await this.HttpClient.SendAsync(request);
          response.EnsureSuccessStatusCode();
          logger.LogInformation("Successfully set claim {ClaimKey} to {ClaimValue} for user {UserId}", claimKey,
            claimValue, userId);
          return new Unit();
        }
        catch (Exception e)
        {
          logger.LogError(e, "Failed to set claim {ClaimKey} to {ClaimValue} for user {UserId}", claimKey, claimValue,
            userId);
          throw;
        }
      }, Errors.MapNone);
  }

  public Task<Result<Unit>> RemoveClaim(string userId, string claimKey)
  {
    logger.LogInformation("Removing claim {ClaimKey} for user {UserId}", claimKey, userId);
    return authenticator.BearerToken()
      .ThenAwait(async bearer =>
      {
        try
        {
          var request = new HttpRequestMessage
          {
            Method = HttpMethod.Patch,
            RequestUri = new Uri($"api/users/{userId}/custom-data", UriKind.Relative),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", bearer), },
            Content = new StringContent(new ClaimsPatchReq
            {
              CustomData = new Dictionary<string, string?> { { claimKey, null } }
            }.ToJson()) { Headers = { ContentType = new MediaTypeHeaderValue("application/json") } }
          };
          using var response = await this.HttpClient.SendAsync(request);
          response.EnsureSuccessStatusCode();
          logger.LogInformation("Successfully set claim {ClaimKey} for user {UserId}", claimKey, userId);
          return new Unit();
        }
        catch (Exception e)
        {
          logger.LogError(e, "Failed to set claim {ClaimKey} for user {UserId}", claimKey, userId);
          throw;
        }
      }, Errors.MapNone);
  }
}
