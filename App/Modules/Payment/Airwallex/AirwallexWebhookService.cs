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
      .Then(_ => adapter.ProcessEvent(evt), Errors.MapNone)
      .ThenAwait(x =>
      {
        var (guid, record, complete) = x;
        return complete
          ? paymentService.CompletePaymentAsync(guid, record).Then(_ => new Unit(), Errors.MapNone)
          : paymentService.UpdatePaymentStatusAsync(guid, record).Then(_ => new Unit(), Errors.MapNone);
      });
  }
}