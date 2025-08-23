using App.Utility;
using FluentValidation;

namespace App.Modules.Configurations.API.V1;

public class CreateConfigurationReqValidator : AbstractValidator<CreateConfigurationReq>
{
  public CreateConfigurationReqValidator()
  {
    this.RuleFor(x => x.Timezone)
      .NotNull()
      .MaximumLength(64)
      .WithMessage("Timezone must be between 1 and 64 characters");
      
    this.RuleFor(x => x.EndOfDay)
      .NotNull()
      .TimeValid();
      
    this.RuleFor(x => x.DefaultCharityId)
      .NotNull()
      .Must(x => Guid.TryParse(x, out _))
      .WithMessage("DefaultCharityId must be a valid GUID");
  }
}

public class UpdateConfigurationReqValidator : AbstractValidator<UpdateConfigurationReq>
{
  public UpdateConfigurationReqValidator()
  {
    this.RuleFor(x => x.Timezone)
      .NotNull()
      .MaximumLength(64)
      .WithMessage("Timezone must be between 1 and 64 characters");
      
    this.RuleFor(x => x.EndOfDay)
      .NotNull()
      .TimeValid();
      
    this.RuleFor(x => x.DefaultCharityId)
      .NotNull()
      .Must(x => Guid.TryParse(x, out _))
      .WithMessage("DefaultCharityId must be a valid GUID");
  }
}