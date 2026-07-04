using CSharp_Result;
using Domain.Exceptions;
using Domain.Payment;
using Microsoft.Extensions.Logging;

namespace Domain.Subscription;

public class SubscriptionManagementService(
  ISubscriptionRepository repo,
  ISubscriptionPlanProvider plans,
  IPaymentService payment,
  IKonnectGateway konnect,
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
    if (isTierSwitch)
    {
      var currentPlanRes = plans.GetPlan(existing!.Record.Tier);
      if (!currentPlanRes.IsSuccess()) return currentPlanRes.FailureOrDefault()!;

      // Only an upgrade (strictly higher price) charges immediately. A cheaper
      // (or equal-priced) tier is a downgrade: schedule it for the period roll
      // instead of charging now and destroying the paid remainder.
      if (plan.Price.Amount <= currentPlanRes.Get().Price.Amount)
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
        KonnectCustomerId = existing?.Record.KonnectCustomerId,
        KonnectSyncedAt = existing?.Record.KonnectSyncedAt,
        LastChargeIntentId = existing?.Record.LastChargeIntentId,
        LastChargeKey = existing?.Record.LastChargeKey
      });
      if (!upserted.IsSuccess()) return upserted.FailureOrDefault()!;
      rowId = upserted.Get().Id;
      storedIntentId = upserted.Get().Record.LastChargeIntentId;
      storedChargeKey = upserted.Get().Record.LastChargeKey;
    }

    // Tier + date in the key so this logical charge is distinct per tier and per
    // day. A stored intent is only reconciled when it was created under the SAME
    // key — a leftover intent from a different tier or period has a different
    // price and must never be able to "pay" for this charge.
    var chargeKey = $"sub-{userId}-{tier}-{periodStart:yyyyMMdd}";
    var existingIntentId = storedChargeKey == chargeKey ? storedIntentId : null;

    var chargeRes = await payment.ChargeStoredConsentAsync(
      userId, plan.Price, $"LazyTax {tier} subscription",
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

    await this.MirrorBestEffort(rowId, userId);

    return await repo.GetByUserId(userId).Then(s => s!, Errors.MapNone);
  }

  public async Task<Result<UserSubscriptionPrincipal>> Cancel(string userId)
  {
    var existingRes = await repo.GetByUserId(userId);
    if (!existingRes.IsSuccess()) return existingRes.FailureOrDefault()!;
    var existing = existingRes.Get();

    if (existing?.Record.Status is not (SubscriptionStatus.Active or SubscriptionStatus.Grace))
      return new NoActiveSubscriptionException(userId);

    return await repo.SetCancelAtPeriodEnd(existing.Id, true)
      .ThenAwait(_ => repo.GetByUserId(userId))
      .Then(s => s!, Errors.MapNone);
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

    return await repo.GetByUserId(userId).Then(s => s!, Errors.MapNone);
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
      await this.MirrorBestEffort(sub.Id, rec.UserId);
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
      await this.MirrorBestEffort(sub.Id, rec.UserId);
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
      if (rec.Status != SubscriptionStatus.Grace)
      {
        var graced = await repo.MarkGrace(sub.Id);
        if (!graced.IsSuccess()) return graced.FailureOrDefault()!;
        await this.MirrorBestEffort(sub.Id, rec.UserId);
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

    await this.MirrorBestEffort(sub.Id, sub.Record.UserId);
    return 1;
  }

  public async Task<Result<int>> ProcessKonnectMirror(int batchSize)
  {
    return await repo.GetUnsynced(batchSize)
      .ThenAwait(async unsynced =>
      {
        var synced = 0;
        foreach (var sub in unsynced)
        {
          if (await this.MirrorOne(sub)) synced++;
        }

        return (Result<int>)synced;
      });
  }

  // Best-effort mirror after a state change: failures are logged only — the row's
  // KonnectSyncedAt watermark stays stale so ProcessKonnectMirror retries later.
  private async Task MirrorBestEffort(Guid id, string userId)
  {
    try
    {
      var subRes = await repo.GetByUserId(userId);
      if (!subRes.IsSuccess() || subRes.Get() is not { } sub) return;
      await this.MirrorOne(sub);
    }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "Konnect mirror threw for subscription {Id}; will retry via worker", id);
    }
  }

  private async Task<bool> MirrorOne(UserSubscriptionPrincipal sub)
  {
    try
    {
      var rec = sub.Record;
      var customerRes = rec.KonnectCustomerId is { } cid
        ? (Result<string>)cid
        : await konnect.UpsertCustomer(rec.UserId);
      if (!customerRes.IsSuccess())
      {
        logger.LogWarning(customerRes.FailureOrDefault(),
          "Konnect customer upsert failed for {UserId}; will retry via worker", rec.UserId);
        return false;
      }

      var customerId = customerRes.Get();
      // Cancelled/PendingActivation mirror as the free tier; Konnect only needs
      // the effective entitlement-granting plan.
      var effectiveTier = rec.Status is SubscriptionStatus.Active or SubscriptionStatus.Grace
        ? rec.Tier
        : SubscriptionService.FreeTier;

      var subRes = await konnect.UpsertSubscription(
        customerId, effectiveTier, rec.PeriodStart, rec.PeriodEnd, rec.Status.ToString());
      if (!subRes.IsSuccess())
      {
        logger.LogWarning(subRes.FailureOrDefault(),
          "Konnect subscription upsert failed for {UserId}; will retry via worker", rec.UserId);
        return false;
      }

      var marked = await repo.MarkKonnectSynced(sub.Id, customerId, DateTime.UtcNow);
      if (!marked.IsSuccess())
      {
        logger.LogWarning(marked.FailureOrDefault(),
          "MarkKonnectSynced failed for subscription {Id}", sub.Id);
        return false;
      }

      return true;
    }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "Konnect mirror threw for subscription {Id}; will retry via worker", sub.Id);
      return false;
    }
  }
}
