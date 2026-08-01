# Subscription Lifecycle

How subscriptions work in zinc: every scenario, what is implemented, and what is planned.

- Engine: `Domain/Subscription/ManagementService.cs`
- Read side / entitlements: `Domain/Subscription/Service.cs`
- Config: `App/Config/settings.yaml` → `Subscription:` (single source of truth for tiers, prices, caps — keep argon `pricing.ts` in sync)
- Portal: `alcohol.argon` `/billing`

Supersedes the billing parts of `subscription-konnect.md` (Konnect was removed 2026-07-06).

## States

One subscription row per user (unique `UserId`). Four statuses, two flags:

```mermaid
stateDiagram-v2
    [*] --> PendingActivation: subscribe (no access yet)
    PendingActivation --> Active: charge SUCCEEDED
    Active --> Active: upgrade (prorated, same period)<br/>or renewal (period rolls)
    Active --> Grace: renewal charge failed<br/>(1 email, paid access kept)
    Grace --> Active: daily retry succeeds
    Grace --> Cancelled: grace window exhausted
    Active --> Cancelled: cancelAtPeriodEnd + period ends
    Cancelled --> PendingActivation: re-subscribe
```

- Scheduled downgrade is **not** a status. It is the `NextTier` field, applied at the period roll.
- Pending cancel is the `CancelAtPeriodEnd` flag.
- Undo = flip the field back. No rows are added or deleted. (History comes from the planned event table, below.)
- Read-side backstop: `EffectiveTier` drops a user to `free` from timestamps alone, even if the renewal worker is off. Access is always correct; the stored `Status` may lag.

## Proration rules

| Action                          | Prorate?                | Behavior                                                                                                   |
| ------------------------------- | ----------------------- | ---------------------------------------------------------------------------------------------------------- |
| First subscribe / re-subscribe  | No — full price now     | New 1-month period starts today                                                                            |
| Upgrade (strictly higher price) | **Yes — the only case** | `(new − old) × remaining fraction of period`, floored to the cent (user's favour). Renewal date unchanged. |
| Downgrade (price ≤ current)     | No                      | Scheduled at period end. No refund, no credit.                                                             |
| Cancel / paid → free            | No                      | Access until period end. No refund.                                                                        |
| Renewal                         | No                      | Full price on the anniversary                                                                              |

## Scenarios

### Signing up

1. **Free → Pro / Ultimate** — Full month charged now; period = now → now+1 month. Requires a verified subscription-purpose Airwallex consent, else `NoPaymentConsent`. Charge fails → row stays `PendingActivation`, no access.
2. **Re-subscribe after cancelled** — Same as fresh signup: full price, new period.

### Upgrading (Pro → Ultimate)

3. **Normal upgrade** — Immediate. Charges only the prorated difference (e.g. 20 days left ≈ $2.00, not $7.99). Rounded down. Renewal date does not move.
4. **Upgrade just before renewal** — Prorated amount ≤ $0 → tier flips free of charge; next renewal bills full new price.
5. **Upgrade during a live renewal** — Rejected with `SubscriptionBusy` (5-min renewal lease). Retry shortly.

### Downgrading (Ultimate → Pro)

6. **Normal downgrade** — Nothing changes today. `NextTier` set; applied at period roll. New price billed from then. No refund.
7. **Undo before it happens** — `ChangeTier` to the current tier clears `NextTier`. No charge.

### Cancelling (paid → Free)

8. **Cancel** — `CancelAtPeriodEnd = true`. Full access until period end, then `Cancelled` → effective `free`. No refund, no further charges.
9. **Resume before period end** — Flag cleared. No charge; original renewal date stands.
10. **Cancel, then pick a cheaper plan** — The scheduled downgrade replaces the cancellation. Current plan until period end, then switch.

### Renewal day

11. **Payment succeeds** — Period rolls one month; receipt sent.
12. **Payment fails → Grace** — Paid features kept. Exactly one payment-failure email (on the Active→Grace edge). Card retried daily, silently, with a fresh idempotency key per day.
13. **Retry succeeds during grace** — Back to `Active`, period rolls, receipt sent.
14. **Grace runs out** — Window = `GracePeriodDays` (7, global) **measured from the original period end**, not from each retry. Then `Cancelled`, effective `free`, one "subscription ended" email. Nothing deleted.
15. **Tier change while in Grace** — Rejected. Settle the payment or cancel first.

### Limits after downgrade

16. **More habits than the new cap** — Policy (decided 2026-08-01): never delete. Oldest habits stay active up to the new cap; the rest become read-only/paused. Upgrading reactivates them.
    Implementation (2026-08-01): `Habits.PausedByLimit` flag, distinct from the user's own `Enabled` toggle. Paused habits are skipped by the daily failure scan (no penalties, streak frozen), rejected on complete/skip/update with `TierInsufficient`, and don't occupy a creation slot (delete an active habit → create a fresh one). Reconciliation (`IEntitlementService.ReconcileHabitPause` → `SetPausedOverCap`) runs on every tier landing — activate, $0 flip, renewal roll, lapse, cancel-end — ranking **currently-active first, then oldest first, Id tiebreak**, so a monthly roll never re-pauses a habit the user swapped in. Best-effort: a reconcile failure never fails the money flow; the next roll self-heals.
17. **Skips / vacation windows already used this period** — They stand. No claw-back. Existing vacation windows are not cancelled.

## Implementation status

### Built and live

Scenarios 1–10, 16, 17. Enforcement gates (`EntitlementService`) block _new_ over-cap actions; over-cap pausing (scenario 16, built 2026-08-01) reconciles existing habits on every tier landing. Unit-tested.

### Built, enabled in pichu only 🟡

Scenarios 11–15. The renewal worker (`SubscriptionRenewalHostedService`, daily tick, batch 500, DB lease + idempotency keys) is gated by `Subscription.RenewalEnabled`: **`true` in pichu** (as of 2026-08-01), still `false` in the base config and every other landscape. Observe a full pichu cycle (renew / grace / lapse), then enable pikachu → raichu. Until then the read-side backstop protects access elsewhere.

### Not built ❌

- **`pausedByLimit` in habit API responses** — server enforces pausing, but neon/argon can't render the paused badge yet (comes with the UX step).
- **Receipts / billing history UI** — the data now exists (`SubscriptionEvents` + `GET /subscription/{userId}/events`, built 2026-08-02); the argon page on top is still open.
- **Reconcile-retry sweeper** — a failed pause reconcile after a lapse now has a durable `lapsed` event row to retry from, but nothing sweeps it yet.
- **Grace countdown in UI** — backend computes the deadline; API/portal never expose it. Portal still says "Renews on …" during grace.
- **Billing preview endpoint** — the portal estimates the prorated upgrade client-side from `periodStart`/`periodEnd` (fixed 2026-08-01); a zinc-authoritative preview endpoint is still open.
- **Undo-downgrade button** — backend supports it (#7); portal renders no affordance.
- **`payment_intent.*` webhooks** — commented out; a 3DS-later-settled charge only recovers via the daily reconcile (pichu-only today).
- Annual billing, trials, coupons, refunds — out of scope for now.

## Append-only billing event table (built 2026-08-02)

The live `UserSubscriptions` row stays the single source of _current_ truth. History goes to `SubscriptionEvents` — append-only (never updated, never deleted), written best-effort after each state/money change in `SubscriptionManagementService`, served newest-first by `GET /api/v1/subscription/{userId}/events`:

```text
SubscriptionEvents
├─ Id, UserId, OccurredAt (UTC)
├─ EventType: activated | upgraded | downgradeScheduled | downgradeUndone |
│             cancelScheduled | resumed | renewed | chargeFailed |
│             graceEntered | lapsed | cancelled
├─ Tier / NextTier snapshot, PeriodEnd
├─ AmountCents, Currency, ChargeIntentId (money events)
└─ Detail (short human-readable context, e.g. gateway status)
```

A `renewed` event whose Tier differs from the previous one is a scheduled downgrade landing. Appends are best-effort: the state/money write has already committed, so a failed append only costs a history row and is logged loudly. No FK to Users — financial history must survive account deletion (anonymize-retain, same seam as the penalty ledger).

Written in the same transaction/flow as each state change. Powers: receipts + billing history page, audit ("did we ever schedule this downgrade?"), dunning/ops debugging, and future invoices.

## Rollout order

1. ~~Fix argon upgrade copy~~ — done 2026-08-01 (argon PR #102).
2. ~~Enable `RenewalEnabled` in pichu~~ — done 2026-08-01 (this repo); **monitor a full cycle**, then promote to pikachu → raichu.
3. ~~Implement #16 (pause over-cap habits, oldest-first keep)~~ — done 2026-08-01.
4. Event table → then receipts, grace countdown, billing preview, undo-downgrade button, `pausedByLimit` in habit responses.
