namespace Domain.Exceptions;

/// <summary>
/// Thrown when a self-service account deletion is rejected because the user has an
/// outstanding debt and the <c>BlockAccountDeletionOnDebt</c> feature flag is enabled.
/// The App layer maps this to a 409 (see <c>AtomiControllerBase.MapException</c>).
/// </summary>
public class AccountDeletionBlockedException : Exception
{
  public AccountDeletionBlockedException(decimal totalDebt, string currency)
    : base($"Account deletion blocked: outstanding debt of {totalDebt} {currency}")
  {
    this.TotalDebt = totalDebt;
    this.Currency = currency;
  }

  public decimal TotalDebt { get; init; }
  public string Currency { get; init; }
}
