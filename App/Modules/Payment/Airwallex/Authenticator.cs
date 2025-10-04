using App.StartUp.Options;
using App.StartUp.Registry;
using App.Utility;
using CSharp_Result;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace App.Modules.Payment.Airwallex;

public interface IAirwallexAuthenticator
{
  Task<Result<string>> GetToken();
  Task<Result<string>> RefreshTokenAsync();
}

public class AirwallexAuthenticator(
  IHttpClientFactory httpClientsFactory,
  IMemoryCache localCache,
  ILogger<AirwallexAuthenticator> logger,
  IOptions<PaymentOption> options
) : IAirwallexAuthenticator
{
  private const string AirwallexTokenKey = "airwallex_auth_token";
  private const string AirwallexTokenExpiryKey = "airwallex_token_expiry";
  private HttpClient HttpClient => httpClientsFactory.CreateClient(HttpClients.Airwallex);

  public async Task<Result<string>> GetToken()
  {
    // Check if we have a valid cached token
    if (localCache.TryGetValue(AirwallexTokenKey, out string? cachedToken) &&
        localCache.TryGetValue(AirwallexTokenExpiryKey, out DateTime expiry) &&
        DateTime.UtcNow < expiry.AddMinutes(-5)) // 5 minute buffer
    {
      return cachedToken!;
    }

    // Get fresh token and extract just the token
    return await GetFreshToken()
      .Then(result => result.Item1, Errors.MapNone);
  }

  private async Task<Result<(string, DateTime)>> GetFreshToken()
  {
    try
    {
      var request = new HttpRequestMessage
      {
        Method = HttpMethod.Post,
        RequestUri = new Uri("api/v1/authentication/login", UriKind.Relative),
        Headers =
        {
          { "x-client-id", options.Value.Airwallex.ClientId },
          { "x-api-key", options.Value.Airwallex.ApiKey },
        },
      };

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
      var expiresAt = DateTime.Parse(authResponse.ExpiresAt);

      return (authResponse.Token, expiresAt);
    }
    catch (Exception e)
    {
      logger.LogError(e, "Failed to get fresh token from Airwallex");
      return e;
    }
  }

  public async Task<Result<string>> RefreshTokenAsync()
  {
    return await GetFreshToken()
      .Then(result =>
      {
        var (token, expiresAt) = result;

        // Cache the token with expiry
        localCache.Set(AirwallexTokenKey, token, expiresAt.AddMinutes(-5));
        localCache.Set(AirwallexTokenExpiryKey, expiresAt);

        return token;
      }, Errors.MapNone);
  }
}