using CSharp_Result;
using Domain.Exceptions;
using Domain.Payment;
using Microsoft.Extensions.Logging;

namespace Domain.Penalty;

public class PenaltyService(
  IPenaltyRepository repo,
  IPaymentService payment,
  ILogger<PenaltyService> logger
) : IPenaltyService
{
  public async Task<Result<int>> ProcessPending(int batchSize, int maxAttempts)
  {
    return await repo.GetPending(batchSize)
      .ThenAwait(async pending =>
      {
        var results = new List<Result<int>>();
        foreach (var p in pending)
        {
          try
          {
            results.Add(await this.ProcessOne(p, maxAttempts));
          }
          catch (OperationCanceledException)
          {
            // A host shutdown / cancellation must abort the drain, not be turned into a Bump.
            throw;
          }
          catch (Exception ex)
          {
            // Isolate per penalty: an unexpected throw (e.g. a malformed gateway response or
            // an auth blip) must not abort the whole batch and strand the other claimed rows.
            // Record it against this penalty and keep draining the rest.
            logger.LogError(ex, "ProcessOne threw for penalty {Id}; isolating and continuing", p.Id);
            try
            {
              await (p.Record.Attempts + 1 >= maxAttempts
                ? repo.MarkFailed(p.Id, $"unhandled: {ex.Message}")
                : repo.Bump(p.Id, $"unhandled: {ex.Message}"));
            }
            catch (Exception inner)
            {
              logger.LogError(inner, "Failed to record isolation for penalty {Id}", p.Id);
            }
            results.Add(0);
          }
        }

        return results
          .ToResultOfSeq()
          .Then(xs => xs.Sum(), Errors.MapAll);
      });
  }

  private async Task<Result<int>> ProcessOne(PenaltyPrincipal p, int maxAttempts)
  {
    var rec = p.Record;
    // Deterministic idempotency key tied to this penalty row: a retried or
    // concurrent charge for the same penalty collapses onto the same Airwallex
    // intent instead of minting a new one. On a retry of a row that already
    // recorded an intent, reconcile that intent rather than re-charging.
    var chargeResult =
      await payment.ChargeStoredConsentAsync(
        rec.UserId, rec.Amount, $"Habit penalty {rec.HabitExecutionId}",
        idempotencyKey: p.Id.ToString(),
        existingIntentId: rec.PaymentIntentId,
        // Persist the intent id the moment it's created (before confirm) so a confirm
        // failure doesn't lose it; a persistence failure surfaces so the attempt retries.
        onIntentCreated: async intentId =>
        {
          var saved = await repo.SetIntentId(p.Id, intentId);
          if (!saved.IsSuccess())
            throw saved.FailureOrDefault() ?? new Exception($"SetIntentId failed for penalty {p.Id}");
        });

    if (chargeResult.IsSuccess())
    {
      var intent = chargeResult.Get();
      if (intent.Status == "SUCCEEDED")
        // Terminal success: charge + atomic charity credit. Propagate a
        // persistence failure (the credit's transaction rolling back) instead
        // of silently counting it as success.
        return await repo.MarkCharged(p.Id, intent.Id).Then(_ => 1, Errors.MapAll);

      // REQUIRES_PAYMENT_METHOD / REQUIRES_CUSTOMER_ACTION / REQUIRES_CAPTURE -> not yet settled, retry.
      var attempts = rec.Attempts + 1;
      if (attempts >= maxAttempts)
        return await repo.MarkFailed(p.Id, $"Max attempts reached, last status {intent.Status}")
          .Then(_ => 1, Errors.MapAll);

      return await repo.MarkPending(p.Id, intent.Id, attempts).Then(_ => 0, Errors.MapAll);
    }

    // Failure branch.
    var ex = chargeResult.FailureOrDefault();

    // No verified consent -> Skipped (typed NotFoundException(PaymentCustomer) from ChargeStoredConsentAsync).
    if (ex is NotFoundException)
      return await repo.MarkSkipped(p.Id).Then(_ => 1, Errors.MapAll);

    // Transient/other error -> bump; exhaust to Failed.
    if (rec.Attempts + 1 >= maxAttempts)
      return await repo.MarkFailed(p.Id, ex?.Message ?? "charge error").Then(_ => 1, Errors.MapAll);

    return await repo.Bump(p.Id, ex?.Message ?? "charge error").Then(_ => 0, Errors.MapAll);
  }
}
