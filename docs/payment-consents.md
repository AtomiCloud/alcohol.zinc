# Payment consents — purpose separation

Each user can hold **two** stored Airwallex consents, mirroring the card
networks' merchant-initiated-transaction (MIT) classification:

| Purpose        | MIT class         | Used by                           |
| -------------- | ----------------- | --------------------------------- |
| `penalty`      | unscheduled (COF) | penalty drain (habit failures)    |
| `subscription` | recurring         | subscription subscribe + renewals |

Consents live in their own table — `PaymentConsents(Id, PaymentCustomerId FK,
Purpose, ConsentId, Status, CreatedAt, UpdatedAt)` with a unique
`(PaymentCustomerId, Purpose)` index — so future purposes are a new enum value,
not a schema change. Disabling a consent deletes its row.

Why: recurring-labelled charges get better issuer approval rates and cleaner
chargeback treatment than unscheduled ones, and revoking one agreement must not
kill the other.

## How a consent gets its purpose

The client (argon/neon) creates the consent through the Airwallex drop-in and
sets `merchant_trigger_reason` there:

- penalty setup → `unscheduled`
- subscription checkout → `scheduled`

zinc never decides the purpose at creation time. The Airwallex
`payment_consent.verified` webhook carries `merchant_trigger_reason`;
`AirwallexEventAdapter` classifies `scheduled → Subscription`, anything else
(including legacy consents created before this split) `→ Penalty`, and the
webhook routes the consent id into the matching column pair.

## Rules encoded in the code

- `ChargeStoredConsentAsync(..., purpose)` only ever confirms against ITS
  purpose's consent and refuses (`NotFoundException`) when that consent is
  missing — the penalty consent can never pay for a subscription or vice versa
  (UnitTest/Payment/ConsentPurposeTests.cs).
- Every `IPaymentService` consent method defaults `purpose = Penalty`, keeping
  all pre-split call sites (penalty drain, habit flows) byte-compatible.
- The Logto `HasPaymentConsent` claim tracks the **penalty** consent only (it
  gates habit flows); subscription flows check the DB via
  `HasPaymentConsentAsync(userId, Subscription)`.
- `GET/DELETE /api/v1/payment/{userId}/consent?purpose=penalty|subscription`
  (default `penalty`).
- Account deletion revokes **all** consents (`DisableAllPaymentConsentsAsync`,
  best-effort, skips missing).

## Existing users

The `SeparatePaymentConsents` migration moves every pre-split consent from the
old `PaymentCustomers` columns into the table as the **penalty** consent (that
is what the single consent was used as), then drops the columns; `Down()`
restores them. Verified against a real Postgres round-trip (up → data present,
down → columns restored). Users subscribing for the first time are prompted for
the recurring consent by the checkout flow (argon ticket 86ey5m5bp).
