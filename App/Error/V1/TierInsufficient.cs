using System.ComponentModel;
using System.Text.Json.Serialization;
using NJsonSchema.Annotations;

namespace App.Error.V1;

[Description("This error means your subscription tier does not allow this action.")]
public class TierInsufficient : IDomainProblem
{
  public TierInsufficient() { }

  public TierInsufficient(string detail)
  {
    this.Detail = detail;
  }

  public TierInsufficient(string tier, string limitKey, int limitValue, string? detail = null)
  {
    this.Tier = tier;
    this.LimitKey = limitKey;
    this.LimitValue = limitValue;
    if (!string.IsNullOrWhiteSpace(detail)) this.Detail = detail!;
  }

  [JsonIgnore, JsonSchemaIgnore]
  public string Id { get; } = "tier_insufficient";

  [JsonIgnore, JsonSchemaIgnore]
  public string Title { get; } = "Tier Insufficient";

  [JsonIgnore, JsonSchemaIgnore]
  public string Version { get; } = "v1";

  [JsonIgnore, JsonSchemaIgnore]
  public string Detail { get; } = string.Empty;

  [Description("The current subscription tier of the user")]
  public string Tier { get; } = string.Empty;

  [Description("The entitlement key that was violated (e.g., ent.vacation.windows.yearly)")]
  public string LimitKey { get; } = string.Empty;

  [Description("The numeric limit value configured for the entitlement key")]
  public int LimitValue { get; } = 0;
}
