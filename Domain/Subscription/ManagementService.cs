using CSharp_Result;
using Domain.Exceptions;
using Domain.Notification;
using Domain.Payment;
using Microsoft.Extensions.Logging;
using NodaMoney;

namespace Domain.Subscription;

public class SubscriptionManagementService(
  ISubscriptionRepository repo,
  ISubscriptionPlanProvider plans,
  IPaymentService payment,
  IEmailNotifier notifier,
  ILogger<SubscriptionManagementService> logger
) : ISubscriptionManagementService
{
  public async Task<Result<UserSubscriptionPrincipal>> Subscribe(string userId, string tier)
  {
    if (tier == SubscriptionService.FreeTier)
      return new InvalidSubscriptionTierException(tier);

    var planRes = plans.GetPlan(tier);
    if (!planRes.IsSuccess()) return planRes.FailureOrDefault()!;
    var plan = planRes.Get();

    var existingRes = await repo.GetByUserId(userId);
    if (!existingRes.IsSuccess()) return existingRes.FailureOrDefault()!;
    var existing = existingRes.Get();

    if (existing?.Record.Status is SubscriptionStatus.Active or SubscriptionStatus.Grace
        && existing.Record.Tier == tier)
    {
      // Re-subscribing the same tier after a pending cancel = un-cancel, free of charge.
      if (existing.Record.CancelAtPeriodEnd)
        return await repo.SetCancelAtPeriodEnd(existing.Id, false)
          .ThenAwait(_ => repo.GetByUserId(userId))
          .Then(s => s!, Errors.MapNone);

      return new AlreadySubscribedException(existing.Record.Tier, tier);
    }

    // A Grace user must settle their current tier via the daily renewal retry
    // (or cancel) before switching; switching would bypass the unpaid period.
    if (existing?.Record.Status is SubscriptionStatus.Grace)
      return new AlreadySubscribedException(existing.Record.Tier, tier);

    var isTierSwitch = existing?.Record.Status is SubscriptionStatus.Active;
    SubscriptionPlan? currentPlan = null;
    if (isTierSwitch)
    {
      var currentPlanRes = plans.GetPlan(existing!.Record.Tier);
      if (!currentPlanRes.IsSuccess()) return currentPlanRes.FailureOrDefault()!;
      currentPlan = currentPlanRes.Get();

      // Only an upgrade (strictly higher price) charges immediately. A cheaper
      // (or equal-priced) tier is a downgrade: schedule it for the period roll
      // instead of charging now and destroying the paid remainder.
      if (plan.Price.Amount <= currentPlan.Price.Amount)
        return await this.ScheduleDowngrade(userId, existing, tier);

      // The renewal worker holds this row's charge lease: an upgrade Activate
      // now would strand the renewal charge it may be settling. Reject and let
      // the client retry once the (short) lease lapses.
      if (existing.Record.RenewingUntil is { } lease && lease > DateTime.UtcNow)
        return new SubscriptionBusyException(userId);
    }

    // Subscriptions charge against the RECURRING consent — a user who only has
    // the penalty (unscheduled) consent must set the subscription one up first.
    var hasConsentRes = await payment.HasPaymentConsentAsync(userId, ConsentPurpose.Subscription);
    if (!hasConsentRes.IsSuccess()) return hasConsentRes.FailureOrDefault()!;
    if (!hasConsentRes.Get()) return new NoPaymentConsentException(userId);

    var now = DateTime.UtcNow;
    var periodStart = now;
    var periodEnd = now.AddMonths(1);
    var chargeAmount = plan.Price;
    var chargeDescription = $"LazyTax {tier} subscription";
    var chargeKey = $"sub-{userId}-{tier}-{now:yyyyMMdd}";

    Guid rowId;
    string? storedIntentId;
    string? storedChargeKey;
    if (isTierSwitch)
    {
      // Immediate upgrade: do NOT touch the active row before the charge
      // succeeds — a failed upgrade must leave the current tier intact.
      rowId = existing!.Id;
      storedIntentId = existing.Record.LastChargeIntentId;
      storedChargeKey = existing.Record.LastChargeKey;

      // PRORATED upgrade: the user already paid the current tier for this
      // period, so charge only the price DIFFERENCE for the remaining fraction
      // of it — and keep the billing anniversary (the next renewal charges the
      // full new-tier price on the existing PeriodEnd). Rounded DOWN to the
      // cent, in the user's favor.
      periodStart = existing.Record.PeriodStart;
      periodEnd = existing.Record.PeriodEnd;
      var periodSeconds = (periodEnd - periodStart).TotalSeconds;
      var remainingSeconds = Math.Max(0d, (periodEnd - now).TotalSeconds);
      var fraction = periodSeconds <= 0 ? 0m : (decimal)(remainingSeconds / periodSeconds);
      var proratedCents = Math.Floor((plan.Price.Amount - currentPlan!.Price.Amount) * fraction * 100m);
      chargeAmount = new Money(proratedCents / 100m, plan.Price.Currency);
      chargeDescription = $"LazyTax {tier} subscription upgrade (prorated)";
      // The -upg- namespace can never collide with (or be paid by) a subscribe
      // or renewal intent; retries on the same day reconcile the same intent.
      chargeKey = $"sub-{userId}-{tier}-upg-{now:yyyyMMdd}";
    }
    else
    {
      // First-time (or re-)subscribe: park a PendingActivation row so the charge
      // attempt has somewhere durable to persist its intent id. It grants no
      // tier until activated.
      var upserted = await repo.Upsert(new UserSubscriptionRecord
      {
        UserId = userId,
        Tier = tier,
        Status = SubscriptionStatus.PendingActivation,
        PeriodStart = periodStart,
        PeriodEnd = periodEnd,
        CancelAtPeriodEnd = false,
        NextTier = null,
        LastChargeIntentId = existing?.Record.LastChargeIntentId,
        LastChargeKey = existing?.Record.LastChargeKey
      });
      if (!upserted.IsSuccess()) return upserted.FailureOrDefault()!;
      rowId = upserted.Get().Id;
      storedIntentId = upserted.Get().Record.LastChargeIntentId;
      storedChargeKey = upserted.Get().Record.LastChargeKey;
    }

    // Upgrading moments before the anniversary can prorate to $0: flip the tier
    // without a charge attempt — the imminent renewal bills the full new price.
    if (isTierSwitch && chargeAmount.Amount <= 0m)
    {
      var flipped = await repo.Activate(rowId, tier, periodStart, periodEnd, now);
      if (!flipped.IsSuccess()) return flipped.FailureOrDefault()!;
      if (!flipped.Get()) return new SubscriptionBusyException(userId);
      return await repo.GetByUserId(userId).Then(s => s!, Errors.MapNone);
    }

    // Tier + date in the key so this logical charge is distinct per tier and per
    // day. A stored intent is only reconciled when it was created under the SAME
    // key — a leftover intent from a different tier or period has a different
    // price and must never be able to "pay" for this charge.
    var existingIntentId = storedChargeKey == chargeKey ? storedIntentId : null;

    var chargeRes = await payment.ChargeStoredConsentAsync(
      userId, chargeAmount, chargeDescription,
      idempotencyKey: chargeKey,
      existingIntentId: existingIntentId,
      onIntentCreated: async intentId =>
      {
        var saved = await repo.SetIntentId(rowId, intentId, chargeKey);
        if (!saved.IsSuccess())
          throw saved.FailureOrDefault() ?? new Exception($"SetIntentId failed for subscription {rowId}");
      },
      purpose: ConsentPurpose.Subscription);

    if (!chargeRes.IsSuccess())
    {
      var ex = chargeRes.FailureOrDefault();
      if (ex is NotFoundException) return new NoPaymentConsentException(userId);
      return ex!;
    }

    var intent = chargeRes.Get();
    if (intent.Status != "SUCCEEDED")
      return new SubscriptionChargeFailedException(intent.Status);

    var activated = await repo.Activate(rowId, tier, periodStart, periodEnd, DateTime.UtcNow);
    if (!activated.IsSuccess()) return activated.FailureOrDefault()!;

    if (!activated.Get())
    {
      // A renewal claimed the row between our lease check and the charge. The
      // charge is settled and its intent is recorded under our key, so the
      // client's retry reconciles it without re-charging — surface busy.
      logger.LogWarning(
        "Activation rejected for subscription {Id}: renewal lease live; charge {Intent} awaits retry reconcile",
        rowId, intent.Id);
      return new SubscriptionBusyException(userId);
    }

    var result = await repo.GetByUserId(userId).Then(s => s!, Errors.MapNone);
    if (result.IsSuccess())
      await notifier.NotifySubscriptionPurchased(userId, tier, chargeAmount, periodEnd);
    return result;
  }

  public async Task<Result<UserSubscriptionPrincipal>> Cancel(string userId)
  {
    var existingRes = await repo.GetByUserId(userId);
    if (!existingRes.IsSuccess()) return existingRes.FailureOrDefault()!;
    var existing = existingRes.Get();

    if (existing?.Record.Status is not (SubscriptionStatus.Active or SubscriptionStatus.Grace))
      return new NoActiveSubscriptionException(userId);

    var cancelled = await repo.SetCancelAtPeriodEnd(existing.Id, true)
      .ThenAwait(_ => repo.GetByUserId(userId))
      .Then(s => s!, Errors.MapNone);
    if (cancelled.IsSuccess())
      await notifier.NotifySubscriptionChanged(
        userId, existing.Record.Tier, SubscriptionService.FreeTier, existing.Record.PeriodEnd);
    return cancelled;
  }

  public async Task<Result<UserSubscriptionPrincipal>> ChangeTier(string userId, string tier)
  {
    if (tier == SubscriptionService.FreeTier)
      return await this.Cancel(userId);

    var existingRes = await repo.GetByUserId(userId);
    if (!existingRes.IsSuccess()) return existingRes.FailureOrDefault()!;
    var existing = existingRes.Get();

    // Same active tier: the only meaning left is undoing a scheduled change.
    if (existing?.Record.Status is SubscriptionStatus.Active && existing.Record.Tier == tier
        && (existing.Record.CancelAtPeriodEnd || existing.Record.NextTier is not null))
    {
      var undoNext = await repo.SetNextTier(existing.Id, null);
      if (!undoNext.IsSuccess()) return undoNext.FailureOrDefault()!;
      if (existing.Record.CancelAtPeriodEnd)
      {
        var uncancel = await repo.SetCancelAtPeriodEnd(existing.Id, false);
        if (!uncancel.IsSuccess()) return uncancel.FailureOrDefault()!;
      }

      return await repo.GetByUserId(userId).Then(s => s!, Errors.MapNone);
    }

    // Everything else (new subscribe, upgrade, scheduled downgrade, same-tier
    // no-op rejection) shares Subscribe's decision table.
    return await this.Subscribe(userId, tier);
  }

  private async Task<Result<UserSubscriptionPrincipal>> ScheduleDowngrade(
    string userId, UserSubscriptionPrincipal existing, string tier)
  {
    var setNext = await repo.SetNextTier(existing.Id, tier);
    if (!setNext.IsSuccess()) return setNext.FailureOrDefault()!;

    // A downgrade request supersedes a pending cancel: the user wants a cheaper
    // tier, not to leave.
    if (existing.Record.CancelAtPeriodEnd)
    {
      var uncancel = await repo.SetCancelAtPeriodEnd(existing.Id, false);
      if (!uncancel.IsSuccess()) return uncancel.FailureOrDefault()!;
    }

    var scheduled = await repo.GetByUserId(userId).Then(s => s!, Errors.MapNone);
    if (scheduled.IsSuccess())
      await notifier.NotifySubscriptionChanged(userId, existing.Record.Tier, tier, existing.Record.PeriodEnd);
    return scheduled;
  }

  public async Task<Result<int>> ProcessRenewals(DateTime nowUtc, int batchSize)
  {
    return await repo.GetDue(nowUtc, batchSize)
      .ThenAwait(async due =>
      {
        var settled = 0;
        foreach (var snapshot in due)
        {
          try
          {
            // Re-read fresh: the batch snapshot can be minutes old, and an
            // interactive Subscribe/upgrade may have already rolled this row —
            // renewing from stale state would double-charge and clobber the
            // period the user just bought.
            var freshRes = await repo.GetByUserId(snapshot.Record.UserId);
            if (!freshRes.IsSuccess())
            {
              logger.LogError(freshRes.FailureOrDefault(),
                "Renewal re-read failed for {UserId}; skipping", snapshot.Record.UserId);
              continue;
            }

            var fresh = freshRes.Get();
            if (fresh is null
                || fresh.Record.Status is not (SubscriptionStatus.Active or SubscriptionStatus.Grace)
                || fresh.Record.PeriodEnd > nowUtc)
              continue; // no longer due (renewed/cancelled/changed since the scan)

            var res = await this.RenewOne(fresh, nowUtc);
            if (res.IsSuccess()) settled += res.Get();
            else logger.LogError(res.FailureOrDefault(), "Renewal failed for subscription {Id}", fresh.Id);
          }
          catch (OperationCanceledException)
          {
            throw;
          }
          catch (Exception ex)
          {
            // Isolate per subscription: one bad row must not strand the batch.
            logger.LogError(ex, "RenewOne threw for subscription {Id}; isolating and continuing", snapshot.Id);
          }
        }

        return (Result<int>)settled;
      });
  }

  private async Task<Result<int>> RenewOne(UserSubscriptionPrincipal sub, DateTime nowUtc)
  {
    var rec = sub.Record;

    if (rec.CancelAtPeriodEnd)
    {
      var cancelled = await repo.MarkCancelled(sub.Id);
      if (!cancelled.IsSuccess()) return cancelled.FailureOrDefault()!;
      return 1;
    }

    var targetTier = rec.NextTier ?? rec.Tier;
    var planRes = plans.GetPlan(targetTier);
    if (!planRes.IsSuccess()) return planRes.FailureOrDefault()!;
    var plan = planRes.Get();

    // Grace window exhausted -> downgrade to free.
    if (rec.Status == SubscriptionStatus.Grace
        && nowUtc > rec.PeriodEnd.AddDays(plan.GracePeriodDays))
    {
      var lapsed = await repo.MarkCancelled(sub.Id);
      if (!lapsed.IsSuccess()) return lapsed.FailureOrDefault()!;
      await notifier.NotifySubscriptionEnded(rec.UserId, rec.Tier, nowUtc);
      return 1;
    }

    // Claim the row before any money moves: while the lease is held, Subscribe
    // rejects upgrades on this row (and Activate is lease-guarded), so a
    // succeeded renewal charge can never be stranded by a concurrent Activate
    // resetting the period underneath it.
    var leaseUntil = nowUtc.AddMinutes(5);
    var claimRes = await repo.TryClaimRenewal(sub.Id, rec.PeriodEnd, leaseUntil, nowUtc);
    if (!claimRes.IsSuccess()) return claimRes.FailureOrDefault()!;
    if (!claimRes.Get()) return 0; // period changed or another pass owns it

    try
    {
      // The renewal key namespace for this period: the plain base key for the
      // first attempt, base-r{day} for grace retries.
      var baseKey = $"sub-{rec.UserId}-{targetTier}-{rec.PeriodEnd:yyyyMMdd}";

      // Step 1 — reconcile: if a previous attempt of THIS renewal left an
      // in-flight intent, settle it first. This is what recovers a "charge
      // succeeded but the response was lost" crash without double-charging.
      // Only keys from this renewal's namespace are reconciled — a leftover
      // intent from a crashed upgrade (different tier ⇒ different price) must
      // never be confirmed here.
      if (rec.LastChargeIntentId is not null && rec.LastChargeKey is not null
          && (rec.LastChargeKey == baseKey || rec.LastChargeKey.StartsWith($"{baseKey}-r")))
      {
        var reconcile = await payment.ChargeStoredConsentAsync(
          rec.UserId, plan.Price, $"LazyTax {targetTier} subscription renewal",
          idempotencyKey: rec.LastChargeKey,
          existingIntentId: rec.LastChargeIntentId,
          purpose: ConsentPurpose.Subscription);

        if (reconcile.IsSuccess() && reconcile.Get().Status == "SUCCEEDED")
          return await this.RollRenewedPeriod(sub, targetTier);

        // Only a DEFINITIVE decline (REQUIRES_PAYMENT_METHOD after a confirm
        // attempt) may fall through to a fresh attempt. A transient error
        // (gateway 5xx) or any status that could still settle without us —
        // including REQUIRES_CUSTOMER_ACTION, whose 3DS challenge Airwallex
        // documents as completable out-of-band — must wait for tomorrow's
        // pass: minting a new intent beside one that later settles would
        // charge the period twice.
        if (!reconcile.IsSuccess()
            || reconcile.Get().Status is not "REQUIRES_PAYMENT_METHOD")
        {
          logger.LogWarning(
            "Renewal reconcile inconclusive for subscription {Id} (intent {Intent}); retrying next pass",
            sub.Id, rec.LastChargeIntentId);
          return 0;
        }
      }

      // Step 2 — fresh attempt. The first attempt of a period uses the plain
      // period key; every grace-day retry after that appends the day so it
      // mints a NEW intent — otherwise Airwallex's request_id dedupe would
      // replay the day-1 decline forever and the card never gets a real retry.
      var attemptKey = rec.Status == SubscriptionStatus.Grace
        ? $"{baseKey}-r{nowUtc:yyyyMMdd}"
        : baseKey;

      var chargeRes = await payment.ChargeStoredConsentAsync(
        rec.UserId, plan.Price, $"LazyTax {targetTier} subscription renewal",
        idempotencyKey: attemptKey,
        existingIntentId: null,
        onIntentCreated: async intentId =>
        {
          var saved = await repo.SetIntentId(sub.Id, intentId, attemptKey);
          if (!saved.IsSuccess())
            throw saved.FailureOrDefault() ?? new Exception($"SetIntentId failed for subscription {sub.Id}");
        },
        purpose: ConsentPurpose.Subscription);

      if (chargeRes.IsSuccess() && chargeRes.Get().Status == "SUCCEEDED")
        return await this.RollRenewedPeriod(sub, targetTier);

      // Not settled (declined, requires action, no consent, transient error):
      // enter/stay in Grace and let tomorrow's pass retry until the window lapses.
      // The Active->Grace transition happens exactly once, so the failure email
      // fires once — daily grace retries stay silent.
      if (rec.Status != SubscriptionStatus.Grace)
      {
        var graced = await repo.MarkGrace(sub.Id);
        if (!graced.IsSuccess()) return graced.FailureOrDefault()!;
        await notifier.NotifySubscriptionPaymentFailed(
          rec.UserId, rec.Tier, rec.PeriodEnd.AddDays(plan.GracePeriodDays));
      }

      return 0;
    }
    finally
    {
      var released = await repo.ReleaseRenewal(sub.Id, leaseUntil);
      if (!released.IsSuccess())
        logger.LogWarning(released.FailureOrDefault(),
          "Failed releasing renewal lease for subscription {Id}; it self-expires", sub.Id);
    }
  }

  private async Task<Result<int>> RollRenewedPeriod(UserSubscriptionPrincipal sub, string targetTier)
  {
    // The new period starts where the old one ended (grace days were consumed as
    // service). The repo guard (PeriodEnd must still equal the old value) makes a
    // stale roll a no-op instead of clobbering a concurrent upgrade.
    var rolled = await repo.RollPeriod(
      sub.Id, targetTier, sub.Record.PeriodEnd, sub.Record.PeriodEnd.AddMonths(1));
    if (!rolled.IsSuccess()) return rolled.FailureOrDefault()!;
    if (!rolled.Get())
    {
      logger.LogWarning(
        "Renewal roll skipped for subscription {Id}: period changed concurrently", sub.Id);
      return 0;
    }

    // The rolled guard above is the idempotency point: reconcile and fresh-charge
    // paths both land here, but only the pass that actually rolls emails.
    await notifier.NotifySubscriptionRenewed(
      sub.Record.UserId, targetTier, sub.Record.PeriodEnd.AddMonths(1));
    return 1;
  }

}
