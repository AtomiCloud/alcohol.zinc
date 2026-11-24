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
}
