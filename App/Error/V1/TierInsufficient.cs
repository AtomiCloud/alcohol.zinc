using System.ComponentModel;
using System.Text.Json.Serialization;
using App.StartUp.Registry;
using Humanizer;
using NJsonSchema.Annotations;

namespace App.Error.V1;

[Description("This error means your subscription tier does not allow this action.")]
public class TierInsufficient : IDomainProblem
{
  private readonly string detail = string.Empty;

  public TierInsufficient() { }

  public TierInsufficient(string detail)
  {
    this.detail = detail;
  }

  public TierInsufficient(string tier, string limitKey, int limitValue, string? detail = null)
  {
    this.Tier = tier;
    this.LimitKey = limitKey;
    this.LimitValue = limitValue;
    if (!string.IsNullOrWhiteSpace(detail)) this.detail = detail!;
  }

  [JsonIgnore, JsonSchemaIgnore]
  public string Id { get; } = "tier_insufficient";

  [JsonIgnore, JsonSchemaIgnore]
  public string Title { get; } = "Tier Insufficient";

  [JsonIgnore, JsonSchemaIgnore]
  public string Version { get; } = "v1";

  // Never surface an empty detail: RFC7807 `detail` is what clients render, and an
  // empty string is indistinguishable from "no message" to users. When no explicit
  // detail is supplied, derive a human-readable one from Tier/LimitKey/LimitValue.
  [JsonIgnore, JsonSchemaIgnore]
  public string Detail => string.IsNullOrWhiteSpace(this.detail) ? this.DefaultDetail() : this.detail;

  [Description("The current subscription tier of the user")]
  public string Tier { get; } = string.Empty;

  [Description("The entitlement key that was violated (e.g., ent.vacation.windows.yearly)")]
  public string LimitKey { get; } = string.Empty;

  [Description("The numeric limit value configured for the entitlement key")]
  public int LimitValue { get; } = 0;

  private string DefaultDetail()
  {
    var plan = string.IsNullOrWhiteSpace(this.Tier) ? "current" : this.Tier;
    var (noun, period) = this.LimitKey switch
    {
      EntitlementKeys.HabitsMax => ("habit", string.Empty),
      EntitlementKeys.SkipsMonthly => ("skip", " per month"),
      EntitlementKeys.VacationWindowsYearly => ("vacation window", " per year"),
      EntitlementKeys.FreezeBase => ("streak freeze", string.Empty),
      _ => ((string?)null, string.Empty),
    };

    if (noun == null)
      return string.IsNullOrWhiteSpace(this.LimitKey)
        ? $"Your {plan} plan does not allow this action."
        : $"Your {plan} plan has reached its limit of {this.LimitValue} for '{this.LimitKey}'.";

    return this.LimitValue <= 0
      ? $"Your {plan} plan does not include {noun.Pluralize()}."
      : $"Your {plan} plan allows up to {noun.ToQuantity(this.LimitValue)}{period}.";
  }
}
