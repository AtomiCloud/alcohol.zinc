using System.ComponentModel.DataAnnotations;

namespace App.StartUp.Options;

public class PaymentOption
{
  public const string Key = "Payment";

  public required AirwallexOption Airwallex { get; set; }
}

public class AirwallexOption
{
  [Required] public string ClientId { get; set; } = string.Empty;
  [Required] public string ApiKey { get; set; } = string.Empty;
  [Required] public string WebhookKey { get; set; } = string.Empty;
  [Required, Url] public string BaseUrl { get; set; } = "https://api-demo.airwallex.com/api/v1";
}