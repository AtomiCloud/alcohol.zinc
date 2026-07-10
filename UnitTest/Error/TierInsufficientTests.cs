using App.Error.V1;
using App.StartUp.Registry;

namespace UnitTest.Error;

// TierInsufficient is rendered to users via RFC7807 `detail` (see
// ProblemDetailsService). An empty detail renders as an invisible message in the
// clients, so when no explicit detail is supplied the error must derive a
// human-readable one from its own Tier/LimitKey/LimitValue fields.
public class TierInsufficientTests
{
  // ---------------------------------------------------------------------------
  // Default detail — derived from tier + entitlement key + limit.
  // ---------------------------------------------------------------------------
  [Theory]
  [InlineData("free", EntitlementKeys.HabitsMax, 2, "Your free plan allows up to 2 habits.")]
  [InlineData("pro", EntitlementKeys.HabitsMax, 1, "Your pro plan allows up to 1 habit.")]
  [InlineData("free", EntitlementKeys.SkipsMonthly, 10, "Your free plan allows up to 10 skips per month.")]
  [InlineData("pro", EntitlementKeys.VacationWindowsYearly, 6, "Your pro plan allows up to 6 vacation windows per year.")]
  [InlineData("pro", EntitlementKeys.FreezeBase, 14, "Your pro plan allows up to 14 streak freezes.")]
  public void Detail_WithoutExplicitDetail_DerivesFromFields(
    string tier, string limitKey, int limitValue, string expected)
  {
    var problem = new TierInsufficient(tier, limitKey, limitValue);

    problem.Detail.Should().Be(expected);
  }

  // Zero-limit entitlements (e.g. free tier has no vacation windows) read as
  // "does not include", not "allows up to 0".
  [Theory]
  [InlineData(EntitlementKeys.VacationWindowsYearly, "Your free plan does not include vacation windows.")]
  [InlineData(EntitlementKeys.FreezeBase, "Your free plan does not include streak freezes.")]
  public void Detail_ZeroLimit_ReadsAsNotIncluded(string limitKey, string expected)
  {
    var problem = new TierInsufficient("free", limitKey, 0);

    problem.Detail.Should().Be(expected);
  }

  // Unknown keys and missing fields must still produce a sensible, non-empty
  // message instead of leaking an empty detail.
  [Fact]
  public void Detail_UnknownLimitKey_FallsBackToGenericLimitMessage()
  {
    var problem = new TierInsufficient("free", "ent.something.new", 3);

    problem.Detail.Should().Be("Your free plan has reached its limit of 3 for 'ent.something.new'.");
  }

  [Fact]
  public void Detail_NoFieldsAtAll_IsStillNonEmpty()
  {
    var problem = new TierInsufficient();

    problem.Detail.Should().Be("Your current plan does not allow this action.");
  }

  [Fact]
  public void Detail_MissingTier_ReadsAsCurrentPlan()
  {
    var problem = new TierInsufficient(string.Empty, EntitlementKeys.HabitsMax, 2);

    problem.Detail.Should().Be("Your current plan allows up to 2 habits.");
  }

  // ---------------------------------------------------------------------------
  // Explicit detail — always wins over the derived default.
  // ---------------------------------------------------------------------------
  [Fact]
  public void Detail_ExplicitDetail_OverridesDefault()
  {
    var problem = new TierInsufficient("free", EntitlementKeys.HabitsMax, 2, "Custom message.");

    problem.Detail.Should().Be("Custom message.");
  }

  [Fact]
  public void Detail_DetailOnlyConstructor_IsPreserved()
  {
    var problem = new TierInsufficient("Custom message.");

    problem.Detail.Should().Be("Custom message.");
  }
}
