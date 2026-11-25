using System.ComponentModel;
using System.Text.Json.Serialization;
using NJsonSchema.Annotations;

namespace App.Error.V1;

[Description("This error means the habit's day has settled, so its result can no longer change.")]
public class HabitDayLocked : IDomainProblem
{
  public HabitDayLocked() { }

  public HabitDayLocked(string localDate)
  {
    this.LocalDate = localDate;
  }

  [JsonIgnore, JsonSchemaIgnore] public string Id { get; } = "habit_day_locked";
  [JsonIgnore, JsonSchemaIgnore] public string Title { get; } = "Habit Day Locked";
  [JsonIgnore, JsonSchemaIgnore] public string Version { get; } = "v1";

  [JsonIgnore, JsonSchemaIgnore]
  public string Detail { get; } = "The day has settled, so its completion can no longer be undone.";

  [Description("The settled local date (yyyy-MM-dd)")]
  public string LocalDate { get; } = string.Empty;
}
