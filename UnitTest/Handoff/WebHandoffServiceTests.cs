using App.Modules.Auth;
using App.StartUp.Options;
using Domain.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;

namespace UnitTest.Handoff;

// WebHandoffService: URL composition/encoding, expiry passthrough, and the
// guard rails (neutral callers refused, missing user -> not found, Logto
// failures propagate as failed Results).
public class WebHandoffServiceTests
{
  private const string Sub = "user-sub-1";

  private static WebPortalOption Opt(string[]? ios = null) => new()
  {
    Scheme = "https",
    Host = "portal.example.com",
    HandoffPath = "/auth/handoff",
    RedirectPath = "/billing",
    OttExpirySeconds = 300,
    Cta = new Dictionary<string, CtaPlatformOption>
    {
      ["ios"] = new() { SubscribeAllowed = ios ?? ["US"] },
      ["android"] = new() { SubscribeAllowed = [] },
    },
  };

  private static WebHandoffService Svc(
    string tier = "free",
    Domain.User.User? user = null,
    FakeAuthManagement? auth = null,
    WebPortalOption? opt = null)
    => new(
      new FakeSubscriptionService(tier),
      new FakeUserService(user),
      auth ?? new FakeAuthManagement("tok"),
      new FakeOptionsMonitor(opt ?? Opt()),
      NullLogger<WebHandoffService>.Instance);

  [Fact]
  public async Task CreateHandoff_ComposesMagicLinkUrl()
  {
    var auth = new FakeAuthManagement("the-token");
    var svc = Svc(user: Users.Make(Sub, "testuser@lazytax.club"), auth: auth);

    var result = await svc.CreateHandoff(Sub, "ios", "US");

    result.IsSuccess().Should().BeTrue();
    var handoff = (WebHandoff)result;
    handoff.Url.Should().Be(
      "https://portal.example.com/auth/handoff"
      + "?one_time_token=the-token"
      + "&login_hint=testuser%40lazytax.club"
      + "&redirect=%2Fbilling");
    handoff.ExpiresInSeconds.Should().Be(300);
  }

  [Fact]
  public async Task CreateHandoff_EncodesPlusInEmail()
  {
    var svc = Svc(user: Users.Make(Sub, "test+tag@lazytax.club"));

    var result = await svc.CreateHandoff(Sub, "ios", "US");

    ((WebHandoff)result).Url.Should().Contain("login_hint=test%2Btag%40lazytax.club");
  }

  [Fact]
  public async Task CreateHandoff_PassesConfiguredExpiryToLogto()
  {
    var auth = new FakeAuthManagement("tok");
    var opt = Opt();
    opt.OttExpirySeconds = 120;
    var svc = Svc(user: Users.Make(Sub, "a@b.co"), auth: auth, opt: opt);

    var result = await svc.CreateHandoff(Sub, "ios", "US");

    result.IsSuccess().Should().BeTrue();
    auth.CreateOneTimeTokenCalls.Should().ContainSingle()
      .Which.Should().Be(("a@b.co", 120));
    ((WebHandoff)result).ExpiresInSeconds.Should().Be(120);
  }

  [Fact]
  public async Task CreateHandoff_PaidUserInRestrictedStorefront_StillAllowed()
  {
    var svc = Svc(tier: "pro", user: Users.Make(Sub, "a@b.co"));

    var result = await svc.CreateHandoff(Sub, "ios", "SG");

    result.IsSuccess().Should().BeTrue();
  }

  [Fact]
  public async Task CreateHandoff_NeutralCaller_IsRefused()
  {
    var auth = new FakeAuthManagement("tok");
    var svc = Svc(user: Users.Make(Sub, "a@b.co"), auth: auth);

    var result = await svc.CreateHandoff(Sub, "ios", "SG");

    result.IsSuccess().Should().BeFalse();
    result.FailureOrDefault().Should().BeOfType<HandoffNotAvailableException>();
    auth.CreateOneTimeTokenCalls.Should().BeEmpty();
  }

  [Fact]
  public async Task CreateHandoff_MissingUser_IsNotFound()
  {
    var svc = Svc(user: null);

    var result = await svc.CreateHandoff(Sub, "ios", "US");

    result.IsSuccess().Should().BeFalse();
    result.FailureOrDefault().Should().BeOfType<NotFoundException>();
  }

  [Fact]
  public async Task CreateHandoff_LogtoFailure_PropagatesAsFailedResult()
  {
    var boom = new HttpRequestException("logto down");
    var svc = Svc(user: Users.Make(Sub, "a@b.co"), auth: new FakeAuthManagement(boom));

    var result = await svc.CreateHandoff(Sub, "ios", "US");

    result.IsSuccess().Should().BeFalse();
    result.FailureOrDefault().Should().Be(boom);
  }

  [Fact]
  public async Task ResolveCta_ReturnsVariantAndTier()
  {
    var svc = Svc(tier: "pro");

    var result = await svc.ResolveCta(Sub, "ios", "SG");

    ((SubscriptionCta)result).Should().Be(new SubscriptionCta(CtaVariants.Manage, "pro"));
  }

  [Fact]
  public async Task ResolveCta_FreeRestricted_IsNeutral()
  {
    var svc = Svc(tier: "free");

    var result = await svc.ResolveCta(Sub, "ios", "SG");

    ((SubscriptionCta)result).Should().Be(new SubscriptionCta(CtaVariants.Neutral, "free"));
  }
}
