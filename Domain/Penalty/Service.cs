using CSharp_Result;
using Domain.Exceptions;
using Domain.Notification;
using Domain.Payment;
using Microsoft.Extensions.Logging;

namespace Domain.Penalty;

public class PenaltyService(
  IPenaltyRepository repo,
  IPaymentService payment,
  IEmailNotifier notifier,
  ILogger<PenaltyService> logger
) : IPenaltyService
{
  public async Task<Result<int>> ProcessPending(int batchSize, int maxAttempts)
  {
    var reconciled = await this.ReconcileHeld(batchSize, maxAttempts);
    return await this.Drain(batchSize, maxAttempts).Then(n => n + reconciled, Errors.MapAll);
  }

  private async Task<Result<int>> Drain(int batchSize, int maxAttempts)
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
              if (p.Record.Attempts + 1 >= maxAttempts)
                await repo.MarkFailed(p.Id, $"unhandled: {ex.Message}");
              else
                await repo.Bump(p.Id, $"unhandled: {ex.Message}");
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

  // How long a Held row waits between provider look-ups. Also keeps a look-up clear of a confirm that
  // was already on its way to the provider when the charity was decommissioned (seconds).
  private static readonly TimeSpan HeldRecheck = TimeSpan.FromMinutes(10);

  // A charge put on hold by a charity decommission is never confirmed again. But if it already had a
  // payment intent, an earlier attempt may have been confirmed at the provider before the hold (or a
  // confirm was in flight). Look it up, WITHOUT confirming:
  //   SUCCEEDED -> the money was taken: Charged (credits the charity ledger once; payouts to a
  //                decommissioned charity stay stopped, so the money is held);
  //   otherwise -> stays Held; checked again later, up to maxChecks times, then left with a note to
  //                reconcile with the payment provider.
  private async Task<int> ReconcileHeld(int batchSize, int maxChecks)
  {
    var batch = await repo.GetHeldToReconcile(batchSize, maxChecks, HeldRecheck);
    if (!batch.IsSuccess())
    {
      logger.LogError(batch.FailureOrDefault(), "Loading held penalties to reconcile failed; continuing");
      return 0;
    }

    var settled = 0;
    foreach (var p in batch.Get())
    {
      var rec = p.Record;
      var intentId = rec.PaymentIntentId!;
      try
      {
        var lookup = await payment.RetrievePaymentIntentAsync(intentId);
        if (lookup.IsSuccess() && lookup.Get().Status == "SUCCEEDED")
        {
          var charged = await repo.MarkCharged(p.Id, intentId);
          if (!charged.IsSuccess())
          {
            logger.LogError(charged.FailureOrDefault(), "Recording held penalty {Id} as charged failed", p.Id);
            continue;
          }
          settled++;
          if (rec.UserId is { } userId && rec.HabitExecutionId is { } executionId)
            await notifier.NotifyPenaltyCharged(userId, rec.Amount, rec.CharityId, executionId, DateTime.UtcNow);
          continue;
        }

        var why = lookup.IsSuccess()
          ? $"status {lookup.Get().Status}"
          : lookup.FailureOrDefault()?.Message ?? "lookup error";
        var note = rec.Attempts + 1 >= maxChecks
          ? $"held: charity decommissioned; payment intent {intentId} still not taken ({why}) after {maxChecks} checks; reconcile with the payment provider"
          : $"held: charity decommissioned; payment intent {intentId} not taken ({why})";
        await repo.NoteHeldCheck(p.Id, note);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception e)
      {
        logger.LogError(e, "Reconciling held penalty {Id} threw; continuing", p.Id);
      }
    }
    return settled;
  }

  // Mark Failed and email the user, but only if the row was really marked: a row that became final
  // meanwhile (Charged, or Held by a charity decommission) gets no "payment failed" email.
  private async Task<Result<int>> FailAndNotify(PenaltyPrincipal p, string userId, Guid habitExecutionId, string error)
  {
    var failed = await repo.MarkFailed(p.Id, error);
    if (failed.IsSuccess() && failed.Get())
      await notifier.NotifyPenaltyFailed(userId, p.Record.Amount, p.Record.CharityId, habitExecutionId, DateTime.UtcNow);
    return failed.Then(_ => 1, Errors.MapAll);
  }

  private int LeftOnHold(PenaltyPrincipal p)
  {
    logger.LogInformation("Penalty {Id} was put on hold mid-charge (charity decommissioned); not charged", p.Id);
    return 1;
  }

  private async Task<Result<int>> ProcessOne(PenaltyPrincipal p, int maxAttempts)
  {
    var rec = p.Record;
    // A penalty detached from its user or execution (account deletion sets both to NULL) is never
    // charged: the person and their payment consent are gone. It is closed without confirming or
    // creating any payment (see CloseDetached).
    if (rec.UserId is not { } userId || rec.HabitExecutionId is not { } habitExecutionId)
      return await this.CloseDetached(p, maxAttempts);

    // Deterministic idempotency key tied to this penalty row: a retried or
    // concurrent charge for the same penalty collapses onto the same Airwallex
    // intent instead of minting a new one. On a retry of a row that already
    // recorded an intent, reconcile that intent rather than re-charging.
    //
    // If the charity is decommissioned while this charge is in flight, the penalty is moved on hold and
    // SetIntentId refuses with PenaltyHeldException before the intent is confirmed: no money moves, and
    // the row is left Held (no retry, no failure, no email).
    var heldMidFlight = false;
    Result<PaymentIntentResult> chargeResult;
    try
    {
      chargeResult = await payment.ChargeStoredConsentAsync(
        userId, rec.Amount, $"Habit penalty {habitExecutionId}",
        idempotencyKey: p.Id.ToString(),
        existingIntentId: rec.PaymentIntentId,
        // Persist the intent id the moment it's created (before confirm) so a confirm
        // failure doesn't lose it; a persistence failure surfaces so the attempt retries.
        onIntentCreated: async intentId =>
        {
          Result<Unit> saved;
          try
          {
            saved = await repo.SetIntentId(p.Id, intentId);
          }
          catch (PenaltyHeldException)
          {
            heldMidFlight = true;
            throw;
          }
          if (!saved.IsSuccess())
          {
            var err = saved.FailureOrDefault();
            if (err is PenaltyHeldException) heldMidFlight = true;
            throw err ?? new Exception($"SetIntentId failed for penalty {p.Id}");
          }
        });
    }
    catch (PenaltyHeldException)
    {
      return this.LeftOnHold(p);
    }

    if (heldMidFlight) return this.LeftOnHold(p);

    if (chargeResult.IsSuccess())
    {
      var intent = chargeResult.Get();
      if (intent.Status == "SUCCEEDED")
      {
        // Terminal success: charge + atomic charity credit. Propagate a
        // persistence failure (the credit's transaction rolling back) instead
        // of silently counting it as success.
        var charged = await repo.MarkCharged(p.Id, intent.Id).Then(_ => 1, Errors.MapAll);
        if (charged.IsSuccess())
          await notifier.NotifyPenaltyCharged(userId, rec.Amount, rec.CharityId, habitExecutionId, DateTime.UtcNow);
        return charged;
      }

      // REQUIRES_PAYMENT_METHOD / REQUIRES_CUSTOMER_ACTION / REQUIRES_CAPTURE -> not yet settled, retry.
      var attempts = rec.Attempts + 1;
      if (attempts >= maxAttempts)
      {
        return await this.FailAndNotify(p, userId, habitExecutionId, $"Max attempts reached, last status {intent.Status}");
      }

      return await repo.MarkPending(p.Id, intent.Id, attempts).Then(_ => 0, Errors.MapAll);
    }

    // Failure branch.
    var ex = chargeResult.FailureOrDefault();

    // No verified consent -> Skipped (typed NotFoundException(PaymentCustomer) from ChargeStoredConsentAsync).
    if (ex is NotFoundException)
      return await repo.MarkSkipped(p.Id).Then(_ => 1, Errors.MapAll);

    // Transient/other error -> bump; exhaust to Failed.
    if (rec.Attempts + 1 >= maxAttempts)
    {
      return await this.FailAndNotify(p, userId, habitExecutionId, ex?.Message ?? "charge error");
    }

    return await repo.Bump(p.Id, ex?.Message ?? "charge error").Then(_ => 0, Errors.MapAll);
  }

  // Close a penalty detached by an account deletion. Never confirms, creates or re-tries a charge.
  //  - No payment intent yet: no money can have moved -> Skipped.
  //  - A payment intent exists (an earlier attempt created it, and may have settled before the worker
  //    could record it): look it up WITHOUT confirming and settle the ledger from the provider's answer:
  //      SUCCEEDED                          -> Charged (MarkCharged credits the charity exactly once);
  //      CANCELLED                          -> Skipped (definitively unpaid);
  //      anything else, or a lookup error   -> retried on later drains; after maxAttempts it is marked
  //                                            Failed with a note to reconcile with the provider.
  // No email: there is no user to send it to.
  private async Task<Result<int>> CloseDetached(PenaltyPrincipal p, int maxAttempts)
  {
    var rec = p.Record;
    if (rec.PaymentIntentId is not { } intentId)
      return await repo.MarkSkipped(p.Id).Then(_ => 1, Errors.MapAll);

    var lookup = await payment.RetrievePaymentIntentAsync(intentId);
    if (lookup.IsSuccess())
    {
      var intent = lookup.Get();
      if (intent.Status == "SUCCEEDED")
        return await repo.MarkCharged(p.Id, intent.Id).Then(_ => 1, Errors.MapAll);
      // Only a cancelled intent is definitively unpaid. REQUIRES_PAYMENT_METHOD can still be confirmed
      // (e.g. by a worker that claimed this row before the deletion), so it stays under reconciliation.
      if (intent.Status == "CANCELLED")
        return await repo.MarkSkipped(p.Id).Then(_ => 1, Errors.MapAll);
    }

    var why = lookup.IsSuccess()
      ? $"status {lookup.Get().Status}"
      : lookup.FailureOrDefault()?.Message ?? "lookup error";
    logger.LogWarning("Detached penalty {Id}: payment intent {IntentId} not settled yet ({Why})", p.Id, intentId, why);
    if (rec.Attempts + 1 >= maxAttempts)
      return await repo.MarkFailed(p.Id,
          $"Detached by account deletion; payment intent {intentId} still unsettled ({why}); reconcile with the payment provider")
        .Then(_ => 1, Errors.MapAll);
    return await repo.Bump(p.Id, $"Detached by account deletion; reconciling payment intent {intentId} ({why})")
      .Then(_ => 0, Errors.MapAll);
  }
}
