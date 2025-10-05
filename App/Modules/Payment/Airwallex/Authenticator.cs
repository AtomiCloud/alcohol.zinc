using App.Modules.System;
using App.StartUp.Options;
using App.StartUp.Registry;
using App.Utility;
using CSharp_Result;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using StackExchange.Redis.Extensions.Core.Abstractions;

namespace App.Modules.Payment.Airwallex;

public interface IAirwallexAuthenticator
{
  Task<Result<string>> GetToken();
}

public record AirwallexAuthenticatorToken(string Secret, DateTime Expiry);

public class AirwallexAuthenticator(
  IRedisClientFactory factory,
  IHttpClientFactory httpClientsFactory,
  IEncryptor encryptor,
  IMemoryCache localCache,
  ILogger<AirwallexAuthenticator> logger,
  IOptions<PaymentOption> options
) : IAirwallexAuthenticator
{
  private const string AirwallexKey = "airwallex_auth_token";
  private IRedisDatabase Redis => factory.GetRedisClient(Caches.Main).Db0;
  private HttpClient HttpClient => httpClientsFactory.CreateClient(HttpClients.Airwallex);

  private async Task<Result<(string, DateTime)>> FetchToken()
  {
    logger.LogInformation("Authenticating with Airwallex");
    try
    {
      var request = new HttpRequestMessage
      {
        Method = HttpMethod.Post,
        RequestUri = new Uri("api/v1/authentication/login", UriKind.Relative),
        Headers =
        {
          { "x-client-id", options.Value.Airwallex.Id },
          { "x-api-key", options.Value.Airwallex.Key },
        },
      };

      logger.LogTrace("Starting HTTP request with Airwallex to get Access Token");
      using var response = await HttpClient.SendAsync(request);
      var body = await response.Content.ReadAsStringAsync();

      try
      {
        response.EnsureSuccessStatusCode();
      }
      catch (HttpRequestException e)
      {
        logger.LogError(
          e,
          "Failed to authenticate with Airwallex (HTTP Error), Response: {Body}",
          body
        );
        return e;
      }
      catch (Exception e)
      {
        logger.LogError(e, "Failed to authenticate with Airwallex");
        throw;
      }

      var authResponse = body.ToObj<AirwallexAuthTokenRes>();
      logger.LogTrace("Received Access Token from Airwallex");
      var expiresAt = DateTime.Parse(authResponse.ExpiresAt);
      logger.LogTrace("Access Token expires at {Expiry}", expiresAt);

      return (authResponse.Token, expiresAt);
    }
    catch (HttpRequestException e)
    {
      logger.LogError(e, "Failed to authenticate with Airwallex (HTTP Error)");
      return e;
    }
    catch (Exception e)
    {
      logger.LogError(e, "Failed to get fresh token from Airwallex");
      throw;
    }
  }

  private async Task<Result<(string, DateTime)?>> Recall()
  {
    logger.LogInformation("Checking for local cached Access Token");
    localCache.TryGetValue(AirwallexKey, out AirwallexAuthenticatorToken? token);
    if (token is null || token.Expiry <= DateTime.Now)
    {
      logger.LogInformation("Local cached Access Token missing or expired, checking Redis for cached Access Token");
      token = await this.Redis.GetAsync<AirwallexAuthenticatorToken>(AirwallexKey);
      if (token is null || token.Expiry <= DateTime.Now)
      {
        logger.LogInformation("Redis cached Access Token missing or expired");
        return ((string, DateTime)?)null;
      }

      logger.LogTrace("Redis cached Access Token found, updating local cache");
      localCache.Set(AirwallexKey, token);
    }

    logger.LogTrace("Local or Redis cached Access Token found");
    var d = encryptor.Decrypt(token.Secret);
    return (d, token.Expiry);
  }

  private async Task<Result<Unit>> Remember(string token, DateTime expiry)
  {
    logger.LogTrace("Updating local and Redis cached Access Token");
    logger.LogTrace("Local cached Access Token will expire at {Expiry} (UTC)", expiry);
    logger.LogTrace("Encrypting Access Token before storage");
    var tokenCipher = encryptor.Encrypt(token);
    logger.LogTrace("Access Token encrypted");
    var model = new AirwallexAuthenticatorToken(tokenCipher, expiry);

    localCache.Set(AirwallexKey, model);
    logger.LogTrace("Access Token stored in local cache");
    await this.Redis.AddAsync(AirwallexKey, model);
    logger.LogTrace("Access Token stored in Redis cache");
    return new Unit();
  }

  public Task<Result<string>> GetToken()
  {
    return this.Recall()
      .ThenAwait(x =>
        x == null
          ? this.FetchToken().DoAwait(DoType.MapErrors, r => this.Remember(r.Item1, r.Item2))
          : Task.FromResult(new Result<(string, DateTime)>((x.Value.Item1, x.Value.Item2)))
      )
      .Then(x => x.Item1, Errors.MapNone);
  }
}
