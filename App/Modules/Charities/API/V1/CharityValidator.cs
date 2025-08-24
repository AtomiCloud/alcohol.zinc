using App.Utility;
using FluentValidation;

namespace App.Modules.Charities.API.V1;

public class CreateCharityReqValidator : AbstractValidator<CreateCharityReq>
{
  public CreateCharityReqValidator()
  {
    this.RuleFor(x => x.Name)
      .NotNull()
      .NameValid();
      
    this.RuleFor(x => x.Email)
      .NotNull()
      .EmailAddress()
      .MaximumLength(256);
      
    this.RuleFor(x => x.Address)
      .MaximumLength(512)
      .When(x => x.Address != null);
  }
}

public class UpdateCharityReqValidator : AbstractValidator<UpdateCharityReq>
{
  public UpdateCharityReqValidator()
  {
    this.RuleFor(x => x.Name)
      .NotNull()
      .NameValid();
      
    this.RuleFor(x => x.Email)
      .NotNull()
      .EmailAddress()
      .MaximumLength(256);
      
    this.RuleFor(x => x.Address)
      .MaximumLength(512)
      .When(x => x.Address != null);
  }
}