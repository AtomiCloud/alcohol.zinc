using System.Text.Json;
using System.Text.Json.Serialization;
using CSharp_Result;

namespace Domain.Proof;

// What the core, the registry and the router see. Settings and payloads stay JSON here.
public interface IProofPlugin
{
  PluginManifest Manifest { get; }

  // Parses and checks a binding's settings; returns them normalized for storage, or an
  // InvalidProofBindingException.
  Result<string> ValidateSettings(string settings);

  Task<Result<Unit>> OnBind(ProofBinding binding);
  Task<Result<Unit>> OnUnbind(ProofBinding binding);

  // binding is null when an AlwaysBound plugin proves a habit it isn't bound to.
  // Null verdict: the event says nothing about this check (not relevant, or not enough yet).
  Task<Result<ProofVerdict?>> Evaluate(ProofBinding? binding, ProofEvent e, ProofWindow window);
}

// What a plugin author extends. Stored settings and the event payload arrive as the plugin's
// own types, so a plugin writes Evaluate(TSettings, TEvent, ...) and never touches JSON.
public abstract class ProofPlugin<TSettings, TEvent> : IProofPlugin
  where TSettings : class, new()
  where TEvent : class, new()
{
  // Settings are user input: unknown fields are rejected so a typo doesn't silently do nothing.
  private static readonly JsonSerializerOptions SettingsJson = new(JsonSerializerDefaults.Web)
  {
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
  };

  // Payloads come from other systems (Strava, Apple): ignore fields the plugin doesn't model.
  private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);

  public abstract PluginManifest Manifest { get; }

  // Rules beyond the settings' shape, as field -> messages. Empty when valid.
  protected virtual Dictionary<string, string[]> Validate(TSettings settings) => [];

  protected virtual Task<Result<Unit>> OnBind(ProofBinding binding, TSettings settings) =>
    Task.FromResult<Result<Unit>>(new Unit());

  protected virtual Task<Result<Unit>> OnUnbind(ProofBinding binding, TSettings settings) =>
    Task.FromResult<Result<Unit>>(new Unit());

  protected abstract Task<Result<ProofVerdict?>> Evaluate(TSettings settings, TEvent payload, ProofEvent e,
    ProofWindow window);

  public Result<string> ValidateSettings(string settings)
  {
    TSettings? parsed;
    try
    {
      parsed = JsonSerializer.Deserialize<TSettings>(settings, SettingsJson);
    }
    catch (JsonException e)
    {
      return new InvalidProofBindingException("settings", e.Message);
    }

    if (parsed == null) return new InvalidProofBindingException("settings", "Settings must be a JSON object");
    var errors = Validate(parsed);
    if (errors.Count > 0) return new InvalidProofBindingException(errors);
    return JsonSerializer.Serialize(parsed, SettingsJson);
  }

  public Task<Result<Unit>> OnBind(ProofBinding binding) => OnBind(binding, SettingsOf(binding));

  public Task<Result<Unit>> OnUnbind(ProofBinding binding) => OnUnbind(binding, SettingsOf(binding));

  public Task<Result<ProofVerdict?>> Evaluate(ProofBinding? binding, ProofEvent e, ProofWindow window)
  {
    var payload = e.Payload == null ? new TEvent() : JsonSerializer.Deserialize<TEvent>(e.Payload, PayloadJson);
    return Evaluate(binding == null ? new TSettings() : SettingsOf(binding), payload ?? new TEvent(), e, window);
  }

  // Stored settings were normalized by ValidateSettings when bound.
  private static TSettings SettingsOf(ProofBinding binding) =>
    JsonSerializer.Deserialize<TSettings>(binding.Principal.Record.Settings, PayloadJson) ?? new TSettings();
}

// Settings or payload of a plugin that has none.
public sealed class Empty;

// A bind request the plugin or the core refused: field -> messages (400).
public class InvalidProofBindingException(Dictionary<string, string[]> errors)
  : Exception("Invalid proof binding")
{
  public InvalidProofBindingException(string field, string message) : this(new() { [field] = [message] })
  {
  }

  public Dictionary<string, string[]> Errors { get; } = errors;
}
