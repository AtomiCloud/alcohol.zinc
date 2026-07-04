using App.StartUp.Options;
using App.StartUp.Services.Auth;
using App.Utility;
using CSharp_Result;
using Domain.Exceptions;
using Domain.Subscription;
using Domain.User;
using Microsoft.Extensions.Options;

namespace App.Modules.Auth;

// CTA variants for the subscription screen. Unknown values must be treated as
// Neutral by clients, so new variants can ship server-side first.
public static class CtaVariants
{
  public const string Manage = "manage";
  public const string Subscribe = "subscribe";
  public const string Neutral = "neutral";
}

// Thrown when a caller in a restricted storefront (neutral CTA) asks for a
// handoff URL; mapped to 403 in AtomiControllerBase.MapException.
public class HandoffNotAvailableException(string platform, string? storefront)
  : Exception("Web handoff is not available for this platform/storefront")
{
  public string Platform { get; } = platform;
  public string? Storefront { get; } = storefront;
}

public record SubscriptionCta(string Variant, string Tier);

public record WebHandoff(string Url, int ExpiresInSeconds);

public interface IWebHandoffService
{
  Task<Result<SubscriptionCta>> ResolveCta(string userId, string platform, string? storefront);

  Task<Result<WebHandoff>> CreateHandoff(string userId, string platform, string? storefront);
}

// Pure steering-rule decision, extracted for direct unit testing. Allowlist
// semantics: anything not explicitly permitted fails closed to Neutral.
public static class CtaMatrix
{
  public static string Resolve(string tier, string platform, string? storefront,
    IReadOnlyDictionary<string, CtaPlatformOption> cta)
  {
    // Any non-free tier (i.e. any active subscription row, even a legacy tier
    // no longer in the catalog) may MANAGE: managing an existing subscription
    // is permitted in every storefront — only the free-user "subscribe" steer
    // below is region-gated, and that path is allowlist/fail-closed.
    if (tier != SubscriptionService.FreeTier) return CtaVariants.Manage;
    if (string.IsNullOrWhiteSpace(storefront)) return CtaVariants.Neutral;
    if (!cta.TryGetValue(platform.ToLowerInvariant(), out var p)) return CtaVariants.Neutral;
    return p.SubscribeAllowed.Contains(storefront, StringComparer.OrdinalIgnoreCase)
      ? CtaVariants.Subscribe
      : CtaVariants.Neutral;
  }
}

public class WebHandoffService(
  ISubscriptionService subscriptionService,
  IUserService userService,
  IAuthManagement authManagement,
  IOptionsMonitor<WebPortalOption> options,
  ILogger<WebHandoffService> logger
) : IWebHandoffService
{
  public Task<Result<SubscriptionCta>> ResolveCta(string userId, string platform, string? storefront)
  {
    var opt = options.CurrentValue;
    return subscriptionService.GetUserTier(userId)
      .Then(tier => new SubscriptionCta(CtaMatrix.Resolve(tier, platform, storefront, opt.Cta), tier),
        Errors.MapNone);
  }

  public async Task<Result<WebHandoff>> CreateHandoff(string userId, string platform, string? storefront)
  {
    var opt = options.CurrentValue;

    var ctaRes = await this.ResolveCta(userId, platform, storefront);
    if (!ctaRes.IsSuccess()) return ctaRes.FailureOrDefault()!;
    if (ctaRes.Get().Variant == CtaVariants.Neutral)
      return new HandoffNotAvailableException(platform, storefront);

    var userRes = await userService.GetById(userId);
    if (!userRes.IsSuccess()) return userRes.FailureOrDefault()!;
    var user = userRes.Get();
    if (user == null)
      return new NotFoundException("User not found", typeof(User), userId);
    var email = user.Principal.Record.Email;
    if (string.IsNullOrWhiteSpace(email))
      return new App.Error.V1.ValidationError(
        "User has no email on record; a web login link cannot be minted",
        new Dictionary<string, string[]> { ["email"] = ["missing"] }).ToException();

    logger.LogInformation("Creating web handoff for user {UserId}", userId);
    return await authManagement
      .CreateOneTimeToken(email, opt.OttExpirySeconds)
      .Then(token => new WebHandoff(BuildUrl(opt, token, email), opt.OttExpirySeconds),
        Errors.MapNone);
  }

  // Query param names deliberately match Logto's signIn extraParams keys
  // (one_time_token, login_hint) so the argon landing page can forward them
  // verbatim; `redirect` is the same-origin path to land on after sign-in.
  private static string BuildUrl(WebPortalOption opt, string token, string email)
  {
    var query = $"one_time_token={Uri.EscapeDataString(token)}"
                + $"&login_hint={Uri.EscapeDataString(email)}"
                + $"&redirect={Uri.EscapeDataString(opt.RedirectPath)}";
    return $"{opt.Scheme}://{opt.Host}{opt.HandoffPath}?{query}";
  }
}
