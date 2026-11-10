using Domain.Check;

namespace Domain.Proof;

// What a proof plugin tells the registry, the bind API and the app about itself.
// AlwaysBound plugins (manual) are available on every habit without a binding.
// MinTier null means every tier may bind it.
public record PluginManifest
{
  public required string Key { get; init; }
  public required string Name { get; init; }
  public required int Version { get; init; }
  public required string[] Platforms { get; init; }
  public required string SettingsSchema { get; init; } // JSON Schema for the binding's settings
  public string? MinTier { get; init; }
  public bool AlwaysBound { get; init; }
}

// One proof per habit (v1). Targets such as "5 km" live in Settings, never in the habit.
public record ProofBindingRecord
{
  public required string PluginKey { get; init; }
  public required int PluginVersion { get; init; }
  public required string Settings { get; init; } // JSON
}

public record ProofBindingPrincipal
{
  public required Guid Id { get; init; }
  public required Guid HabitId { get; init; }
  public required ProofBindingRecord Record { get; init; }
}

public record ProofBinding
{
  public required ProofBindingPrincipal Principal { get; init; }
  public required string UserId { get; init; }
}

// Every source (app call, webhook, poll) builds one. HabitId targets one habit (the
// Complete button, an NFC tap); without it the router evaluates every habit of the user
// bound to the plugin (a Strava activity). (habit, plugin, ExternalId) is idempotent.
public record ProofEvent
{
  public required string PluginKey { get; init; }
  public required string UserId { get; init; }
  public required string ExternalId { get; init; }
  public required DateTime OccurredAtUtc { get; init; }
  public Guid? HabitId { get; init; }
  public string? Payload { get; init; } // JSON, the plugin's own event type
  public string? Notes { get; init; }
}

// The Pending check the event falls in, so a plugin can re-read the whole day from
// its source instead of the core storing partial progress.
public record ProofWindow(DateOnly LocalDate, string Timezone, DateTime OpensAtUtc, DateTime DeadlineUtc)
{
  public static ProofWindow Of(CheckPrincipal check) =>
    new(check.Record.LocalDate, check.Record.Timezone, check.Record.OpensAtUtc, check.Record.DeadlineUtc);
}

// A plugin's decision for one check. Data is a summary stake plugins can show
// ("Jimmy ran today"), serialized into Evidence.Data.
public record ProofVerdict(EvidenceVerdict Verdict, object? Data = null);

// What routing one event did to one habit: the execution it recorded, or null when
// the plugin had no verdict or nothing changed (replay, already settled, too late).
public record ProofOutcome(Guid HabitId, Domain.Habit.HabitExecutionPrincipal? Execution);
