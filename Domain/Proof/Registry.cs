namespace Domain.Proof;

public interface IProofPluginRegistry
{
  IReadOnlyList<PluginManifest> List();
  IProofPlugin? Get(string pluginKey);
}

// Every IProofPlugin registered in DI. Adding a plugin is a subclass plus one registration.
public class ProofPluginRegistry(IEnumerable<IProofPlugin> plugins) : IProofPluginRegistry
{
  // Duplicate keys throw here, at startup, rather than routing to an arbitrary plugin.
  private readonly Dictionary<string, IProofPlugin> _byKey = plugins.ToDictionary(p => p.Manifest.Key);

  public IReadOnlyList<PluginManifest> List() => [.. _byKey.Values.Select(p => p.Manifest).OrderBy(m => m.Key)];

  public IProofPlugin? Get(string pluginKey) => _byKey.GetValueOrDefault(pluginKey);
}
