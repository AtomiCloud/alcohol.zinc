using System.ComponentModel;
using System.Text.Json.Serialization;
using NJsonSchema.Annotations;

namespace App.Error.V1;

[Description("This error means the Strava activities of that day can no longer move: the day has settled.")]
public class StravaDayLocked : IDomainProblem
{
  public StravaDayLocked() { }

  public StravaDayLocked(string localDate)
  {
    this.LocalDate = localDate;
  }

  [JsonIgnore, JsonSchemaIgnore] public string Id { get; } = "strava_day_locked";
  [JsonIgnore, JsonSchemaIgnore] public string Title { get; } = "Strava Day Locked";
  [JsonIgnore, JsonSchemaIgnore] public string Version { get; } = "v1";

  [JsonIgnore, JsonSchemaIgnore]
  public string Detail { get; } = "The day has settled, so its Strava activities can no longer change habits.";

  [Description("The settled local date (yyyy-MM-dd)")]
  public string LocalDate { get; } = string.Empty;
}
