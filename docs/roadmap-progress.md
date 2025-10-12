Habit Protections Roadmap & Progress

Overview

- Scope: Skip (monthly allowance), Freeze Days (user-level), Vacation Windows (periods), Tier/Entitlements, Habit limits, and related controllers/services.
- Enforcement Location: Controllers (tier/entitlements), with dedicated services for allowance/time, entitlement checks, and business persistence.

Status Summary (2025-10-12)

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

- Phase 3 — Freeze Days (User-Level): COMPLETED

  - Daily failure flow consumes one user-level freeze day (if available) and inserts Frozen executions for all scheduled habits when no completion/skip exists (Domain/Habit/Service.cs:84–107, App/Modules/Habit/Data/HabitRepository.cs:483–516, 340–460).
  - GET protections endpoint returns balance and cap; cap now computed using tier base + user max streak (App/Modules/Protection/API/V1/ProtectionController.cs:1–200, App/Modules/Entitlement/EntitlementService.cs:1–120, App/Modules/Habit/Data/StreakRepository.cs:300–380).
  - Protection repository supports idempotent consumption and award ledgers (App/Modules/Protection/Data/ProtectionRepository.cs:1–200).

- Phase 4 — Habit Limit by Tier: COMPLETED

  - Enforce EntitlementKeys.HabitsMax on habit create and enable toggle in controller via EntitlementService.EnsureHabitsAllowed (App/Modules/Habit/API/V1/HabitController.cs:1–200, App/Modules/Entitlement/EntitlementService.cs:1–120).

- Phase 5 — Weekly Award Job: COMPLETED (now event-driven)
  - External event triggers per-user award via `POST /api/v1/Protection/award-weekly`.
  - Server evaluates prior week and awards +1 freeze per perfect habit week using FreezeAwardData ledger; clamps to cap via EntitlementService (App/Modules/Protection/ProtectionAwardService.cs, ProtectionController, Startup DI).

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

- Monitor weekly award job performance and add backoff/retry if needed.
- Consider moving MarkDailyFailures to per-user API to avoid partial effects.
- Tighten metrics around freeze consumption vs. failures for visibility.

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
