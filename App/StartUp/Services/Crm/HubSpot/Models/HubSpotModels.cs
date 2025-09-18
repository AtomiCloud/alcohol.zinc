namespace App.StartUp.Services.Crm.HubSpot.Models;

// Search models
public record HubSpotSearchFilter(string PropertyName, string Operator, string Value);
public record HubSpotSearchFilterGroup(HubSpotSearchFilter[] Filters);
public record HubSpotSearchRequest(HubSpotSearchFilterGroup[] FilterGroups, string[]? Properties = null);
public record HubSpotObject(string Id);
public record HubSpotSearchResponse(HubSpotObject[] Results);

// Contact models
public record HubSpotCreateUpdateRequest
{
  public required Dictionary<string, object?> Properties { get; init; }
}

// Subscription models (Communication Preferences)
public record HubSpotSubscriptionStatusUpdate
{
  public int Id { get; init; }
  public bool Subscribed { get; init; }
  public string? LegalBasis { get; init; }
  public string? LegalBasisExplanation { get; init; }
}

public record HubSpotUpdateSubscriptionByEmailRequest
{
  public HubSpotSubscriptionStatusUpdate[] SubscriptionStatuses { get; init; } = [];
}
