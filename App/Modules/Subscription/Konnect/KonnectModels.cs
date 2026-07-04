using System.Text.Json.Serialization;

namespace App.Modules.Subscription.Konnect;

// Minimal DTOs for the Kong Konnect Metering & Billing (OpenMeter) v3 API.
// All Konnect API specifics stay in this folder; the rest of the app only sees
// Domain.Subscription.IKonnectGateway.

public class KonnectPage<T>
{
  [JsonPropertyName("data")] public List<T> Data { get; set; } = [];
}

public class KonnectCustomer
{
  [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
  [JsonPropertyName("key")] public string? Key { get; set; }
  [JsonPropertyName("name")] public string? Name { get; set; }
}

public class KonnectCreateCustomerReq
{
  [JsonPropertyName("key")] public required string Key { get; set; }
  [JsonPropertyName("name")] public required string Name { get; set; }
  [JsonPropertyName("usage_attribution")] public required KonnectUsageAttribution UsageAttribution { get; set; }
}

public class KonnectUsageAttribution
{
  [JsonPropertyName("subject_keys")] public required string[] SubjectKeys { get; set; }
}

public class KonnectPlan
{
  [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
  [JsonPropertyName("key")] public string? Key { get; set; }
  [JsonPropertyName("status")] public string? Status { get; set; }
}

public class KonnectSubscription
{
  [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
  [JsonPropertyName("customer_id")] public string CustomerId { get; set; } = string.Empty;
  [JsonPropertyName("plan_id")] public string? PlanId { get; set; }
  [JsonPropertyName("status")] public string? Status { get; set; } // active | inactive | canceled | scheduled
}

public class KonnectCreateSubscriptionReq
{
  [JsonPropertyName("customer")] public required KonnectRef Customer { get; set; }
  [JsonPropertyName("plan")] public required KonnectPlanRef Plan { get; set; }
  [JsonPropertyName("labels")] public Dictionary<string, string>? Labels { get; set; }
}

public class KonnectChangeSubscriptionReq
{
  [JsonPropertyName("customer")] public required KonnectRef Customer { get; set; }
  [JsonPropertyName("plan")] public required KonnectPlanRef Plan { get; set; }
  [JsonPropertyName("timing")] public required string Timing { get; set; } // immediate | next_billing_cycle
  [JsonPropertyName("labels")] public Dictionary<string, string>? Labels { get; set; }
}

public class KonnectRef
{
  [JsonPropertyName("id")] public required string Id { get; set; }
}

public class KonnectPlanRef
{
  [JsonPropertyName("key")] public required string Key { get; set; }
}
