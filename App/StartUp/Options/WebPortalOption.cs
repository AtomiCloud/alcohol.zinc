using System.ComponentModel.DataAnnotations;

namespace App.StartUp.Options;

// Web billing portal handoff: where the neon app's magic-link URL points, and
// which storefronts may be shown the web "subscribe" link-out. Steering rules
// differ per store region and shift with ongoing litigation, so the matrix
// lives in per-landscape config, not code.
public class WebPortalOption
{
  public const string Key = "WebPortal";

  [Required, AllowedValues("http", "https")]
  public string Scheme { get; set; } = string.Empty;

  [Required] public string Host { get; set; } = string.Empty;

  [Required] public string HandoffPath { get; set; } = "/auth/handoff";

  [Required] public string RedirectPath { get; set; } = "/billing";

  // Kept below Logto's 600s default: the token is a live login credential and
  // is consumed within seconds of minting (tap -> browser).
  [Range(30, 3600)] public int OttExpirySeconds { get; set; } = 300;

  // Storefront allowlist per platform ("ios"/"android"): countries where a
  // free user may see the "subscribe" link-out. Anything absent fails closed
  // to the neutral CTA (no steering, no price comparison).
  [Required] public Dictionary<string, CtaPlatformOption> Cta { get; set; } = [];
}

public class CtaPlatformOption
{
  [Required] public string[] SubscribeAllowed { get; set; } = [];
}
