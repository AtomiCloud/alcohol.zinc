# Developer Documentation

This file contains technical implementation details and patterns used in this codebase.

## Financial Data Storage

This codebase uses specific patterns for storing financial data with precision to avoid floating point errors and ensure accurate calculations.

### Money Storage - Cents + Currency

**Money values** are stored as whole numbers (cents) plus currency code in the database, and use NodaMoney in the domain layer.

#### Implementation Pattern

**Domain Layer (Business Logic):**

```csharp
using NodaMoney;

public record HabitRecord
{
    public Money Stake { get; init; }  // e.g., Money(10.50, "SGD")
}
```

**Data Layer (Database Storage):**

```csharp
public class HabitData
{
    public int StakeCents { get; set; }      // 1050 (for $10.50)
    public string StakeCurrency { get; set; } = "SGD";
}
```

**Mapping Between Layers:**

```csharp
// Domain → Data (storing)
data.StakeCents = (int)(principal.Record.Stake.Amount * 100);  // $10.50 * 100 = 1050 cents
data.StakeCurrency = principal.Record.Stake.Currency.Code;     // "SGD"

// Data → Domain (loading)
Stake = new Money(data.StakeCents / 100m, Currency.FromCode(data.StakeCurrency))  // 1050/100 = $10.50 SGD
```

#### Benefits

- ✅ **No floating point precision errors** for monetary calculations
- ✅ **Integer arithmetic** is exact and fast
- ✅ **Multi-currency support** with proper currency handling
- ✅ **NodaMoney integration** provides rich money operations in domain layer

### Percentage Storage - Basis Points

**Basis points** (bp or bps) are used throughout this codebase for storing percentage values with precision.

### Definition

- **1 basis point = 0.01%** (one hundredth of a percent)
- **100 basis points = 1%**
- **10,000 basis points = 100%**

### Examples

```
25.5% = 2,550 basis points
50% = 5,000 basis points
0.25% = 25 basis points
100% = 10,000 basis points
```

### Implementation in Habit System

**Domain Layer (Business Logic):**

```csharp
public record HabitRecord
{
    public decimal Ratio { get; init; }  // 0.255 (25.5%)
}
```

**Data Layer (Database Storage):**

```csharp
public class HabitData
{
    public int RatioBasisPoints { get; set; }  // 2550 (25.5% as basis points)
}
```

**Mapping Between Layers:**

```csharp
// Domain → Data (storing)
data.RatioBasisPoints = (int)(principal.Record.Ratio * 10000);  // 0.255 * 10000 = 2550

// Data → Domain (loading)
Ratio = data.RatioBasisPoints / 10000m  // 2550 / 10000 = 0.255
```

### Benefits

- ✅ **No floating point precision errors** when storing percentages
- ✅ **Integer storage** in database (faster, more reliable than decimals)
- ✅ **Industry standard** for financial percentage calculations
- ✅ **Precise calculations** for money transfers and charity donations

## Usage in Codebase

These financial storage patterns are used throughout:

### Money (Cents + Currency)

- **Habit stakes**: User monetary commitments for habit accountability
- **Charity donations**: Failed-habit penalties are charged to the user's card and later paid out
  to charities via Pledge (see [Charity payout](#charity-payout-disbursement) below)
- **Any monetary values**: Precise financial calculations without rounding errors

### Percentages (Basis Points)

- **Habit ratios**: Percentage of stake money going to charity
- **Financial calculations**: Ensuring exact penny-accurate transfers
- **Any percentage-based business logic**: Avoiding decimal precision issues

## Guidelines for Developers

1. **Always use NodaMoney.Money** in domain models for monetary values
2. **Always store as cents + currency** in data models
3. **Always use decimal** for percentages in domain models
4. **Always store as basis points** in data models
5. **Convert only at domain/data boundaries** - never mix types within a layer
6. **Use mappers** to handle conversions between storage and business representations

This ensures all financial calculations are precise and audit-compliant.

## Charity payout (disbursement)

How a failed habit's money actually reaches the charity, end to end:

1. **Charge** (existing): a failed habit execution enqueues a `Penalty`. `PenaltyProcessorHostedService`
   charges the user's saved card via Airwallex (MIT). On success the penalty is `Charged` and the
   `(charity, currency)` balance is credited. **The money lands in our (LazyTax) Airwallex account.**
2. **Claim + donate** (this feature): `DisbursementHostedService` runs daily. It groups
   `Charged` penalties with `DisbursementId IS NULL` by `(charity, currency)`, and for each group
   creates a `Disbursement` row (status `Pending`) and stamps those penalties' `DisbursementId`
   **in one row-locked transaction** — so a retried/parallel pass can never re-select the same
   penalties and donate twice. It then records a donation to the charity via the **Pledge
   Donations API** (`POST /v1/donations`), keyed by the `Disbursement.Id` (stored as Pledge
   `metadata`). On success the disbursement is `Completed`; on failure it is `Failed` and the
   penalties are **released** (`DisbursementId → NULL`) for a later retry.
3. **Reconcile**: if we crash after the donation lands but before recording it, the next pass finds
   the stale `Pending` disbursement and looks it up at Pledge by `metadata`. Found → mark
   `Completed` (never re-donate); not found → release for a clean retry.

### ⚠️ How settlement _actually_ works (important)

`POST /v1/donations` **does not move money in real time** — it records a donation on Pledge's
ledger. Pledge then **batches and bills LazyTax's payment method on file** in the Impact Hub.
Confirmed terms from the staging Impact Hub (Fees and Schedule, 2026-06-29):

- **Billing schedule:** billed **monthly on the 1st**, _or_ sooner when the pending amount reaches
  the **$50 threshold** (both the billing date and threshold are editable in the Impact Hub).
- **Fees:** **Donation API fee = 5% + payment processing (2.9% + 30¢ per _batch_ charge, not per
  donation).** The charity nets ~92% of the gross. `Disbursement.AmountCents` is the **gross** we
  ask Pledge to donate; Pledge deducts its fee before the charity is paid. Our ledger/reconciliation
  tracks the gross recorded, not the net the charity receives.
- **Fee model (decided):** **LazyTax takes 0% and does NOT absorb the fee.** We donate the full
  amount collected from the user; the donation platform (Pledge) deducts its API + processing fee,
  and the charity receives the remainder. So we donate as-collected — there is **no gross-up** and
  **no LazyTax cut**. This is already reflected in the Terms of Service ("100% Donation Policy" /
  "Fee Transparency": LazyTax donates 100% of penalties _after_ third-party Airwallex + Pledge fees,
  which it does not control or profit from; LazyTax acts as donor, not the user's agent).

So:

- **Two separate money flows.** (a) We collect penalties into our **Airwallex** account. (b) Pledge
  **separately bills LazyTax's payment method on file in the Pledge Impact Hub** (a card/bank set up
  there) for the batched donation total, then pays the charities. Our code drives flow (a) and the
  _recording_ of (b) — it does **not** transfer funds to Pledge.
- **Operational dependency (not code):** the Pledge Impact Hub payment method must exist and stay
  **funded** (e.g. topped up from Airwallex), or the monthly batch bill fails. This must be set up
  in **both** the Pledge sandbox (for testing) and production (before go-live).
- **Fee optimization (the cadence that matters is Pledge's, not ours):** our donation-worker
  schedule (how often `POST /v1/donations` runs) has **zero** fee impact — creating donations only
  adds to Pledge's running tally; it does not trigger a charge. Fees are set by **Pledge's batch
  billing** + the **payment method**:
  - **5% API fee** and the **percentage processing fee** are proportional to the money, so they are
    cadence-independent.
  - The **flat/cap part** is charged **once per batch charge to our card** (Pledge batches monthly
    on the 1st, or sooner at the **$50 threshold** — both editable in the Impact Hub): **card =
    2.9% + $0.30**, **ACH = 0.8% capped at $5**.
  - So to minimize fees: prefer **ACH** (capped at $5/batch) and a **larger batch threshold** (fewer
    batches → the flat/cap applies fewer times). Trade-off: a higher threshold means money sits at
    Pledge longer before the charity is paid. E.g. a $1,000 batch costs ≈ $29.30 on card vs **$5 on
    ACH** (plus the 5% API fee either way).
- **`Disbursement.Completed` means "donation recorded at Pledge", not "charity has been paid."**
  True settlement is Pledge's monthly batch. The reconciliation invariant
  (`sum(charged) == sum(disbursed) + outstanding`) tracks _recorded_ payouts, not settled cash.
- **Currency:** the donation body has **no currency field** (Pledge bills the account's currency).
  Multi-currency penalty groups (e.g. SGD + USD) are recorded per-currency on our side but may
  settle in a single currency at Pledge — confirm against the account before relying on it.

### Safety: never donate against production Pledge in dev

Donations on **`api.pledge.to`** move real money. All dev/testing uses the **staging** host
**`api-staging.pledge.to`** (a fully isolated sandbox). `pichu` is wired to staging and is the only
landscape with the disbursement worker enabled (`Disbursement.Enabled: true`). The base host is set
per-landscape under `HttpClient.PLEDGE.BaseAddress`.
