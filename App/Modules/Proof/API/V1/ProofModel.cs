namespace App.Modules.Proof.API.V1;

// Settings is a JSON object as text; its shape is the plugin's SettingsSchema.
public record BindProofReq(
  string PluginKey,
  string Settings
);

// A habit with no binding reports the manual proof.
public record ProofBindingRes(
  Guid HabitId,
  string PluginKey,
  int PluginVersion,
  string Settings
);

public record PluginManifestRes(
  string Key,
  string Name,
  int Version,
  string[] Platforms,
  string SettingsSchema,
  string? MinTier,
  bool AlwaysBound
);
