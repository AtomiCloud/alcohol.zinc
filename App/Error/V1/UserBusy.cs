using System.ComponentModel;
using System.Text.Json.Serialization;
using NJsonSchema.Annotations;

namespace App.Error.V1;

[Description("This error means another request of the same user is still changing their data; retry shortly.")]
public class UserBusy : IDomainProblem
{
  public UserBusy() { }

  public UserBusy(string userId)
  {
    this.UserId = userId;
  }

  [JsonIgnore, JsonSchemaIgnore] public string Id { get; } = "user_busy";
  [JsonIgnore, JsonSchemaIgnore] public string Title { get; } = "User Busy";
  [JsonIgnore, JsonSchemaIgnore] public string Version { get; } = "v1";
  [JsonIgnore, JsonSchemaIgnore] public string Detail { get; } = "Another request of this user is in progress; retry shortly.";

  [Description("The user whose request is in progress")]
  public string UserId { get; } = string.Empty;
}
