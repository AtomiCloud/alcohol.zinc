using System.Text.Json;
using CSharp_Result;
using Domain.Check;
using Domain.Exceptions;

namespace Domain.Proof;

public interface IProofRouter
{
  // The one way in for every proof source (app call, webhook, poll): evaluates the event for
  // each habit it can prove and reports each verdict to ICheckService.Report as evidence.
  Task<Result<List<ProofOutcome>>> Route(ProofEvent e, DateTime? nowUtc = null);
}

public class ProofRouter(IProofPluginRegistry registry, IProofBindingRepository bindings, ICheckService checks)
  : IProofRouter
{
  private static readonly JsonSerializerOptions DataJson = new(JsonSerializerDefaults.Web);

  public async Task<Result<List<ProofOutcome>>> Route(ProofEvent e, DateTime? nowUtc = null)
  {
    var now = nowUtc ?? DateTime.UtcNow;
    var plugin = registry.Get(e.PluginKey);
    if (plugin == null) return new NotFoundException("Proof plugin not found", typeof(PluginManifest), e.PluginKey);

    List<(Guid HabitId, ProofBinding? Binding)> targets;
    if (e.HabitId is { } target)
    {
      // An always-bound plugin (manual) proves any habit of the user; Report checks ownership.
      // Any other plugin only proves the habit if it is bound to it.
      if (plugin.Manifest.AlwaysBound) targets = [(target, null)];
      else
      {
        var binding = (ProofBinding?)await bindings.GetForHabit(target);
        if (binding == null || binding.UserId != e.UserId || binding.Principal.Record.PluginKey != e.PluginKey)
          return new List<ProofOutcome>();
        targets = [(target, binding)];
      }
    }
    else
    {
      var bound = (List<ProofBinding>)await bindings.ListForUser(e.UserId, e.PluginKey);
      targets = [.. bound.Select(b => (b.Principal.HabitId, (ProofBinding?)b))];
    }

    var outcomes = new List<ProofOutcome>();
    foreach (var (habitId, binding) in targets)
    {
      var check = await checks.PendingCheckAt(e.UserId, habitId, e.OccurredAtUtc, now);
      if (check.IsFailure()) return check.FailureOrDefault();
      // That moment's day is settled, unscheduled or over: a replay or a late event.
      if (check.Get() is not { } pending)
      {
        outcomes.Add(new ProofOutcome(habitId, null));
        continue;
      }

      var verdict = await plugin.Evaluate(binding, e, ProofWindow.Of(pending));
      if (verdict.IsFailure()) return verdict.FailureOrDefault();
      if (verdict.Get() is not { } v)
      {
        outcomes.Add(new ProofOutcome(habitId, null));
        continue;
      }

      var reported = await checks.Report(e.UserId, habitId, new Evidence
      {
        ProofKey = e.PluginKey,
        ExternalId = e.ExternalId,
        Verdict = v.Verdict,
        OccurredAtUtc = e.OccurredAtUtc,
        Data = v.Data == null ? null : JsonSerializer.Serialize(v.Data, DataJson),
        Notes = e.Notes
      }, now);
      if (reported.IsFailure()) return reported.FailureOrDefault();
      outcomes.Add(new ProofOutcome(habitId, reported.Get()));
    }

    return outcomes;
  }
}
