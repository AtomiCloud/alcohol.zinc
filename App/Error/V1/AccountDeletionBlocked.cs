using System.ComponentModel;
using System.Text.Json.Serialization;
using NJsonSchema.Annotations;

namespace App.Error.V1;

[Description("This error means account deletion was blocked because of an outstanding debt.")]
public class AccountDeletionBlocked : IDomainProblem
{
  public AccountDeletionBlocked() { }

  public AccountDeletionBlocked(decimal totalDebt, string currency)
  {
    this.TotalDebt = totalDebt;
    this.Currency = currency;
    this.Detail =
      $"Account deletion is blocked because you have an outstanding debt of {totalDebt} {currency}. " +
      "Please settle your debt before deleting your account.";
  }

  [JsonIgnore, JsonSchemaIgnore]
  public string Id { get; } = "account_deletion_blocked";

  [JsonIgnore, JsonSchemaIgnore]
  public string Title { get; } = "Account Deletion Blocked";

  [JsonIgnore, JsonSchemaIgnore]
  public string Version { get; } = "v1";

  [JsonIgnore, JsonSchemaIgnore]
  public string Detail { get; } = string.Empty;

  [Description("The total outstanding debt that is blocking the account deletion")]
  public decimal TotalDebt { get; } = 0;

  [Description("The ISO currency code of the outstanding debt")]
  public string Currency { get; } = string.Empty;
}
