using App.Modules.Auth;
using App.StartUp.Options;

namespace UnitTest.Handoff;

// The pure steering-rule decision: paid -> manage everywhere; free -> subscribe
// only in explicitly allowlisted storefronts; everything else fails closed to
// neutral (compliance-safe default for restricted regions like SG).
public class CtaMatrixTests
{
  private static Dictionary<string, CtaPlatformOption> Cta(
    string[]? ios = null, string[]? android = null) => new()
  {
    ["ios"] = new CtaPlatformOption { SubscribeAllowed = ios ?? [] },
    ["android"] = new CtaPlatformOption { SubscribeAllowed = android ?? [] },
  };

  [Theory]
  [InlineData("US")]
  [InlineData("SG")]
  [InlineData(null)]
  public void PaidTier_IsManage_RegardlessOfStorefront(string? storefront)
  {
    CtaMatrix.Resolve("pro", "ios", storefront, Cta(ios: ["US"]))
      .Should().Be(CtaVariants.Manage);
  }

  [Fact]
  public void FreeTier_AllowlistedStorefront_IsSubscribe()
  {
    CtaMatrix.Resolve("free", "ios", "US", Cta(ios: ["US", "DE"]))
      .Should().Be(CtaVariants.Subscribe);
  }

  [Fact]
  public void FreeTier_StorefrontCaseInsensitive()
  {
    CtaMatrix.Resolve("free", "ios", "us", Cta(ios: ["US"]))
      .Should().Be(CtaVariants.Subscribe);
  }

  [Theory]
  [InlineData("SG")]
  [InlineData("BR")]
  [InlineData("XX")]
  public void FreeTier_UnlistedStorefront_IsNeutral(string storefront)
  {
    CtaMatrix.Resolve("free", "ios", storefront, Cta(ios: ["US", "DE"]))
      .Should().Be(CtaVariants.Neutral);
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData(" ")]
  public void FreeTier_MissingStorefront_FailsClosedToNeutral(string? storefront)
  {
    CtaMatrix.Resolve("free", "ios", storefront, Cta(ios: ["US"]))
      .Should().Be(CtaVariants.Neutral);
  }

  [Fact]
  public void FreeTier_UnknownPlatform_FailsClosedToNeutral()
  {
    CtaMatrix.Resolve("free", "windows", "US", Cta(ios: ["US"]))
      .Should().Be(CtaVariants.Neutral);
  }

  [Fact]
  public void PlatformAllowlists_AreIndependent()
  {
    var cta = Cta(ios: ["US"], android: ["DE"]);
    CtaMatrix.Resolve("free", "ios", "US", cta).Should().Be(CtaVariants.Subscribe);
    CtaMatrix.Resolve("free", "android", "US", cta).Should().Be(CtaVariants.Neutral);
    CtaMatrix.Resolve("free", "android", "DE", cta).Should().Be(CtaVariants.Subscribe);
    CtaMatrix.Resolve("free", "ios", "DE", cta).Should().Be(CtaVariants.Neutral);
  }
}
