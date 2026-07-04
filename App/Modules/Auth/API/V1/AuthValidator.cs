using FluentValidation;

namespace App.Modules.Auth.API.V1;

public class WebHandoffReqValidator : AbstractValidator<WebHandoffReq>
{
  public WebHandoffReqValidator()
  {
    RuleFor(x => x.Platform)
      .NotEmpty()
      .Must(p => p is "ios" or "android")
      .WithMessage("Platform must be one of: ios, android");

    // Storefront is optional (the app may fail to read it); the CTA matrix
    // fails closed to neutral when it is absent.
    // \z (not $) so a trailing newline can't sneak past the exact-length match.
    RuleFor(x => x.Storefront)
      .Matches(@"^[A-Za-z]{2}\z")
      .When(x => !string.IsNullOrEmpty(x.Storefront))
      .WithMessage("Storefront must be an ISO 3166-1 alpha-2 country code");
  }
}
