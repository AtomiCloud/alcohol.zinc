using System.Text.Json;
using App.Error.V1;
using App.Modules.Common;
using App.StartUp.Options;
using App.StartUp.Services.Auth;
using Asp.Versioning;
using CSharp_Result;
using Domain.Strava;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace App.Modules.Strava.API.V1;

[ApiVersion(1.0)]
[ApiController]
[Route("api/v{version:apiVersion}/[controller]")]
public class StravaController(
  IStravaService service,
  IStravaInboxRepository inbox,
  IOptionsMonitor<StravaOption> options,
  IAuthHelper authHelper,
  ILogger<StravaController> logger
) : AtomiControllerBase(authHelper)
{
  private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(15);

  // "read" is Strava's default profile scope; activity:read_all also sees "Only You" activities.
  private const string RequestedScope = "read," + StravaScopes.Required;

  private StravaOption Opt => options.CurrentValue;

  // The Strava authorize URLs to open: AppUrl in the Strava app when installed, else Url in an
  // in-app browser. Both come back to /callback, which redirects to the app's deep link.
  [Authorize, HttpGet("connect-url/{userId}")]
  public ActionResult<StravaConnectUrlRes> ConnectUrl(string userId)
  {
    if (!Opt.Enabled) return this.NotFound();
    var guard = this.Guard(userId);
    if (guard.IsFailure()) return this.ReturnResult<StravaConnectUrlRes>(guard.FailureOrDefault());

    var query = new Dictionary<string, string?>
    {
      ["client_id"] = Opt.ClientId,
      ["redirect_uri"] = Opt.CallbackUrl,
      ["response_type"] = "code",
      ["approval_prompt"] = "auto",
      ["scope"] = RequestedScope,
      ["state"] = StravaState.Sign(userId, DateTime.UtcNow + StateLifetime, Opt.ClientSecret)
    };
    return this.Ok(new StravaConnectUrlRes(
      QueryHelpers.AddQueryString("https://www.strava.com/oauth/mobile/authorize", query),
      QueryHelpers.AddQueryString("strava://oauth/mobile/authorize", query)));
  }

  // Strava redirects here after the user approves or cancels. Always ends at the app's deep link
  // with ?result=connected|denied|scope_missing|athlete_taken|expired.
  [HttpGet("callback")]
  public async Task<ActionResult> Callback([FromQuery] string? code, [FromQuery] string? scope,
    [FromQuery] string? state, [FromQuery] string? error)
  {
    if (!Opt.Enabled) return this.NotFound();
    var userId = StravaState.Verify(state, DateTime.UtcNow, Opt.ClientSecret);
    if (userId == null) return this.ToApp("expired");
    if (error != null || code == null) return this.ToApp("denied");

    var outcome = await service.Connect(userId, code, scope ?? "");
    if (outcome.IsFailure())
    {
      logger.LogError(outcome.FailureOrDefault(), "Strava connect failed for UserId={UserId}", userId);
      return this.ToApp("error");
    }

    return this.ToApp(outcome.Get() switch
    {
      StravaConnectOutcome.Connected => "connected",
      StravaConnectOutcome.ScopeMissing => "scope_missing",
      StravaConnectOutcome.AthleteTaken => "athlete_taken",
      _ => "error"
    });
  }

  [Authorize, HttpGet("{userId}")]
  public async Task<ActionResult<StravaConnectionRes>> Get(string userId)
  {
    if (!Opt.Enabled) return this.NotFound();
    var result = await this.GuardAsync(userId)
      .ThenAwait(_ => service.Get(userId))
      .Then(c => new StravaConnectionRes(c != null, c?.ConnectedAtUtc), Errors.MapNone);
    return this.ReturnResult(result);
  }

  // Revokes LazyTax in Strava and deletes the user's Strava data; their Strava habits go back to
  // manual check-in. Checks already done stay done.
  [Authorize, HttpDelete("{userId}")]
  public async Task<ActionResult> Disconnect(string userId)
  {
    if (!Opt.Enabled) return this.NotFound();
    var result = await this.GuardAsync(userId).ThenAwait(_ => service.Disconnect(userId));
    return this.ReturnUnitNullableResult(result,
      new EntityNotFound("Strava Connection Not Found", typeof(StravaConnectionPrincipal), userId));
  }

  // Strava's subscription check: echo hub.challenge when the verify token matches.
  [HttpGet("webhook")]
  public ActionResult WebhookChallenge([FromQuery(Name = "hub.mode")] string? mode,
    [FromQuery(Name = "hub.verify_token")] string? verifyToken,
    [FromQuery(Name = "hub.challenge")] string? challenge)
  {
    if (!Opt.Enabled) return this.NotFound();
    if (mode != "subscribe" || challenge == null || verifyToken != Opt.VerifyToken) return this.StatusCode(403);
    return this.Ok(new Dictionary<string, string> { ["hub.challenge"] = challenge });
  }

  // Must answer within 2 seconds: queue the event and let the worker fetch the activity.
  // Events aren't signed; the worker only trusts what it fetches with the owner's own token.
  [HttpPost("webhook")]
  public async Task<ActionResult> Webhook([FromBody] JsonElement body)
  {
    if (!Opt.Enabled) return this.NotFound();
    var e = StravaWebhookParser.Parse(body, Opt.SubscriptionId);
    if (e == null)
    {
      logger.LogWarning("Ignoring Strava webhook event: {Body}", body.GetRawText());
      return this.Ok();
    }

    await inbox.Enqueue(e);
    return this.Ok();
  }

  private RedirectResult ToApp(string result) =>
    this.Redirect(QueryHelpers.AddQueryString(Opt.AppRedirectUri, "result", result));
}

public record StravaConnectUrlRes(string Url, string AppUrl);

public record StravaConnectionRes(bool Connected, DateTime? ConnectedAtUtc);

public static class StravaWebhookParser
{
  // Null for a malformed event or another subscription's (subscriptionId 0 skips that check).
  public static StravaWebhookEvent? Parse(JsonElement body, long subscriptionId)
  {
    if (body.ValueKind != JsonValueKind.Object) return null;
    if (!body.TryGetProperty("object_type", out var objectType) || objectType.ValueKind != JsonValueKind.String) return null;
    if (!body.TryGetProperty("aspect_type", out var aspectType) || aspectType.ValueKind != JsonValueKind.String) return null;
    if (!TryGetInt64(body, "object_id", out var oid)) return null;
    if (!TryGetInt64(body, "owner_id", out var owner)) return null;
    if (!TryGetInt64(body, "event_time", out var at) || at < MinUnixSeconds || at > MaxUnixSeconds) return null;
    if (subscriptionId != 0 && (!TryGetInt64(body, "subscription_id", out var sid) || sid != subscriptionId))
      return null;

    var deauthorized = body.TryGetProperty("updates", out var updates) && updates.ValueKind == JsonValueKind.Object &&
                       updates.TryGetProperty("authorized", out var authorized) &&
                       authorized.ValueKind == JsonValueKind.String && authorized.GetString() == "false";
    var type = objectType.GetString()!;
    var aspect = aspectType.GetString()!;
    if (type is not ("activity" or "athlete") || aspect is not ("create" or "update" or "delete")) return null;

    return new StravaWebhookEvent
    {
      ObjectType = type,
      AspectType = aspect,
      ObjectId = oid,
      OwnerId = owner,
      EventTimeUtc = DateTimeOffset.FromUnixTimeSeconds(at).UtcDateTime,
      Deauthorized = type == "athlete" && deauthorized
    };
  }

  private static readonly long MinUnixSeconds = DateTimeOffset.MinValue.ToUnixTimeSeconds();
  private static readonly long MaxUnixSeconds = DateTimeOffset.MaxValue.ToUnixTimeSeconds();

  // A JSON number that fits a long; TryGetInt64 throws on any other kind, such as a string.
  private static bool TryGetInt64(JsonElement body, string name, out long value)
  {
    value = 0;
    return body.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out value);
  }
}
