namespace Domain.Check;

// When a habit's day settles: its local midnight plus a wait that depends on how the habit is
// proven. Once settled the day is locked: results and penalties are final. The App layer
// implements this over its settings.
public interface ICheckSettlePolicy
{
  // Proof plugins whose evidence arrives late, with their wait after local midnight. A Strava
  // activity only reaches us once it is saved, so a long run needs hours, not minutes.
  // Every other habit waits CheckWindow.Grace.
  IReadOnlyDictionary<string, TimeSpan> SlowProofs { get; }
}

public static class CheckSettlePolicyExtensions
{
  // proofKey: the habit's bound proof plugin, null when it has none.
  public static TimeSpan WaitFor(this ICheckSettlePolicy policy, string? proofKey) =>
    proofKey != null && policy.SlowProofs.TryGetValue(proofKey, out var wait) ? wait : CheckWindow.Grace;

  // Whether the check's day has settled at now: its End plus the habit's wait.
  public static bool Settled(this ICheckSettlePolicy policy, CheckRecord check, string? proofKey, DateTime now) =>
    now >= check.DeadlineUtc + policy.WaitFor(proofKey);

  // Whether the user can undo the check now: a Done from a tap in the app (manual proof on a
  // habit without an NFC tag, since a tag tap is recorded the same way) before its day settles.
  public static bool CanUndo(this ICheckSettlePolicy policy, CheckRecord check, string? proofKey, bool tagged,
    DateTime now) =>
    check is { Status: CheckStatus.Done, SettledBy: CheckService.ManualProof } && !tagged
    && !policy.Settled(check, proofKey, now);
}
