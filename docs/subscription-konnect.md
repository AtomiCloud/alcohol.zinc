# Subscriptions via Kong Konnect — Design

**Status:** Draft / research
**Service:** alcohol.zinc (habit-accountability API, .NET 8, Clean Architecture)
**Author:** research pass, 2026-07-02

---

## 1. TL;DR

- zinc **already has** the subscription seam: `ISubscriptionService` (`GetUserTier` + `GetLimitForTier(tier, key)`) and `EntitlementService` enforce freeze / skip / vacation / habit caps today. The current impl is a stub (`NullSubscriptionService`, "temporary until Lagos-backed service is integrated").
- The plan is to make **paid tiers** where the plan sets those caps, with **Kong Konnect** as the subscription platform.
- Kong Konnect Metering & Billing (M&B, powered by OpenMeter) maps cleanly:
  **Plan → Features → Metered Entitlements (`issueAfterReset` = allowance, `usagePeriod` = reset window, hard limit).**
- **Key gap finding:** OpenMeter has **no subscription pause and no proration yet** ("coming soon"). _But that gap does not bite us_ — in zinc, freeze/skip/vacation are **in-plan allowances**, they do **not** pause the subscription fee. The fee is flat.
- **Key architecture finding:** zinc already does merchant-initiated **recurring** charging via **Airwallex** (`ChargeStoredConsentAsync`). So Konnect billing (→ Stripe) would be a **second processor**. Decision needed: use Konnect only as the **plan/entitlement catalog** (bill the fee via existing Airwallex) vs. use Konnect's full billing → Stripe.

---

## 2. The gap we were asked to investigate

> Does Konnect natively handle the billing semantics of freeze / vacation / skip?

### 2.1 What Konnect (OpenMeter) _does_ give us — great fit

Metered entitlements are exactly the primitive for "N per period, auto-reset":

| Field                                                     | Meaning                             | Our use                                                |
| --------------------------------------------------------- | ----------------------------------- | ------------------------------------------------------ |
| `type: metered`                                           | usage-limited feature               | skips / vacations / habits                             |
| `usagePeriod: { interval: MONTH \| YEAR }`                | reset cadence                       | `SkipsMonthly` = MONTH, `VacationWindowsYearly` = YEAR |
| `issueAfterReset: N`                                      | allowance granted each reset        | the tier's cap                                         |
| `isSoftLimit: false`                                      | hard limit → `hasAccess=false` at 0 | enforce the cap                                        |
| value endpoint → `{ hasAccess, balance, usage, overage }` | live balance check                  | what `EnsureSkipsAllowed` needs                        |

Consumption is a CloudEvents usage event (`POST /openmeter/events`, `subject = customerId`); a Meter (`count`/`sum`) aggregates it and decrements the balance.

### 2.2 What Konnect does **not** do (the real gaps)

1. **No automatic blocking.** "Kong does not block when an entitlement is exhausted." You read `hasAccess` and enforce in your own service. → zinc already enforces locally, so this is a non-issue.
2. **No subscription pause / resume.**
3. **No proration** on mid-cycle change (listed "coming soon").
4. **No documented one-time credit / period-skip** on an invoice.

### 2.3 Why the gaps don't bite zinc

The gaps (2, 3, 4) only matter if a freeze/vacation/skip is supposed to **pause or credit the money**. In zinc's domain it isn't:

- **skip** = skip one habit occurrence without penalty (consumes a monthly allowance).
- **vacation** = a window where habits are paused without penalty (consumes a yearly-window allowance).
- **freeze** = streak protection tokens (`FreezeBase` cap fed into `FreezePolicy`).

None of these change the **subscription price**. The subscription fee is flat per tier. So we never need OpenMeter pause/proration. The allowances are pure **entitlement counters**, which OpenMeter _does_ support.

### 2.4 Second, subtler finding — zinc already counts usage itself

`EntitlementService` enforces by counting in zinc's own DB:

- `vacationRepository.CountWindowsForYear`
- `habitRepository.CountUserSkipsForMonth`
- `habitRepository.CountHabitsForUser`

It only needs the **limit** from the tier. So Konnect's _metering_ (emit events, query `balance`) is **redundant** with zinc's local counting. We need Konnect for **plan definitions + which plan a user is on + (optionally) fee billing** — not for counting.

---

## 3. Current state in zinc (grounding)

```
Domain/Subscription/ISubscriptionService.cs
  GetUserTier(userId)            -> "free" | "pro" | ...
  GetLimitForTier(tier, key)     -> int cap for an EntitlementKey

App/StartUp/Registry EntitlementKeys
  ent.habits.max
  ent.skips.monthly
  ent.vacation.windows.yearly
  ent.freeze.base

App/Modules/Entitlement/EntitlementService.cs   (IEntitlementService)
  EnsureVacationWindowAllowed / EnsureSkipsAllowed / GetFreezeCapForUser / EnsureHabitsAllowed
  -> reads tier + limit from ISubscriptionService, counts usage locally, throws TierInsufficient

App/StartUp/Services/Subscription/NullSubscriptionService.cs   <-- STUB to replace
  free tier, hardcoded caps (habits 10, skips 10, vacations 3, freeze 7)

Payment (already live, Airwallex):
  Domain/Payment/IPaymentGateway.cs  -> AirwallexGateway
  IPaymentService.ChargeStoredConsentAsync(...)  <-- merchant-initiated RECURRING charge
                                                     (used today for penalty drains)
```

**Enforcement stays exactly where it is.** The only thing that changes is where `ISubscriptionService` gets its answers: stub → Konnect-backed.

---

## 4. Konnect entity mapping

| zinc concept                  | Kong Konnect / OpenMeter entity                                                               |
| ----------------------------- | --------------------------------------------------------------------------------------------- |
| Tier (`free`, `pro`, …)       | **Plan**                                                                                      |
| `ent.habits.max`              | **Feature** + **static** or metered entitlement, limit = cap                                  |
| `ent.skips.monthly`           | **Feature** + **metered** entitlement, `usagePeriod=MONTH`, `issueAfterReset=cap`, hard limit |
| `ent.vacation.windows.yearly` | **Feature** + **metered** entitlement, `usagePeriod=YEAR`, `issueAfterReset=cap`              |
| `ent.freeze.base`             | **Feature** + **static** entitlement, value = base cap (fed to `FreezePolicy`)                |
| user↔tier                    | **Subscription** (customer on a plan)                                                         |
| monthly tier fee              | **Rate card** flat price → invoice → Stripe _(only if Konnect owns billing)_                  |
| charity invoice               | **open — see §6**                                                                             |

Feature keys should mirror `EntitlementKeys` so `GetLimitForTier(tier, key)` is a direct lookup.

---

## 5. Three integration shapes (pick one)

### Option A — Konnect as **catalog only** (recommended, lowest risk)

- Konnect owns **Plans + entitlement limits + which plan a user is on**.
- `GetUserTier` → Konnect subscription's plan key (webhook-synced into a local `user_subscription` table).
- `GetLimitForTier` → Konnect plan entitlement limit (cache/sync).
- **Billing:** the flat tier fee is charged by **zinc via existing Airwallex** `ChargeStoredConsentAsync` on a monthly cron. **No Stripe, single processor.**
- zinc keeps local counting/enforcement unchanged.
- ✅ Avoids OpenMeter billing immaturity; one processor; smallest change.
- ⚠️ We hand-roll the monthly-fee cron + dunning (but the charging primitive already exists).

### Option B — Konnect owns **billing** too

- Konnect **Subscription** drives the recurring fee → **Stripe** (M&B payments integration).
- zinc reads tier via webhook, enforces locally.
- ✅ Real recurring billing, invoices, tax handled by Konnect.
- ⚠️ **Two processors:** Stripe (subscription fee) + Airwallex (penalty drains). Reconciliation, two consent flows, two dashboards. OpenMeter proration/pause still immature (but unused here).

### Option C — Konnect as **catalog + live metering** (not recommended)

- As A/B, plus zinc emits a usage event to Konnect on every skip/freeze/vacation and reads `hasAccess`/`balance` instead of counting locally.
- ⚠️ Dual source of truth vs. zinc's own counts; Konnect can't auto-block anyway; more failure modes. No benefit given §2.4.

**Recommendation:** **Option A.** It reuses the Airwallex recurring capability zinc already has, keeps one processor, and uses Konnect for what it's genuinely good at here (plan + entitlement catalog + subscription state). Move to B only if finance wants Konnect/Stripe to own invoicing + tax.

---

## 6. Open question — "charity invoice"

Needs product clarification. In zinc, penalties are forfeited to charities (`Domain/Charity`, `Domain/Disbursement`), and those flow through **Airwallex**, not Konnect. Candidate meanings:

1. **Donation receipt** for penalty money already sent to charity (a document/PDF, not a charge) — belongs in zinc/Disbursement, unrelated to Konnect billing.
2. **Optional donation add-on** billed alongside the subscription — a Konnect **Feature/rate card** line (works in Option B; in Option A it's an Airwallex line).
3. **Higher tiers unlock charity features** — just another **entitlement** on the plan.

➡️ **Decide which before building.** It changes whether "charity" is a Konnect billable feature, a zinc-generated receipt, or a plan entitlement flag.

---

## 7. Proposed implementation (Option A)

1. **`KonnectSubscriptionService : ISubscriptionService`** in `App/StartUp/Services/Subscription/`, registered in `DomainServices.cs` in place of `NullSubscriptionService`.
   - `GetUserTier` → local `user_subscription` table (kept fresh by Konnect webhook).
   - `GetLimitForTier(tier, key)` → local `plan_entitlement` cache synced from Konnect plans (key == `EntitlementKeys`).
2. **Konnect sync:** webhook endpoint (subscription created/updated/canceled) → upsert `user_subscription`; periodic pull of plan/entitlement definitions → `plan_entitlement`.
3. **Fee billing (Option A):** monthly cron → for each active paid subscription, `IPaymentService.ChargeStoredConsentAsync(userId, tierPrice, "…", idempotencyKey: subscriptionPeriodId)`. Reuse the existing idempotency/reconcile path.
4. **Config:** Konnect base URL, org/system-account token (Ingest not needed for Option A; read scope for plans), plan→price map. Follow the landscape config pattern (lapras/pichu/pikachu/raichu).
5. **EntitlementService, EntitlementKeys, enforcement — unchanged.**

### Failure modes to cover

- Konnect webhook missed → stale tier. Mitigation: periodic reconcile pull + treat unknown as `free`.
- Fee charge fails (no valid Airwallex consent) → dunning/grace + downgrade to `free` after N retries.
- Tier downgrade mid-period with usage already over the new cap → enforcement blocks _new_ actions only (existing counts stand); confirm desired behavior.

---

## 8. Decisions needed

1. **Billing ownership:** Option A (Airwallex-only) vs B (Konnect→Stripe). _(Recommend A.)_
2. **"Charity invoice" meaning** (§6).
3. **Is M&B enabled** for AtomiCloud's Konnect org/tier? (M&B is new — the OpenMeter acquisition.)
4. **Plan matrix:** concrete tiers × caps (habits / skips / vacations / freeze) × price × currency.
5. **Proration on upgrade/downgrade:** since OpenMeter proration is "coming soon" and Option A bills via Airwallex anyway — do we prorate at all, or change tier at next period only?

---

## Appendix — sources

- Kong M&B overview — https://developer.konghq.com/metering-and-billing/
- M&B get-started — https://developer.konghq.com/metering-and-billing/get-started/
- Metering / event schema — https://developer.konghq.com/metering-and-billing/metering/
- OpenMeter entitlements — https://openmeter.io/docs/billing/entitlements/entitlement
- OpenMeter subscription edit (pause/proration gaps) — https://openmeter.io/docs/billing/subscription/edit
- Kong acquires OpenMeter — https://konghq.com/blog/news/kong-acquires-openmeter
