using App.Utility;
using FluentValidation;

namespace App.Modules.Causes.API.V1;

public class CreateCauseReqValidator : AbstractValidator<CreateCauseReq>
{
  public CreateCauseReqValidator()
  {
    RuleFor(x => x.Key).NotEmpty().MaximumLength(128);
    RuleFor(x => x.Name).NotEmpty().NameValid();
  }
}

public class UpdateCauseReqValidator : AbstractValidator<UpdateCauseReq>
{
  public UpdateCauseReqValidator()
  {
    RuleFor(x => x.Name).NotEmpty().NameValid();
  }
}

public class CauseSearchReqValidator : AbstractValidator<CauseSearchReq>
{
  public CauseSearchReqValidator()
  {
    RuleFor(x => x.Key).MaximumLength(128).When(x => x.Key != null);
    RuleFor(x => x.Name).MaximumLength(256).When(x => x.Name != null);
    RuleFor(x => x.Limit).Limit();
    RuleFor(x => x.Skip).Skip();
  }
}
