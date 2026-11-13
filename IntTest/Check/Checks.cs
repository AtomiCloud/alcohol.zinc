using App.Modules.Check.Data;
using App.Modules.Entitlement;
using App.Modules.Habit.Data;
using App.Modules.Penalty.Data;
using App.Modules.Protection;
using App.Modules.Protection.Data;
using App.Modules.Vacation.Data;
using App.StartUp.Database;
using App.StartUp.Registry;
using CSharp_Result;
using Domain.Check;
using Domain.Protection;
using Domain.Subscription;
using Microsoft.Extensions.Logging.Abstractions;

namespace IntTest.Check;

// Builds the real CheckService over a real MainDbContext, with the charity penalty as its stake.
// The freeze cap is real too (FreezeCapService, EntitlementService, FreezePolicy, streaks);
// only the user's plan is stubbed.
public static class Checks
{
  // Freeze base cap unless a test sets one: high enough that the cap never cuts a balance.
  private const int NoCap = 1000;

  public static CheckService Service(MainDbContext db, params IStakeBinding[] extra)
    => Service(db, new FreezeBase(NoCap), extra);

  public static CheckService Service(MainDbContext db, ISubscriptionService plan, params IStakeBinding[] extra)
  {
    var repo = new CheckRepository(db, NullLogger<CheckRepository>.Instance);
    var vacations = new VacationRepository(db, NullLogger<VacationRepository>.Instance);
    var protection = new ProtectionRepository(db, NullLogger<ProtectionRepository>.Instance);
    IStakeBinding[] bindings =
    [
      new PenaltyStakeBinding(repo, new PenaltyRepository(db, NullLogger<PenaltyRepository>.Instance)),
      .. extra
    ];
    return new CheckService(repo, vacations, protection, FreezeCap(db, plan), bindings,
      NullLogger<CheckService>.Instance);
  }

  public static FreezeCapService FreezeCap(MainDbContext db, ISubscriptionService plan)
  {
    var vacations = new VacationRepository(db, NullLogger<VacationRepository>.Instance);
    var entitlement = new EntitlementService(plan, vacations, null!, new FreezePolicy(), null!);
    return new FreezeCapService(new ProtectionRepository(db, NullLogger<ProtectionRepository>.Instance),
      new StreakRepository(db, NullLogger<StreakRepository>.Instance), entitlement);
  }

  public static Evidence Manual(DateTime occurredAtUtc, string? externalId = null) => new()
  {
    ProofKey = CheckService.ManualProof,
    ExternalId = externalId ?? Guid.NewGuid().ToString(),
    Verdict = EvidenceVerdict.Passed,
    OccurredAtUtc = occurredAtUtc
  };

  // A plan whose only limit is its base freeze cap.
  public sealed class FreezeBase(int baseCap) : ISubscriptionService
  {
    public Task<Result<string>> GetUserTier(string userId) => Task.FromResult<Result<string>>("plus");

    public Task<Result<int>> GetLimitForTier(string tier, string key)
      => Task.FromResult<Result<int>>(key == EntitlementKeys.FreezeBase
        ? baseCap
        : throw new InvalidOperationException($"unexpected limit {key}"));
  }
}
