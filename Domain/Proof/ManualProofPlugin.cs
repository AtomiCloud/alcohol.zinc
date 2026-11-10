using CSharp_Result;
using Domain.Check;

namespace Domain.Proof;

// The Complete button (app, widget, NFC tap until it gets its own plugin). Always available
// on every habit, even one bound to another proof, and always passes.
public sealed class ManualProofPlugin : ProofPlugin<Empty, Empty>
{
  public const string Key = CheckService.ManualProof;
  public const int Version = 1;

  public override PluginManifest Manifest { get; } = new()
  {
    Key = Key,
    Name = "Check in",
    Version = ManualProofPlugin.Version,
    Platforms = ["ios", "android"],
    SettingsSchema = """{"type":"object","properties":{},"additionalProperties":false}""",
    AlwaysBound = true
  };

  protected override Task<Result<ProofVerdict?>> Evaluate(Empty settings, Empty payload, ProofEvent e,
    ProofWindow window) =>
    Task.FromResult<Result<ProofVerdict?>>(new ProofVerdict(EvidenceVerdict.Passed));
}
