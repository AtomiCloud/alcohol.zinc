using System.ComponentModel;
using System.Text.Json.Serialization;
using NJsonSchema.Annotations;

namespace App.Error.V1;

[Description(
  "This error means the web billing portal handoff is not offered for the caller's platform and storefront region.")]
public class HandoffNotAvailable : IDomainProblem
{
  public HandoffNotAvailable() { }

  public HandoffNotAvailable(string platform, string? storefront)
  {
    this.Platform = platform;
    this.Storefront = storefront ?? string.Empty;
  }

  [JsonIgnore, JsonSchemaIgnore] public string Id { get; } = "handoff_not_available";
  [JsonIgnore, JsonSchemaIgnore] public string Title { get; } = "Handoff Not Available";
  [JsonIgnore, JsonSchemaIgnore] public string Version { get; } = "v1";

  [JsonIgnore, JsonSchemaIgnore]
  public string Detail { get; } = "Web subscription handoff is not offered in this region.";

  [Description("The caller's platform")] public string Platform { get; } = string.Empty;

  [Description("The caller's storefront country (ISO 3166-1 alpha-2), empty if unknown")]
  public string Storefront { get; } = string.Empty;
}
