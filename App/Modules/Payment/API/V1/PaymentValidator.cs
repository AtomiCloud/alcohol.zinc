using FluentValidation;

namespace App.Modules.Payment.API.V1;

public class CreatePaymentIntentReqValidator : AbstractValidator<CreatePaymentIntentReq>
{
  public CreatePaymentIntentReqValidator()
  {
    RuleFor(x => x.Amount)
      .GreaterThan(0)
      .WithMessage("Amount must be greater than 0")
      .LessThanOrEqualTo(10000)
      .WithMessage("Amount must not exceed 10,000");

    RuleFor(x => x.Currency)
      .NotEmpty()
      .WithMessage("Currency is required")
      .Length(3)
      .WithMessage("Currency must be a 3-letter ISO code")
      .Must(BeValidCurrency)
      .WithMessage("Currency must be a valid ISO currency code (e.g., USD, AUD, EUR)");

    RuleFor(x => x.Description)
      .NotEmpty()
      .WithMessage("Description is required")
      .MaximumLength(500)
      .WithMessage("Description must not exceed 500 characters");
  }

  private static bool BeValidCurrency(string currency)
  {
    if (string.IsNullOrWhiteSpace(currency)) return false;

    var validCurrencies = new[]
    {
      "USD", "AUD", "EUR", "GBP", "JPY", "CAD", "CHF", "CNY", "SEK", "NZD",
      "MXN", "SGD", "HKD", "NOK", "ZAR", "TRY", "BRL", "INR", "KRW", "PLN"
    };

    return validCurrencies.Contains(currency.ToUpperInvariant());
  }
}

public class ConfirmPaymentIntentReqValidator : AbstractValidator<ConfirmPaymentIntentReq>
{
  public ConfirmPaymentIntentReqValidator()
  {
    RuleFor(x => x.PaymentConsentId)
      .NotEmpty()
      .WithMessage("PaymentConsentId is required")
      .MaximumLength(100)
      .WithMessage("PaymentConsentId must not exceed 100 characters");
  }
}