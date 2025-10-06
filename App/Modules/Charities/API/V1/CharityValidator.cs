using App.Utility;
using FluentValidation;

namespace App.Modules.Charities.API.V1;

public class CreateCharityReqValidator : AbstractValidator<CreateCharityReq>
{
  public CreateCharityReqValidator()
  {
    RuleFor(x => x.Name).NotNull().NameValid();

    RuleFor(x => x.Slug).MaximumLength(128).When(x => x.Slug != null);

    RuleFor(x => x.Mission).MaximumLength(512).When(x => x.Mission != null);

    RuleFor(x => x.Description).MaximumLength(2048).When(x => x.Description != null);

    RuleFor(x => x.Countries)
      .Must(c => c == null || c.All(cc => !string.IsNullOrWhiteSpace(cc) && cc.Length == 2))
      .WithMessage("Each country must be an ISO alpha-2 code");

    RuleFor(x => x.PrimaryRegistrationNumber).MaximumLength(128).When(x => x.PrimaryRegistrationNumber != null);

    RuleFor(x => x.PrimaryRegistrationCountry)
      .Length(2)
      .When(x => x.PrimaryRegistrationCountry != null);

    RuleFor(x => x.WebsiteUrl!)
      .UrlValid()
      .When(x => x.WebsiteUrl != null);
    RuleFor(x => x.LogoUrl!)
      .UrlValid()
      .When(x => x.LogoUrl != null);

    RuleFor(x => x.VerificationSource).MaximumLength(128).When(x => x.VerificationSource != null);
  }
}

public class UpdateCharityReqValidator : AbstractValidator<UpdateCharityReq>
{
  public UpdateCharityReqValidator()
  {
    RuleFor(x => x.Name).NotNull().NameValid();

    RuleFor(x => x.Slug).MaximumLength(128).When(x => x.Slug != null);

    RuleFor(x => x.Mission).MaximumLength(512).When(x => x.Mission != null);

    RuleFor(x => x.Description).MaximumLength(2048).When(x => x.Description != null);

    RuleFor(x => x.Countries)
      .Must(c => c == null || c.All(cc => !string.IsNullOrWhiteSpace(cc) && cc.Length == 2))
      .WithMessage("Each country must be an ISO alpha-2 code");

    RuleFor(x => x.PrimaryRegistrationNumber).MaximumLength(128).When(x => x.PrimaryRegistrationNumber != null);

    RuleFor(x => x.PrimaryRegistrationCountry)
      .Length(2)
      .When(x => x.PrimaryRegistrationCountry != null);

    RuleFor(x => x.WebsiteUrl!).UrlValid().When(x => x.WebsiteUrl != null);
    RuleFor(x => x.LogoUrl!).UrlValid().When(x => x.LogoUrl != null);

    RuleFor(x => x.VerificationSource).MaximumLength(128).When(x => x.VerificationSource != null);
  }
}

public class CharitySearchReqValidator : AbstractValidator<CharitySearchReq>
{
  public CharitySearchReqValidator()
  {
    this.RuleFor(x => x.Country)
      .Length(2)
      .When(x => x.Country != null);

    this.RuleFor(x => x.PrimaryRegistrationCountry)
      .Length(2)
      .When(x => x.PrimaryRegistrationCountry != null);

    this.RuleFor(x => x.Limit).Limit();
    this.RuleFor(x => x.Skip).Skip();
  }
}

public class SetCharityCausesReqValidator : AbstractValidator<SetCharityCausesReq>
{
  public SetCharityCausesReqValidator()
  {
    RuleFor(x => x.Keys)
      .NotNull();

    RuleForEach(x => x.Keys)
      .NotEmpty()
      .MaximumLength(128);
  }
}
