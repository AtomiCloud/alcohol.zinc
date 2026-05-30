using System.Text.Json;
using App.Modules.Payment.Airwallex;
using CSharp_Result;
using Domain.Payment;

namespace App.Modules.Payment.Data;

public class AirwallexGateway(AirwallexClient client) : IPaymentGateway
{
  public async Task<Result<string?>> GetCustomerIdByMerchantIdAsync(string merchantCustomerId)
  {
    return await client
      .ListCustomersAsync(merchantCustomerId)
      .Then(res => res.Items.Length > 0 ? res.Items[0].Id : null, Errors.MapNone);
  }

  public async Task<Result<string>> CreateCustomerAsync(string merchantCustomerId)
  {
    var req = new AirwallexCreateCustomerReq
    {
      RequestId = Guid.NewGuid().ToString(),
      MerchantCustomerId = merchantCustomerId
    };

    return await client
      .CreateCustomerAsync(req)
      .Then(res => res.Id, Errors.MapNone);
  }

  public async Task<Result<string>> GenerateClientSecretAsync(string customerId)
  {
    return await client
      .GenerateClientSecretAsync(customerId)
      .Then(res => res.ClientSecret, Errors.MapNone);
  }

  public async Task<Result<PaymentConsentInfo[]>> GetVerifiedPaymentConsentsAsync(string customerId)
  {
    return await client
      .GetPaymentConsentsAsync(customerId, "VERIFIED")
      .Then(res => res.Items.Select(item => new PaymentConsentInfo
      {
        Id = item.Id,
        Status = item.Status,
        Currency = item.Currency,
        NextTriggeredBy = item.NextTriggeredBy
      }).ToArray(), Errors.MapNone);
  }

  public async Task<Result<Unit>> DisablePaymentConsentAsync(string paymentConsentId)
  {
    return await client
      .DisablePaymentConsentAsync(paymentConsentId)
      .Then(_ => new Unit(), Errors.MapNone);
  }

  public async Task<Result<PaymentIntentResult>> CreatePaymentIntentAsync(CreatePaymentIntentRequest request)
  {
    // Deterministic idempotency: when the caller supplies a stable key (e.g. the
    // penalty Id) reuse it for BOTH request_id and merchant_order_id so Airwallex
    // dedupes retried/concurrent attempts onto the same intent. Otherwise mint
    // fresh GUIDs (interactive flows that intend a brand-new intent each call).
    var requestId = request.IdempotencyKey ?? Guid.NewGuid().ToString();
    var merchantOrderId = request.IdempotencyKey ?? Guid.NewGuid().ToString();
    var req = new AirwallexCreatePaymentIntentReq
    {
      RequestId = requestId,
      Amount = request.Amount,
      Currency = request.Currency,
      CustomerId = request.CustomerId,
      MerchantOrderId = merchantOrderId
    };

    return await client
      .CreatePaymentIntentAsync(req)
      .Then(res => new PaymentIntentResult
      {
        Id = res.Id,
        Status = res.Status,
        Amount = res.Amount,
        Currency = res.Currency,
        CustomerId = res.CustomerId,
        MerchantOrderId = res.MerchantOrderId
      }, Errors.MapNone);
  }

  public async Task<Result<PaymentIntentResult>> RetrievePaymentIntentAsync(string paymentIntentId)
  {
    return await client
      .GetPaymentIntentAsync(paymentIntentId)
      .Then(res => new PaymentIntentResult
      {
        Id = res.Id,
        Status = res.Status,
        Amount = res.Amount,
        Currency = res.Currency,
        CustomerId = res.CustomerId,
        MerchantOrderId = res.MerchantOrderId
      }, Errors.MapNone);
  }

  public async Task<Result<PaymentIntentResult>> ConfirmPaymentIntentAsync(string paymentIntentId, string paymentConsentId, string customerId)
  {
    var req = new AirwallexConfirmPaymentIntentReq
    {
      RequestId = Guid.NewGuid().ToString(),
      PaymentConsentId = paymentConsentId,
      CustomerId = customerId
    };

    return await client
      .ConfirmPaymentIntentAsync(paymentIntentId, req)
      .Then(res => new PaymentIntentResult
      {
        Id = res.Id,
        Status = res.Status,
        Amount = res.Amount,
        Currency = res.Currency,
        CustomerId = res.CustomerId,
        MerchantOrderId = res.MerchantOrderId
      }, Errors.MapNone);
  }
}
