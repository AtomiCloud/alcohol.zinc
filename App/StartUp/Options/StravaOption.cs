using System.ComponentModel.DataAnnotations;

namespace App.StartUp.Options;

// One Strava API app per landscape (Strava allows one webhook subscription per app).
public class StravaOption
{
  public const string Key = "Strava";

  // Off: the strava proof plugin isn't offered, the endpoints return 404 and the worker doesn't run.
  public bool Enabled { get; set; } = false;

  [Required] public string ClientId { get; set; } = string.Empty;

  // Also signs the connect flow's state.
  [Required] public string ClientSecret { get; set; } = string.Empty;

  // Our own token, echoed by Strava when the webhook subscription is created.
  [Required] public string VerifyToken { get; set; } = string.Empty;

  // The subscription id Strava returned; webhook events with another id are ignored. 0 = not checked.
  [Range(0, long.MaxValue)] public long SubscriptionId { get; set; } = 0;

  // This API's callback URL, inside the Strava app's Authorization Callback Domain.
  [Required, Url] public string CallbackUrl { get; set; } = string.Empty;

  // The app's deep link the callback redirects to, with ?result=connected|denied|scope_missing|athlete_taken|expired.
  [Required] public string AppRedirectUri { get; set; } = string.Empty;

  [Range(1, int.MaxValue)] public int MaxAttempts { get; set; } = 5;
}
