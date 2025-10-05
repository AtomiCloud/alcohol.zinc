using App.Error.Common;
using App.Error.V1;
using App.Utility;
using CSharp_Result;
using Domain.Payment;

namespace App.Modules.Payment.Airwallex;

public class AirwallexWebhookService(
  IPaymentService paymentService,
  AirwallexEventAdapter adapter,
  AirwallexHmacCalculator airwallexHmacCalculator,
  ILogger<AirwallexWebhookService> logger
)
{
  public Task<Result<Unit>> ProcessEvent(
    AirwallexEvent evt,
    string timestamp,
    string payload,
    string signature
  )
  {
    return airwallexHmacCalculator
      .Compute(timestamp, payload)
      .ToAsyncResult()
      .Then(x =>
        x == signature
          ? new Unit().ToResult()
          : new Unauthorized(
            "Incorrect Signature",
            [new Scope("x-signature", signature)],
            [new Scope("x-signature", x)]
          ).ToException()
      )
      .ThenAwait(_ => this.RouteEvent(evt));
  }

  private Task<Result<Unit>> RouteEvent(AirwallexEvent evt)
  {
    return evt.Name switch
    {
      "payment_consent.verified" or "payment_consent.verification_failed" => this.ProcessPaymentConsentEvent(evt),
      _ => this.LogUnknownEvent(evt)
    };
  }

  private Task<Result<Unit>> ProcessPaymentConsentEvent(AirwallexEvent evt)
  {
    var (customerId, consentId, status) = adapter.ProcessPaymentConsentEvent(evt);
    logger.LogInformation(
      "Processing payment consent event: {EventName}, CustomerId: {CustomerId}, ConsentId: {ConsentId}, Status: {Status}",
      evt.Name, customerId, consentId, status);

    return paymentService
      .UpdatePaymentConsentAsync(customerId, consentId, status)
      .Then(_ => new Unit(), Errors.MapNone);
  }

  // private Task<Result<Unit>> ProcessPaymentIntentEvent(AirwallexEvent evt)
  // {
  //   var (guid, record, complete) = adapter.ProcessPaymentIntentEvent(evt);
  //   logger.LogInformation(
  //     "Processing payment intent event: {EventName}, RequestId: {RequestId}, Status: {Status}, Complete: {Complete}",
  //     evt.Name, guid, record.Status, complete);
  //
  //   return complete
  //     ? paymentService.CompletePaymentAsync(guid, record).Then(_ => new Unit(), Errors.MapNone)
  //     : paymentService.UpdatePaymentStatusAsync(guid, record).Then(_ => new Unit(), Errors.MapNone);
  // }

  private Task<Result<Unit>> LogUnknownEvent(AirwallexEvent evt)
  {
    logger.LogWarning("Unknown webhook event type: {EventName}", evt.Name);
    return new Unit().ToAsyncResult();
  }
}
