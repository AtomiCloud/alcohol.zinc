using System.Text.Json;
using App.Modules.Strava;
using App.Modules.Strava.API.V1;
using CSharp_Result;
using Domain.Check;
using Domain.Proof;
using Domain.Strava;

namespace UnitTest.Strava;

public class StravaTests
{
  private static readonly ProofWindow Window =
    new(new DateOnly(2026, 1, 5), "UTC", new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc),
      new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc));

  private static ProofBinding Binding(string settings) => new()
  {
    UserId = "u",
    Principal = new ProofBindingPrincipal
    {
      Id = Guid.NewGuid(), HabitId = Guid.NewGuid(),
      Record = new ProofBindingRecord { PluginKey = StravaProofPlugin.Key, PluginVersion = 1, Settings = settings }
    }
  };

  private static ProofEvent Activity(string sport, bool manual) => new()
  {
    PluginKey = StravaProofPlugin.Key, UserId = "u", ExternalId = "1", OccurredAtUtc = Window.OpensAtUtc,
    Payload = JsonSerializer.Serialize(new StravaActivity { SportType = sport, StartDate = Window.OpensAtUtc, Manual = manual },
      new JsonSerializerOptions(JsonSerializerDefaults.Web))
  };

  [Theory]
  [InlineData("Run", false, true)]
  [InlineData("TrailRun", false, true)]
  [InlineData("Run", true, false)] // typed in by hand: never counts
  [InlineData("Ride", false, false)] // not a bound sport type
  public async Task Evaluate_PassesOnlyRecordedActivitiesOfABoundSportType(string sport, bool manual, bool passes)
  {
    var plugin = new StravaProofPlugin(new NoConnections());

    var verdict = await plugin.Evaluate(Binding("""{"sportTypes":["Run","TrailRun"]}"""), Activity(sport, manual), Window);

    (verdict.Get()?.Verdict == EvidenceVerdict.Passed).Should().Be(passes);
    verdict.Get()?.Data.Should().BeNull("Strava data is never stored as evidence");
  }

  [Theory]
  [InlineData("""{"sportTypes":[]}""")]
  [InlineData("""{"sportTypes":["Run","Jogging"]}""")]
  [InlineData("""{}""")]
  public void ValidateSettings_RejectsEmptyOrUnknownSportTypes(string settings)
  {
    var result = new StravaProofPlugin(new NoConnections()).ValidateSettings(settings);

    result.FailureOrDefault().Should().BeOfType<InvalidProofBindingException>()
      .Which.Errors.Should().ContainKey("sportTypes");
  }

  [Fact]
  public async Task OnBind_RefusesWhenStravaIsNotConnected()
  {
    var result = await new StravaProofPlugin(new NoConnections()).OnBind(Binding("""{"sportTypes":["Run"]}"""));

    result.FailureOrDefault().Should().BeOfType<InvalidProofBindingException>();
  }

  [Fact]
  public void Manifest_NeedsProAndListsEverySportType()
  {
    var manifest = new StravaProofPlugin(new NoConnections()).Manifest;

    manifest.MinTier.Should().Be("pro");
    using var schema = JsonDocument.Parse(manifest.SettingsSchema);
    schema.RootElement.GetProperty("properties").GetProperty("sportTypes").GetProperty("items")
      .GetProperty("enum").GetArrayLength().Should().Be(StravaProofPlugin.SportTypes.Length);
  }

  private static readonly DateTime Now = new(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);

  [Fact]
  public void State_RoundTripsTheUser()
  {
    var state = StravaState.Sign("user:with.dots", Now.AddMinutes(15), "key");

    StravaState.Verify(state, Now, "key").Should().Be("user:with.dots");
  }

  [Fact]
  public void State_RejectsExpiredForgedOrTamperedStates()
  {
    var state = StravaState.Sign("alice", Now.AddMinutes(15), "key");
    var forged = StravaState.Sign("bob", Now.AddMinutes(15), "other-key");
    var body = Convert.ToBase64String("1767617100:bob"u8.ToArray()).TrimEnd('=');
    var swapped = $"{body}.{state.Split('.')[1]}";

    StravaState.Verify(state, Now.AddMinutes(16), "key").Should().BeNull("expired");
    StravaState.Verify(forged, Now, "key").Should().BeNull("signed with another key");
    StravaState.Verify(swapped, Now, "key").Should().BeNull("body changed under alice's signature");
    StravaState.Verify(null, Now, "key").Should().BeNull();
    StravaState.Verify("garbage", Now, "key").Should().BeNull();
  }

  [Fact]
  public void Webhook_ParsesActivityAndDeauthorizeEvents()
  {
    var create = Parse("""
      {"aspect_type":"create","event_time":1767600000,"object_id":1360128428,"object_type":"activity",
       "owner_id":134815,"subscription_id":120475,"updates":{}}
      """, 120475);
    var deauth = Parse("""
      {"aspect_type":"update","event_time":1767600000,"object_id":134815,"object_type":"athlete",
       "owner_id":134815,"subscription_id":120475,"updates":{"authorized":"false"}}
      """, 120475);

    create.Should().BeEquivalentTo(new StravaWebhookEvent
    {
      ObjectType = "activity", AspectType = "create", ObjectId = 1360128428, OwnerId = 134815,
      EventTimeUtc = DateTimeOffset.FromUnixTimeSeconds(1767600000).UtcDateTime, Deauthorized = false
    });
    deauth!.Deauthorized.Should().BeTrue();
  }

  [Theory]
  [InlineData("""{"aspect_type":"create","event_time":1,"object_id":1,"object_type":"activity","owner_id":1,"subscription_id":999}""")]
  [InlineData("""{"aspect_type":"create","event_time":1,"object_type":"activity","owner_id":1,"subscription_id":120475}""")]
  [InlineData("""{"aspect_type":"rename","event_time":1,"object_id":1,"object_type":"activity","owner_id":1,"subscription_id":120475}""")]
  [InlineData("""[]""")]
  public void Webhook_IgnoresOtherSubscriptionsAndMalformedEvents(string body)
  {
    Parse(body, 120475).Should().BeNull();
  }

  private static StravaWebhookEvent? Parse(string body, long subscriptionId)
  {
    using var doc = JsonDocument.Parse(body);
    return StravaWebhookParser.Parse(doc.RootElement, subscriptionId);
  }

  private sealed class NoConnections : IStravaConnectionRepository
  {
    public Task<Result<StravaConnectionPrincipal?>> Get(string userId) =>
      Task.FromResult<Result<StravaConnectionPrincipal?>>((StravaConnectionPrincipal?)null);

    public Task<Result<StravaConnectionPrincipal?>> GetByAthlete(long athleteId) => throw new NotImplementedException();
    public Task<Result<StravaConnectionPrincipal>> Upsert(string userId, StravaConnectionRecord record) => throw new NotImplementedException();
    public Task<Result<StravaConnectionPrincipal?>> RefreshLocked(string userId,
      Func<StravaConnectionPrincipal, Task<Result<StravaTokens?>>> refresh) => throw new NotImplementedException();
    public Task<Result<Unit?>> Purge(string userId) => throw new NotImplementedException();
    public Task<Result<int>> DeleteActivityEvidence(long activityId) => throw new NotImplementedException();
  }
}
