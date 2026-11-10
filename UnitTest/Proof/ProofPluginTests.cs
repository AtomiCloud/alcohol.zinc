using App.Error;
using App.Modules.Entitlement;
using CSharp_Result;
using Domain.Check;
using Domain.Proof;
using Domain.Subscription;
using NodaMoney;
using UnitTest.Protection;

namespace UnitTest.Proof;

public class ProofPluginTests
{
  // A plugin as an author would write one: typed settings and payload, one extra rule.
  private sealed class RunSettings
  {
    public string[] SportTypes { get; set; } = [];
    public int Target { get; set; } = 1;
  }

  private sealed class RunEvent
  {
    public string SportType { get; set; } = "";
  }

  private sealed class RunPlugin : ProofPlugin<RunSettings, RunEvent>
  {
    public override PluginManifest Manifest { get; } = new()
    {
      Key = "run", Name = "Run", Version = 1, Platforms = ["ios"], SettingsSchema = "{}", MinTier = "pro"
    };

    protected override Dictionary<string, string[]> Validate(RunSettings settings) =>
      settings.Target < 1 ? new() { ["target"] = ["Must be at least 1"] } : [];

    protected override Task<Result<ProofVerdict?>> Evaluate(RunSettings settings, RunEvent payload, ProofEvent e,
      ProofWindow window) =>
      Task.FromResult<Result<ProofVerdict?>>(settings.SportTypes.Contains(payload.SportType)
        ? new ProofVerdict(EvidenceVerdict.Passed, new { payload.SportType })
        : null);
  }

  private static readonly ProofWindow Window =
    new(new DateOnly(2026, 1, 5), "UTC", new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc),
      new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc));

  [Theory]
  [InlineData("""{"sportTypes":["Run"],"targt":2}""", "settings")] // typo: unknown field
  [InlineData("""{"sportTypes":["Run"],"target":0}""", "target")] // plugin's own rule
  [InlineData("""[]""", "settings")] // not an object
  [InlineData("""null""", "settings")]
  public void ValidateSettings_RejectsBadSettings_WithTheField(string settings, string field)
  {
    var result = new RunPlugin().ValidateSettings(settings);

    result.IsFailure().Should().BeTrue();
    var error = result.FailureOrDefault().Should().BeOfType<InvalidProofBindingException>().Subject;
    error.Errors.Should().ContainKey(field);
  }

  [Fact]
  public void ValidateSettings_NormalizesValidSettings()
  {
    var result = new RunPlugin().ValidateSettings("""{ "SportTypes": ["Run", "TrailRun"] }""");

    result.Get().Should().Be("""{"sportTypes":["Run","TrailRun"],"target":1}""");
  }

  [Fact]
  public async Task Evaluate_GetsTypedSettingsAndPayload_IgnoringUnknownPayloadFields()
  {
    var binding = new ProofBinding
    {
      UserId = "u",
      Principal = new ProofBindingPrincipal
      {
        Id = Guid.NewGuid(), HabitId = Guid.NewGuid(),
        Record = new ProofBindingRecord { PluginKey = "run", PluginVersion = 1, Settings = """{"sportTypes":["Run"],"target":1}""" }
      }
    };
    static ProofEvent Event(string sport) => new()
    {
      PluginKey = "run", UserId = "u", ExternalId = sport, OccurredAtUtc = Window.OpensAtUtc,
      Payload = $$"""{"sportType":"{{sport}}","distance":5012.3}"""
    };

    var run = await new RunPlugin().Evaluate(binding, Event("Run"), Window);
    var ride = await new RunPlugin().Evaluate(binding, Event("Ride"), Window);

    run.Get()!.Verdict.Should().Be(EvidenceVerdict.Passed);
    ride.Get().Should().BeNull();
  }

  [Theory]
  [InlineData("free", false)]
  [InlineData("pro", true)]
  [InlineData("ultimate", true)] // a pricier plan counts as "at least pro"
  public async Task EnsureProofPluginAllowed_ComparesPlansByPrice(string tier, bool allowed)
  {
    var entitlements = new EntitlementService(new FakeSubscriptionService(tier, 0), null!, null!, null!,
      new PricedPlans());

    var result = await entitlements.EnsureProofPluginAllowed("u", new RunPlugin().Manifest);

    result.IsSuccess().Should().Be(allowed);
    if (!allowed)
      ((DomainProblemException)result.FailureOrDefault()).Problem.Should().BeOfType<App.Error.V1.TierInsufficient>();
  }

  [Fact]
  public async Task EnsureProofPluginAllowed_WithoutMinTier_AllowsEveryTier()
  {
    var entitlements = new EntitlementService(new FakeSubscriptionService("free", 0), null!, null!, null!,
      new PricedPlans());

    var result = await entitlements.EnsureProofPluginAllowed("u", new ManualProofPlugin().Manifest);

    result.IsSuccess().Should().BeTrue();
  }

  private sealed class PricedPlans : ISubscriptionPlanProvider
  {
    private static readonly Dictionary<string, long> Cents = new() { ["free"] = 0, ["pro"] = 499, ["ultimate"] = 799 };

    public Result<SubscriptionPlan> GetPlan(string tier) => new SubscriptionPlan
    {
      Tier = tier,
      Price = new Money(Cents[tier] / 100m, Currency.FromCode("USD")),
      Caps = new Dictionary<string, int>(),
      GracePeriodDays = 7
    };
  }
}
