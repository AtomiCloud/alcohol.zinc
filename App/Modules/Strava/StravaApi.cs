using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using App.StartUp.Options;
using App.StartUp.Registry;
using CSharp_Result;
using Domain.Strava;
using Microsoft.Extensions.Options;

namespace App.Modules.Strava;

// Strava's OAuth and activity endpoints (developers.strava.com/docs/authentication, /reference).
public class StravaApi(
  IHttpClientFactory factory,
  IOptionsMonitor<StravaOption> options,
  ILogger<StravaApi> logger
) : IStravaApi
{
  private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

  private HttpClient Http => factory.CreateClient(HttpClients.Strava);

  private record TokenRes(string AccessToken, string RefreshToken, long ExpiresAt, AthleteRes? Athlete);

  private record AthleteRes(long Id);

  private record ActivityRes(string SportType, DateTime StartDate, bool Manual);

  public async Task<Result<StravaGrant>> ExchangeCode(string code)
  {
    var res = await Token(new() { ["grant_type"] = "authorization_code", ["code"] = code });
    if (res.IsFailure()) return res.FailureOrDefault();
    if (res.Get() is not { Athlete: { } athlete } t)
      return new HttpRequestException("Strava rejected the authorization code");
    return new StravaGrant(athlete.Id, ToTokens(t));
  }

  public async Task<Result<StravaTokens?>> Refresh(string refreshToken)
  {
    var res = await Token(new() { ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken });
    if (res.IsFailure()) return res.FailureOrDefault();
    return res.Get() is { } t ? ToTokens(t) : null;
  }

  public Task<Result<StravaActivity?>> GetActivity(string accessToken, long activityId) =>
    Send<StravaActivity?>("GetActivity", () => new HttpRequestMessage(HttpMethod.Get, $"api/v3/activities/{activityId}")
    {
      Headers = { Authorization = new AuthenticationHeaderValue("Bearer", accessToken) }
    }, async response =>
    {
      // Deleted, or no longer visible to this token (private without read_all, deauthorized).
      if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        return (StravaActivity?)null;
      response.EnsureSuccessStatusCode();
      var a = await response.Content.ReadFromJsonAsync<ActivityRes>(Json);
      return a == null ? null : new StravaActivity { SportType = a.SportType, StartDate = a.StartDate, Manual = a.Manual };
    });

  public Task<Result<Unit>> Revoke(string token) =>
    Send("Revoke", () =>
    {
      var o = options.CurrentValue;
      var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{o.ClientId}:{o.ClientSecret}"));
      return new HttpRequestMessage(HttpMethod.Post, "oauth/revoke")
      {
        Headers = { Authorization = new AuthenticationHeaderValue("Basic", basic) },
        Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token })
      };
    }, response =>
    {
      response.EnsureSuccessStatusCode(); // 200 whether or not the token was found
      return Task.FromResult(new Unit());
    });

  // Null when Strava refuses the grant (400/401: bad code or refresh token).
  private Task<Result<TokenRes?>> Token(Dictionary<string, string> form) =>
    Send("Token", () =>
    {
      var o = options.CurrentValue;
      form["client_id"] = o.ClientId;
      form["client_secret"] = o.ClientSecret;
      return new HttpRequestMessage(HttpMethod.Post, "oauth/token") { Content = new FormUrlEncodedContent(form) };
    }, async response =>
    {
      if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
      {
        logger.LogWarning("Strava token grant refused: {Status} {Body}", (int)response.StatusCode,
          await response.Content.ReadAsStringAsync());
        return (TokenRes?)null;
      }

      response.EnsureSuccessStatusCode();
      return await response.Content.ReadFromJsonAsync<TokenRes>(Json);
    });

  private static StravaTokens ToTokens(TokenRes t) =>
    new(t.AccessToken, t.RefreshToken, DateTimeOffset.FromUnixTimeSeconds(t.ExpiresAt).UtcDateTime);

  // Network errors, timeouts and 429/5xx are failures, so the inbox retries the event.
  private async Task<Result<T>> Send<T>(string op, Func<HttpRequestMessage> request,
    Func<HttpResponseMessage, Task<T>> read)
  {
    try
    {
      using var response = await Http.SendAsync(request());
      return await read(response);
    }
    catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
    {
      logger.LogError(e, "Strava {Op} failed", op);
      return e;
    }
  }
}
