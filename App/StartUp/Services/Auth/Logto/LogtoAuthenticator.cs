using System.Net.Http.Headers;
using App.Modules.System;
using App.StartUp.Options.Auth;
using App.StartUp.Registry;
using App.Utility;
using CSharp_Result;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using StackExchange.Redis.Extensions.Core.Abstractions;

namespace App.StartUp.Services.Auth.Logto;

public interface ILogtoAuthenticator
{
  Task<Result<string>> BearerToken();
}

public class LogtoAuthenticator(
  IRedisClientFactory factory,
  IHttpClientFactory httpClientsFactory,
  IEncryptor encryptor,
  IMemoryCache localCache,
  IOptions<AuthOption> o,
  ILogger<ILogtoAuthenticator> logger) : ILogtoAuthenticator
{
  private const string LogtoKey = "logto_auth_token";
  private IRedisDatabase Redis => factory.GetRedisClient(Caches.Main).Db0;
  private HttpClient HttpClient => httpClientsFactory.CreateClient(HttpClients.Logto);

  private async Task<Result<(string, DateTime)>> FetchToken()
  {
    logger.LogInformation("Authenticating with Logto");
    if (!o.Value.Enabled || o.Value.Management == null)
      throw new ApplicationException("Using Auth without configuring management configuration");
    var m = o.Value.Management;
    try
    {
      var request = new HttpRequestMessage
      {
        Method = HttpMethod.Post,
        RequestUri = new Uri("oidc/token", UriKind.Relative),
        Headers = { Authorization = new AuthenticationHeaderValue("Basic", $"{m.Id}:{m.Secret}".ToBase64()) },
        Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
          { "grant_type", "client_credentials" }, { "resource", m.Resource }, { "scope", m.Scope },
        })
      };
      var start = DateTime.Now;
      logger.LogTrace("Starting HTTP request with Logto to get Access Token");
      using var response = await this.HttpClient.SendAsync(request);

      response.EnsureSuccessStatusCode();
      var body = await response.Content.ReadAsStringAsync();
      var r = body.ToObj<LogtoTokenRes>();
      logger.LogTrace("Received Access Token from Logto");
      var expiry = start.AddSeconds(r.ExpiresIn - 10);
      logger.LogTrace("Access Token expires at {Expiry}", expiry);
      return (r.AccessToken, expiry);
    }
    catch (HttpRequestException e)
    {
      logger.LogError(e, "Failed to authenticate with Logto (HTTP Error)");
      return e;
    }
    catch (Exception e)
    {
      logger.LogError(e, "Failed to authenticate with Logto");
      throw;
    }
  }

  private async Task<Result<(string, DateTime)?>> Recall()
  {
    logger.LogTrace("Checking for local cached Access Token");
    localCache.TryGetValue(LogtoKey, out LogtoAuthenticatorToken? token);
    if (token is null || token.Expiry <= DateTime.Now)
    {
      logger.LogTrace("Local cached Access Token missing or expired, checking Redis for cached Access Token");
      token = await this.Redis.GetAsync<LogtoAuthenticatorToken>(LogtoKey);
      if (token is null || token.Expiry <= DateTime.Now)
      {
        logger.LogTrace("Redis cached Access Token missing or expired");
        return ((string, DateTime)?)null;
      }

      logger.LogTrace("Redis cached Access Token found, updating local cache");
      localCache.Set(LogtoKey, token);
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
    var model = new LogtoAuthenticatorToken(tokenCipher, expiry);

    localCache.Set(LogtoKey, model);
    logger.LogTrace("Access Token stored in local cache");
    await this.Redis.AddAsync(LogtoKey, model);
    logger.LogTrace("Access Token stored in Redis cache");
    return new Unit();
  }

  public Task<Result<string>> BearerToken()
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
