Habit Protections Roadmap & Progress

Overview

- Scope: Skip (monthly allowance), Freeze Days (user-level), Vacation Windows (periods), Tier/Entitlements, Habit limits, and related controllers/services.
- Enforcement Location: Controllers (tier/entitlements), with dedicated services for allowance/time, entitlement checks, and business persistence.

Status Summary (2025-10-10)

- Phase 0 — Base Infrastructure: COMPLETED

  - Data models: VacationPeriodData, UserProtectionData, FreezeAwardData, FreezeConsumptionData
  - Mappers: Data <=> Domain for vacation/protection
  - Repositories: Habit (helpers), Vacation (CRUD), Protection (freeze ops)
  - DI wiring for repos/services

- Phase 1 — Vacation Windows: COMPLETED

  - API: Create/List/Delete/End-today
  - Controller checks: tier-based entitlement (vacation windows per year) using ISubscriptionService and EntitlementKeys
  - Repo-level overlap check: HasOverlap(userId, start, end)
  - Count API: CountWindowsForYear(userId, year)
  - Service: VacationService validates ranges/overlaps; no subscription logic
  - Mappers: API <=> Domain for vacation (Create/Res, Search.ToDomain)

- Phase 2 — Manual Skip (Today-only): COMPLETED

  - API: POST /habits/{userId}/{habitVersionId}/executions/skip
  - Controller checks: monthly skip entitlement via tier-based limit
  - AllowanceService: GetUserMonthWindow(userId) returns user-local month window and now
  - EntitlementService: EnsureSkipsAllowed(userId, monthStart, monthEnd)
  - Repo: SkipHabit(userId, habitVersionId, date, notes) idempotent insert; CountUserSkipsForMonth(userId, window)

- Phase 3 — Freeze Days (User-Level): PENDING

  - Implement consumption (user-level freeze day) across all scheduled habits via daily failure orchestration
  - GET protections endpoint for balance/cap
  - Tier cap enforcement in controller (EntitlementService extension): ComputeFreezeMax via tier + max streak

- Phase 4 — Habit Limit by Tier: PENDING

  - Enforce entitlements (EntitlementKeys.HabitsMax) on create/enable; controller-only, service remains pure CRUD

- Phase 5 — Weekly Award Job: PENDING
  - Award +1 freeze for per-habit perfect week; record ledger (FreezeAwardData) to avoid duplicates
  - Clamp to cap at award time using EntitlementService

Key Services & Responsibilities

- AllowanceService (NEW)

  - GetUserMonthWindow(userId[, utcNow])
  - GetUserToday(userId[, utcNow])

- EntitlementService (NEW)

  - EnsureVacationWindowAllowed(userId, startDate)
  - EnsureSkipsAllowed(userId, monthStart, monthEnd)
  - Uses: ISubscriptionService (tier + GetLimitForTier), Vacation/Habit repos for count

- ISubscriptionService

  - GetUserTier(userId)
  - GetLimitForTier(tier, key) — decoupled entitlement lookup by tier
  - Registry Keys: App.StartUp.Registry.EntitlementKeys

- VacationService

  - Create: Validates ranges; guards with repo.HasOverlap
  - Delete/EndToday: Uses repo.Get(id, userId) for user-scoped fetch

- HabitService
  - SkipHabit: wraps GetUserCurrentDate + repo.SkipHabit
  - CompleteHabit: (unchanged)
  - MarkDailyFailures: applies Vacation protections first, then failures

Controller Patterns (updated)

- VacationController

  - Create: req.ToRecord() → EntitlementService.EnsureVacationWindowAllowed → VacationService.Create → ToRes()
  - List: Search mapper ToDomain(userId)
  - Delete / End-today: delegate to VacationService methods

- HabitController
  - Skip: AllowanceService.GetUserMonthWindow → EntitlementService.EnsureSkipsAllowed → HabitService.SkipHabit → ToRes()
  - Complete: unchanged
  - GetOverview/GetExecutions/Search: unchanged aside from minor mapper usage

Centralized Mapping

- HabitExecutionMapper: public ToDataStatus(ExecutionStatus)
- Vacation mappers: API Create/Res + Search.ToDomain

Open Items / Next Steps

- Phase 3: Freeze Days

  - Consumption on daily-failure job: If no completion/skip and user freeze day available, freeze all scheduled habits (user-level)
  - Controller endpoint for protections (GET balance/cap)
  - EntitlementService: EnsureFreezeAllowed(userId) with tier cap + clamp helper

- Phase 4: Habit Limits

  - EntitlementService.EnsureHabitsAllowed(userId) using repo.CountHabitsForUser + tier limit
  - Apply on habit create/enable

- Phase 5: Weekly Awards
  - Background job: award per-habit perfect week; use FreezeAwardData ledger; clamp to cap
  - Ensure cap via EntitlementService (tier + policy)

Testing & Ops

- Unit: overlap/entitlement checks, AllowanceService windows, EntitlementService checks
- Integration: end-to-end skip/vacation flows; daily failures respect protections
- Metrics: failures, protected days, awards, freeze consumption

Changelog (high-level)

- Added: AllowanceService, EntitlementService, NullSubscriptionService stub for ISubscriptionService
- Added: EntitlementKeys, vacation count/overlap repo methods
- Added: skip endpoint and repo/service for Skipped executions
- Refactored: controller-level entitlement checks; services focus on business/persistence
- Centralized: status mapping via HabitExecutionMapper
