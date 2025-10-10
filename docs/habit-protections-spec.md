Habit Protections: Skips, Freeze Days, Vacation Windows, Tier Limits

Overview

- Goal: prevent streak breaks and stake loss when protections apply.
- Protections: Skip (manual, today-only), Freeze Day (user-level), Vacation (date windows), and Tier-based limits.
- Enforcement layer: controllers (subscription via Lagos). Repositories only persist; no tier logic in DB.

Confirmed Rules

- Skip allowance scope: per-user monthly bucket across all habits.
- Skip time window: today-only in user’s local day; must be manually triggered.
- Freeze semantics: single user-level freeze day applies to ALL scheduled habits for that date. No login required.
- Vacation allowance: X per year means number of windows/periods (not days), tier-controlled.
- Weekly earn rule: per-habit perfect-week awards +1 freeze day to the user-level pool, capped by ComputeFreezeMax.
- Stakes: Skips, Frozen, and Vacation outcomes do not incur penalties; only Failed outcomes do.

Entitlements (Lagos)

- Enforced in controllers via ISubscriptionService.
- Proposed keys (examples):
  - ent.habits.max — max habits per user
  - ent.skips.monthly — monthly skips per user
  - ent.vacation.windows.yearly — number of vacation windows per year
  - ent.freeze.base — base freeze capacity by tier
- ComputeFreezeMax(tier, userMaxStreak) — policy function. The cap for user-level freeze days depends on current tier and the user’s max streak across habits.

Domain Model Additions (Mapper rules honored)

- Vacation windows
  - Domain: VacationRecord(StartDate, EndDate, Timezone), VacationPrincipal(Id, UserId, Record), Vacation (aggregate optional).
  - Data: VacationPeriodData(Id, UserId, StartDate, EndDate, Timezone, CreatedAt).
  - Mappers:
    - Create: VacationPeriodData ToData(this VacationRecord record)
    - Update: VacationPeriodData ToData(this VacationPeriodData data, VacationRecord record)
    - Data → Domain: ToRecord, ToPrincipal, ToDomain
  - Indexes: by UserId; by StartDate year. Overlap prevention handled in controller.
- Freeze days (user-level)
  - Domain: UserProtectionRecord(FreezeCurrent), UserProtectionPrincipal(UserId, Record).
  - Data: UserProtectionData(UserId PK, FreezeCurrent, UpdatedAt).
  - Ledger: FreezeAwardData(HabitId, WeekStart, AwardedAt) with unique(HabitId, WeekStart) to prevent double awards.
  - Consumption log: FreezeConsumptionData(UserId, Date, ConsumedAt) with unique(UserId, Date) to ensure exactly-once for a day.
  - Mappers as per conventions.

Controller Endpoints (new)

- POST /api/v1/users/{userId}/vacations
  - Body: VacationRecord
  - Enforce: ent.vacation.windows.yearly for StartDate.Year; reject overlaps; return Problem on denial.
- GET /api/v1/users/{userId}/vacations?year=&limit=&skip=
  - Search with pagination; no GetAll without filters.
- DELETE /api/v1/users/{userId}/vacations/{id}
  - Allow delete if window not started.
- PATCH /api/v1/users/{userId}/vacations/{id}/end-today
  - Shorten an active vacation window by setting EndDate to the user-local today; validate EndDate >= StartDate.
- POST /api/v1/habits/{userId}/{habitVersionId}/executions/skip
  - Today-only; verify habit ownership and scheduling; enforce monthly bucket (ent.skips.monthly) using user-local month boundaries; upsert Skipped for today.
- GET /api/v1/users/{userId}/protections
  - Returns freeze day balance and cap for convenience; non-authoritative (source of truth is DB + policy).

Tier Enforcement (controllers)

- Habits per user: on create, check CountHabitsForUser(userId) < ent.habits.max.
- Monthly skips: count Skipped executions in user-local month < ent.skips.monthly.
- Vacation windows per year: count windows for StartDate.Year < ent.vacation.windows.yearly.
- Freeze cap: clamp on award/consumption using ComputeFreezeMax(currentTier, userMaxStreakAcrossHabits).

MarkDailyFailures Algorithm (replaces raw fail insert)
Inputs: date (UTC-independent DateOnly), system batch scope. Execution windows respect habit scheduling.

1. Group by user

   - If endpoint input is per habit, resolve owning user for each habit and group; proposed: introduce per-user endpoint to avoid partial effects.

2. For each user U on date D (user-local):
   - Vacation check: if D is within any active VacationPeriod for U, insert Vacation executions for ALL U’s scheduled habits for D (idempotent), skip remaining steps.
   - Completion/skip presence: if any scheduled habit has Completed or Skipped on D, do NOT consume freeze day; fail only the remaining scheduled, non-executed habits.
   - Freeze consumption: if no Completed and no Skipped exist and at least one scheduled habit lacks an execution:
     - If FreezeConsumptionData(UserId=U, Date=D) exists, treat as already consumed; ensure Frozen executions exist for all scheduled, non-executed habits.
     - Else if UserProtectionData.FreezeCurrent > 0, atomically decrement by 1 and record FreezeConsumptionData(U,D), then insert Frozen executions for all scheduled, non-executed habits.
     - Else insert Failed for all remaining scheduled, non-executed habits.
   - Notes:
     - Insertions must be idempotent via unique (HabitVersionId, Date) on HabitExecutions.
     - “Forgot day” semantics: freeze day is consumed only if user had no completions/skips for that date.

Weekly Freeze Award (background job)

- At user-local end of week (Sunday→Saturday boundaries, same logic as existing StreakCalculator), for each habit H:
  - Perfect-week criterion: all scheduled days in week are Completed (Skipped/Frozen/Vacation do not count as success).
  - If met and not already awarded for H/weekStart, award +1 to UserProtectionData.FreezeCurrent (capped by ComputeFreezeMax) and write FreezeAwardData(H, weekStart).

Upgrade/Downgrade Behavior

- Habits per tier: block new creations beyond ent.habits.max; existing habits remain unchanged.
- Skips per month: apply new allowance forward; no retroactive changes.
- Vacation windows: existing windows remain; new windows must respect current annual allowance.
- Freeze cap changes: on downgrade, immediately truncate the user-level freeze balance to the new cap (ClampFreezeToCap); continue to clamp on award/consumption.

Data/Repository Additions (summary)

- Habit repository: CountHabitsForUser; CountUserSkipsForMonth; InsertSkip; GetActiveHabitVersions(userId, date); GetActiveHabitVersionsByIds(ids, date).
- Vacation repository: Create/Update/Delete/Search/ListActiveForUserOnDate.
- Protection repository: Get/Upsert protection; TryConsumeFreeze(userId, date) for atomic ledger + decrement; IncrementFreeze; ClampFreezeToCap (downgrades); award ledger; GetUserMaxStreakAcrossHabits.

API/DTO/Mapper Conventions

- Follow Record → Principal → Aggregate mapping rules.
- Update signatures: Update(Guid id, <Record> record).
- Keep mappers side-effect free; business logic in services/repositories.

Vertical Rollout Plan

- Phase 0 — Base Infrastructure

  - Add data models (VacationPeriodData, UserProtectionData, FreezeAwardData, FreezeConsumptionData) + migrations.
  - Add mappers and repositories; add ISubscriptionService and policy interfaces; no behavior changes yet.

- Phase 1 — Vacation Windows

  - Expose create/list/delete endpoints with controller-level entitlement checks and overlap validation.
  - Update mark-daily-failures flow to apply Vacation before failures.

- Phase 2 — Manual Skip (Today-Only)

  - Add endpoint to skip today for a habit; enforce monthly bucket (per-user) in user-local month.
  - Ensure mark-daily-failures respects existing Skipped executions (it already does via unique constraint).

- Phase 3 — Freeze Days (User-Level)

  - Implement freeze consumption logic in mark-daily-failures using single daily decrement + Frozen inserts across all scheduled habits.
  - Add GET protections endpoint to expose balance/cap.

- Phase 4 — Habit Limit by Tier

  - Enforce ent.habits.max at habit create (and optionally on enable toggle), returning a Problem on violation.

- Phase 5 — Weekly Award Job

  - Background job to award per-habit perfect-week → +1 user freeze day; cap via ComputeFreezeMax; use ledger to prevent duplicates.

- Phase 6 — API Shape Hardening
  - Introduce per-user mark-daily-failures endpoint (preferred) and deprecate habitIds input; maintain compatibility temporarily.

Edge Cases

- Partially completed day: if any habit Completed/Skipped on date, no freeze day is consumed; remaining unsatisfied scheduled habits may Fail or be manually Skipped.
- Disabled habits: excluded from scheduling.
- Timezones: user-local for skip windows, monthly buckets, weekly awards, and vacation evaluation; habit-local for scheduling and EOD boundaries.

Validation & Testing

- Unit tests: entitlement checks, overlap detection, month boundary math, award logic, cap clamping, idempotency (unique keys).
- Integration tests: end-to-end daily run with combinations (vacation/freeze/skip/completed/failed) and ensuring debts only on Failed.

Ops Notes

- Add metrics for failures, protected days, awards, and freeze consumption.
- Lint: run `pls lint` before merging; if unavailable locally, run `task lint`.
