using Domain.Payment;

namespace App.Modules.Payment.Data;

public static class PaymentIntentMapper
{
  private static PaymentIntentStatus ParseIntentStatus(string status)
  {
    return status switch
    {
      "REQUIRES_PAYMENT_METHOD" => PaymentIntentStatus.RequiresPaymentMethod,
      "REQUIRES_CUSTOMER_ACTION" => PaymentIntentStatus.RequiresCustomerAction,
      "REQUIRES_CAPTURE" => PaymentIntentStatus.RequiresCapture,
      "PENDING" => PaymentIntentStatus.Pending,
      "SUCCEEDED" => PaymentIntentStatus.Succeeded,
      "CANCELLED" => PaymentIntentStatus.Cancelled,
      _ => throw new ArgumentException($"Unknown payment intent status: {status}")
    };
  }

  public static string IntentStatusToString(PaymentIntentStatus status)
  {
    return status switch
    {
      PaymentIntentStatus.RequiresPaymentMethod => "REQUIRES_PAYMENT_METHOD",
      PaymentIntentStatus.RequiresCustomerAction => "REQUIRES_CUSTOMER_ACTION",
      PaymentIntentStatus.RequiresCapture => "REQUIRES_CAPTURE",
      PaymentIntentStatus.Pending => "PENDING",
      PaymentIntentStatus.Succeeded => "SUCCEEDED",
      PaymentIntentStatus.Cancelled => "CANCELLED",
      _ => throw new ArgumentException($"Unknown payment intent status: {status}")
    };
  }

  public static PaymentIntent ToDomain(this PaymentIntentData data)
  {
    return new PaymentIntent
    {
      Principal = new PaymentIntentPrincipal
      {
        Id = data.Id,
        Record = new PaymentIntentRecord
        {
          UserId = data.UserId,
          AirwallexPaymentIntentId = data.AirwallexPaymentIntentId,
          AirwallexCustomerId = data.AirwallexCustomerId,
          Amount = data.AmountCents / 100m,  // Convert cents to decimal
          Currency = data.Currency,
          CapturedAmount = data.CapturedAmountCents / 100m,  // Convert cents to decimal
          Status = ParseIntentStatus(data.Status),
          MerchantOrderId = data.MerchantOrderId
        },
        CreatedAt = data.CreatedAt,
        UpdatedAt = data.UpdatedAt
      }
    };
  }

  public static PaymentIntentPrincipal ToPrincipal(this PaymentIntentData data)
  {
    return new PaymentIntentPrincipal
    {
      Id = data.Id,
      Record = new PaymentIntentRecord
      {
        UserId = data.UserId,
        AirwallexPaymentIntentId = data.AirwallexPaymentIntentId,
        AirwallexCustomerId = data.AirwallexCustomerId,
        Amount = data.AmountCents / 100m,  // Convert cents to decimal
        Currency = data.Currency,
        CapturedAmount = data.CapturedAmountCents / 100m,  // Convert cents to decimal
        Status = ParseIntentStatus(data.Status),
        MerchantOrderId = data.MerchantOrderId
      },
      CreatedAt = data.CreatedAt,
      UpdatedAt = data.UpdatedAt
    };
  }
}