using System.ComponentModel.DataAnnotations;

namespace App.StartUp.Options;

public class PaymentOption
{
  public const string Key = "Payment";

  public required AirwallexOption Airwallex { get; set; }
}

public class AirwallexOption
{
  [Required] public string Id { get; set; } = string.Empty;
  [Required] public string Key { get; set; } = string.Empty;
  [Required] public string Webhook { get; set; } = string.Empty;
}
