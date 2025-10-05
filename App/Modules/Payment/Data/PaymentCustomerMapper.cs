using Domain.Payment;

namespace App.Modules.Payment.Data;

public static class PaymentCustomerMapper
{
  // Helper methods to convert between enum and string for database storage
  private static PaymentConsentStatus? ParseConsentStatus(string? status)
  {
    return status switch
    {
      "VERIFIED" => PaymentConsentStatus.Verified,
      "REQUIRES_PAYMENT_METHOD" => PaymentConsentStatus.RequiresPaymentMethod,
      "REQUIRES_CUSTOMER_ACTION" => PaymentConsentStatus.RequiresCustomerAction,
      _ => null
    };
  }

  public static string? ConsentStatusToString(PaymentConsentStatus? status)
  {
    return status switch
    {
      PaymentConsentStatus.Verified => "VERIFIED",
      PaymentConsentStatus.RequiresPaymentMethod => "REQUIRES_PAYMENT_METHOD",
      PaymentConsentStatus.RequiresCustomerAction => "REQUIRES_CUSTOMER_ACTION",
      _ => null
    };
  }


  public static PaymentCustomer ToDomain(this PaymentCustomerData data)
  {
    return new PaymentCustomer
    {
      Principal = new PaymentCustomerPrincipal
      {
        Id = data.Id,
        Record = new PaymentCustomerRecord
        {
          UserId = data.UserId,
          AirwallexCustomerId = data.AirwallexCustomerId,
          PaymentConsentId = data.PaymentConsentId,
          ConsentStatus = ParseConsentStatus(data.PaymentConsentStatus),
          HasPaymentConsent = data.PaymentConsentId != null && ParseConsentStatus(data.PaymentConsentStatus) == PaymentConsentStatus.Verified
        },
        CreatedAt = data.CreatedAt,
        UpdatedAt = data.UpdatedAt
      }
    };
  }

  public static PaymentCustomerPrincipal ToPrincipal(this PaymentCustomerData data)
  {
    return new PaymentCustomerPrincipal
    {
      Id = data.Id,
      Record = new PaymentCustomerRecord
      {
        UserId = data.UserId,
        AirwallexCustomerId = data.AirwallexCustomerId,
        PaymentConsentId = data.PaymentConsentId,
        ConsentStatus = ParseConsentStatus(data.PaymentConsentStatus),
        HasPaymentConsent = data.PaymentConsentId != null && ParseConsentStatus(data.PaymentConsentStatus) == PaymentConsentStatus.Verified
      },
      CreatedAt = data.CreatedAt,
      UpdatedAt = data.UpdatedAt
    };
  }
}