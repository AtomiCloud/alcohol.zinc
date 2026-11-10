using CSharp_Result;
using Domain.Entitlement;
using Domain.Exceptions;
using Domain.Habit;

namespace Domain.Proof;

public interface IProofService
{
  IReadOnlyList<PluginManifest> ListPlugins();

  // The habit's binding; null when it only has the always-bound proofs (manual).
  Task<Result<ProofBindingPrincipal?>> GetBinding(string userId, Guid habitId);

  // Validates the settings, checks the user's tier, then binds, replacing the habit's binding.
  Task<Result<ProofBindingPrincipal>> Bind(string userId, Guid habitId, string pluginKey, string settings);

  // Null when the habit has no binding.
  Task<Result<Unit?>> Unbind(string userId, Guid habitId);
}

public class ProofService(
  IProofPluginRegistry registry,
  IProofBindingRepository repo,
  IHabitRepository habitRepo,
  IEntitlementService entitlements
) : IProofService
{
  public IReadOnlyList<PluginManifest> ListPlugins() => registry.List();

  public async Task<Result<ProofBindingPrincipal?>> GetBinding(string userId, Guid habitId)
  {
    var owned = await EnsureOwned(userId, habitId);
    if (owned.IsFailure()) return owned.FailureOrDefault();
    return ((ProofBinding?)await repo.GetForHabit(habitId))?.Principal;
  }

  public async Task<Result<ProofBindingPrincipal>> Bind(string userId, Guid habitId, string pluginKey,
    string settings)
  {
    var owned = await EnsureOwned(userId, habitId);
    if (owned.IsFailure()) return owned.FailureOrDefault();

    var plugin = registry.Get(pluginKey);
    if (plugin == null) return new NotFoundException("Proof plugin not found", typeof(PluginManifest), pluginKey);
    if (plugin.Manifest.AlwaysBound)
      return new InvalidProofBindingException("pluginKey", $"'{pluginKey}' is always available and can't be bound");

    var normalized = plugin.ValidateSettings(settings);
    if (normalized.IsFailure()) return normalized.FailureOrDefault();

    var allowed = await entitlements.EnsureProofPluginAllowed(userId, plugin.Manifest);
    if (allowed.IsFailure()) return allowed.FailureOrDefault();

    // Rebinding (another plugin or new settings) unbinds the old one first.
    var existing = (ProofBinding?)await repo.GetForHabit(habitId);
    if (existing != null)
    {
      var unbound = await UnbindPlugin(existing);
      if (unbound.IsFailure()) return unbound.FailureOrDefault();
    }

    var record = new ProofBindingRecord
    {
      PluginKey = pluginKey,
      PluginVersion = plugin.Manifest.Version,
      Settings = normalized.Get()
    };
    var bound = await plugin.OnBind(new ProofBinding
    {
      Principal = new ProofBindingPrincipal { Id = existing?.Principal.Id ?? Guid.Empty, HabitId = habitId, Record = record },
      UserId = userId
    });
    if (bound.IsFailure()) return bound.FailureOrDefault();

    return await repo.Upsert(habitId, record);
  }

  public async Task<Result<Unit?>> Unbind(string userId, Guid habitId)
  {
    var owned = await EnsureOwned(userId, habitId);
    if (owned.IsFailure()) return owned.FailureOrDefault();

    var existing = (ProofBinding?)await repo.GetForHabit(habitId);
    if (existing == null) return (Unit?)null;

    var unbound = await UnbindPlugin(existing);
    if (unbound.IsFailure()) return unbound.FailureOrDefault();
    return await repo.Delete(habitId);
  }

  // A binding whose plugin was removed from the app has nothing to clean up.
  private Task<Result<Unit>> UnbindPlugin(ProofBinding binding) =>
    registry.Get(binding.Principal.Record.PluginKey)?.OnUnbind(binding)
    ?? Task.FromResult<Result<Unit>>(new Unit());

  private async Task<Result<Unit>> EnsureOwned(string userId, Guid habitId)
  {
    var habit = (HabitVersionPrincipal?)await habitRepo.GetCurrentVersion(userId, habitId);
    if (habit == null) return new NotFoundException("Habit Not Found", typeof(HabitPrincipal), habitId.ToString());
    return new Unit();
  }
}
