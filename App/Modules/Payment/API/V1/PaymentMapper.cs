using Domain.Payment;

namespace App.Modules.Payment.API.V1;

public static class PaymentMapper
{
  // Request to Domain mappings
  public static CreatePaymentIntentRequest ToDomain(this CreatePaymentIntentReq req, string userId)
  {
    return new CreatePaymentIntentRequest
    {
      UserId = userId,
      CustomerId = "todo",
      Amount = req.Amount,
      Currency = req.Currency,
      Description = req.Description
    };
  }

  public static ConfirmPaymentIntentRequest ToDomain(this ConfirmPaymentIntentReq req, string userId, string intentId)
  {
    return new ConfirmPaymentIntentRequest
    {
      UserId = userId,
      PaymentIntentId = intentId,
      PaymentConsentId = req.PaymentConsentId
    };
  }

  // Domain to Response mappings
  public static CreateCustomerRes ToRes(this PaymentCustomerPrincipal principal, string clientSecret)
  {
    return new CreateCustomerRes
    {
      CustomerId = principal.Record.AirwallexCustomerId,
      ClientSecret = clientSecret
    };
  }

  public static ClientSecretRes ToRes(this ClientSecretResult result)
  {
    return new ClientSecretRes
    {
      ClientSecret = result.ClientSecret,
      CustomerId = result.CustomerId
    };
  }

  public static PaymentConsentRes ToRes(this PaymentConsentInfo info)
  {
    return new PaymentConsentRes
    {
      HasPaymentConsent = info.Status == "VERIFIED",
      ConsentId = info.Id,
      Status = info.Status
    };
  }

  public static PaymentConsentRes ToRes(this PaymentConsentStatusResult status)
  {
    return new PaymentConsentRes
    {
      HasPaymentConsent = status.HasPaymentConsent,
      ConsentId = status.ConsentId,
      Status = status.Status?.ToString()
    };
  }

  public static CreatePaymentIntentRes ToRes(this PaymentIntentResult result)
  {
    return new CreatePaymentIntentRes
    {
      PaymentIntentId = result.Id,
      Status = result.Status,
      Amount = result.Amount,
      Currency = result.Currency
    };
  }

  // Extension method that returns ConfirmPaymentIntentRes
  public static ConfirmPaymentIntentRes ToConfirmRes(this PaymentIntentResult result)
  {
    return new ConfirmPaymentIntentRes
    {
      PaymentIntentId = result.Id,
      Status = result.Status,
      Amount = result.Amount,
      Currency = result.Currency
    };
  }
}
