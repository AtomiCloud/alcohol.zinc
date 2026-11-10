using FluentValidation;

namespace App.Modules.Proof.API.V1;

public class BindProofReqValidator : AbstractValidator<BindProofReq>
{
  public BindProofReqValidator()
  {
    RuleFor(x => x.PluginKey).NotEmpty().MaximumLength(64);
    RuleFor(x => x.Settings).NotEmpty().MaximumLength(4096);
  }
}
