using App.Modules.Check.Data;
using App.Modules.Penalty.Data;
using App.Modules.Protection.Data;
using App.Modules.Vacation.Data;
using App.StartUp.Database;
using Domain.Check;
using Microsoft.Extensions.Logging.Abstractions;

namespace IntTest.Check;

// Builds the real CheckService over a real MainDbContext, with the charity penalty as its stake.
public static class Checks
{
  public static CheckService Service(MainDbContext db, params IStakeBinding[] extra)
  {
    var repo = new CheckRepository(db, NullLogger<CheckRepository>.Instance);
    IStakeBinding[] bindings =
    [
      new PenaltyStakeBinding(repo, new PenaltyRepository(db, NullLogger<PenaltyRepository>.Instance)),
      .. extra
    ];
    return new CheckService(repo,
      new VacationRepository(db, NullLogger<VacationRepository>.Instance),
      new ProtectionRepository(db, NullLogger<ProtectionRepository>.Instance),
      bindings,
      NullLogger<CheckService>.Instance);
  }

  public static Evidence Manual(DateTime occurredAtUtc, string? externalId = null) => new()
  {
    ProofKey = CheckService.ManualProof,
    ExternalId = externalId ?? Guid.NewGuid().ToString(),
    Verdict = EvidenceVerdict.Passed,
    OccurredAtUtc = occurredAtUtc
  };
}
