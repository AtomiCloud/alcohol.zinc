using System.Text.Json;
using CSharp_Result;
using Domain.Check;
using Domain.Proof;

namespace Domain.Strava;

public class StravaSettings
{
  public string[] SportTypes { get; set; } = [];
}

// Completes a habit on a recorded Strava activity of a bound sport type, any length. Activities
// typed in by hand (manual) never count. Evidence carries the activity ID only, no Strava data.
public sealed class StravaProofPlugin(IStravaConnectionRepository connections)
  : ProofPlugin<StravaSettings, StravaActivity>
{
  public const string Key = "strava";
  public const int Version = 1;

  // Strava's SportType values (developers.strava.com/docs/reference, read 2026-11-11).
  public static readonly string[] SportTypes =
  [
    "AlpineSki", "BackcountrySki", "Badminton", "Basketball", "Canoeing", "Cricket", "Crossfit", "Dance",
    "EBikeRide", "Elliptical", "EMountainBikeRide", "Golf", "GravelRide", "Handcycle",
    "HighIntensityIntervalTraining", "Hike", "IceSkate", "InlineSkate", "Kayaking", "Kitesurf",
    "MountainBikeRide", "NordicSki", "Padel", "PhysicalTherapy", "Pickleball", "Pilates", "Racquetball", "Ride",
    "RockClimbing", "RollerSki", "Rowing", "Run", "Sail", "Skateboard", "Snowboard", "Snowshoe", "Soccer",
    "Squash", "StairStepper", "StandUpPaddling", "Surfing", "Swim", "TableTennis", "Tennis", "TrailRun",
    "Velomobile", "VirtualRide", "VirtualRow", "VirtualRun", "Volleyball", "Walk", "WeightTraining", "Wheelchair",
    "Windsurf", "Workout", "Yoga"
  ];

  public override PluginManifest Manifest { get; } = new()
  {
    Key = Key,
    Name = "Strava",
    Version = StravaProofPlugin.Version,
    Platforms = ["ios", "android"],
    SettingsSchema = JsonSerializer.Serialize(new
    {
      type = "object",
      properties = new
      {
        sportTypes = new
        {
          type = "array",
          minItems = 1,
          items = new { type = "string", @enum = SportTypes }
        }
      },
      required = new[] { "sportTypes" },
      additionalProperties = false
    }),
    MinTier = "pro"
  };

  protected override Dictionary<string, string[]> Validate(StravaSettings settings)
  {
    if (settings.SportTypes.Length == 0) return new() { ["sportTypes"] = ["Pick at least one sport type"] };
    var unknown = settings.SportTypes.Where(t => !SportTypes.Contains(t)).ToArray();
    return unknown.Length > 0
      ? new() { ["sportTypes"] = [$"Unknown Strava sport type: {string.Join(", ", unknown)}"] }
      : [];
  }

  protected override async Task<Result<Unit>> OnBind(ProofBinding binding, StravaSettings settings)
  {
    var connection = await connections.Get(binding.UserId);
    if (connection.IsFailure()) return connection.FailureOrDefault();
    return connection.Get() == null
      ? new InvalidProofBindingException("pluginKey", "Connect Strava before choosing it as proof")
      : new Unit();
  }

  protected override Task<Result<ProofVerdict?>> Evaluate(StravaSettings settings, StravaActivity payload,
    ProofEvent e, ProofWindow window) =>
    Task.FromResult<Result<ProofVerdict?>>(!payload.Manual && settings.SportTypes.Contains(payload.SportType)
      ? new ProofVerdict(EvidenceVerdict.Passed)
      : null);
}
