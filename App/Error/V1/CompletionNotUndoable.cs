using System.ComponentModel;
using System.Text.Json.Serialization;
using NJsonSchema.Annotations;

namespace App.Error.V1;

[Description("This error means the day wasn't completed by a tap in the app, so it can't be undone.")]
public class CompletionNotUndoable : IDomainProblem
{
  public CompletionNotUndoable() { }

  public CompletionNotUndoable(string status, string? settledBy)
  {
    this.Status = status;
    this.SettledBy = settledBy;
  }

  [JsonIgnore, JsonSchemaIgnore] public string Id { get; } = "completion_not_undoable";
  [JsonIgnore, JsonSchemaIgnore] public string Title { get; } = "Completion Not Undoable";
  [JsonIgnore, JsonSchemaIgnore] public string Version { get; } = "v1";

  [JsonIgnore, JsonSchemaIgnore]
  public string Detail { get; } = "Only a completion tapped in the app can be undone.";

  [Description("The day's status: done, missed, skipped, frozen or vacation")]
  public string Status { get; } = string.Empty;

  [Description("What settled the day: manual, nfc (a habit with an NFC tag), strava, or null")]
  public string? SettledBy { get; }
}
