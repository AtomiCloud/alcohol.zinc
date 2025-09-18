namespace App.StartUp.Options;

public class HubSpotOption
{
  public const string Key = "HubSpot";

  public int SubscriptionTypeId { get; set; }
  public string? LegalBasis { get; set; }
  public string? LegalBasisExplanation { get; set; }
}

