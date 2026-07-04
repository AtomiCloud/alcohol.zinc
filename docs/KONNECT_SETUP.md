# Kong Konnect Setup (Subscription Catalog Mirror)

zinc's subscription enforcement source of truth is **local**: `Subscription:` in
`App/Config/settings.yaml` (caps + prices per tier) and the `UserSubscriptions`
table (who is on which tier). Kong Konnect's Metering & Billing catalog is a
**mirror** for reporting/admin visibility — if Konnect is down, zinc keeps
working; the mirror retries via the `KonnectSyncedAt` watermark (daily worker).

**Money never flows through Konnect.** The monthly fee is charged via the
existing Airwallex stored-consent path (`IPaymentService.ChargeStoredConsentAsync`).

## 1. Token

Create a Konnect Personal Access Token (profile → Personal Access Tokens) or a
system-account token. Inject it per landscape as:

```
Atomi_HttpClient__KONNECT__BearerAuth=<kpat_...>
```

(Config key `HttpClient.KONNECT` in settings.yaml; base URL is regional, e.g.
`https://us.api.konghq.com`.)

## 2. Catalog objects (create once per Konnect org)

The Metering & Billing v3 API base is `https://{region}.api.konghq.com/v3/openmeter`.

> ⚠️ Konnect keys must match `^[a-z0-9]+(?:_[a-z0-9]+)*$` (snake_case, no dots).
> zinc's entitlement keys use dots (`ent.habits.max`), so the Konnect features
> use the underscore form. The mapping lives only in this doc and the Konnect
> catalog — zinc itself never reads these features.

### Features (4, static entitlements)

| Konnect feature key           | zinc EntitlementKey           |
| ----------------------------- | ----------------------------- |
| `ent_habits_max`              | `ent.habits.max`              |
| `ent_skips_monthly`           | `ent.skips.monthly`           |
| `ent_vacation_windows_yearly` | `ent.vacation.windows.yearly` |
| `ent_freeze_base`             | `ent.freeze.base`             |

```bash
curl -X POST "$BASE/features" -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"key": "ent_habits_max", "name": "Habits Max"}'
# repeat for the other three
```

### Plans (3) — must mirror `settings.{landscape}.yaml`

| Plan key   | Price  | habits | skips/mo | vacations/yr | freeze base |
| ---------- | ------ | ------ | -------- | ------------ | ----------- |
| `free`     | $0     | 10     | 10       | 3            | 7           |
| `pro`      | $5/mo  | 25     | 20       | 6            | 14          |
| `ultimate` | $15/mo | 100    | 60       | 12           | 30          |

Each plan: `currency` USD, `billing_cadence` P1M, one `default` phase whose
rate cards are (a) a `flat_fee` "Monthly Fee" card with the tier price and
(b) one `flat_fee` card per feature with price `0` and a **static**
`entitlement_template` (`config` = `{"value":N}`). API quirks learned live:

- `price` is required on every rate card — use flat `"0"` for entitlement-only cards.
- Omit fields instead of sending `null` (e.g. phase `duration`).
- After create, publish: `POST /plans/{planId}/publish`.
- Published plans are immutable — to change one: `POST /plans/{id}/archive`,
  `DELETE /plans/{id}`, recreate, publish.
- Customer `currency` must match the plan currency.

Full working payload example: see the create call in `docs/subscription-konnect.md`
history or replay `App/Modules/Subscription/Konnect/KonnectModels.cs` shapes.

## 3. What zinc mirrors at runtime

`KonnectGateway` (App/Modules/Subscription/Konnect/):

- `UpsertCustomer(userId)` → `GET /customers?filter[key]={userId}`, else `POST /customers`
  (key = zinc userId, `usage_attribution.subject_keys = [userId]`).
- `UpsertSubscription` → resolves plan via `filter[key]`, then `POST /subscriptions`
  (customer+plan, zinc status as `zinc_status` label) or
  `POST /subscriptions/{id}/change` (timing `immediate`).
  Cancelled rows mirror as the `free` plan.
- ⚠️ List filters MUST use `filter[...]` syntax — bare params (`?key=...`) are
  **silently ignored** and return an unfiltered page (verified live 2026-07-02).

## 4. Drift procedure

Config is truth. To change caps/prices: edit `settings.yaml` (all landscapes) →
deploy → update the Konnect plan (archive/recreate/publish) to match. Konnect
being stale only affects reporting, never enforcement.

## 5. Terraform (future)

The official `Kong/terraform-provider-konnect` does **not** support M&B
resources yet — tracked in
[issue #348](https://github.com/Kong/terraform-provider-konnect/issues/348).
When it ships, encode §2 as Terraform and drop the manual/curl setup.

## Test account state (2026-07-02, region US)

Created via API: the 4 features, published USD plans `free`/`pro`/`ultimate`
per the table above, test customer `zinc-test-user-001` subscribed to `pro`.
