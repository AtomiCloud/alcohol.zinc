using Domain.Proof;

namespace App.Modules.Proof.API.V1;

public static class ProofMapper
{
  public static ProofBindingRes ToRes(this ProofBindingPrincipal p) =>
    new(p.HabitId, p.Record.PluginKey, p.Record.PluginVersion, p.Record.Settings);

  public static PluginManifestRes ToRes(this PluginManifest m) =>
    new(m.Key, m.Name, m.Version, m.Platforms, m.SettingsSchema, m.MinTier, m.AlwaysBound);
}
