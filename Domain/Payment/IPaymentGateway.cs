using CSharp_Result;

namespace Domain.Payment;

public interface IPaymentGateway
{
  // Customer management
  Task<Result<string?>> GetCustomerIdByMerchantIdAsync(string merchantCustomerId);
  Task<Result<string>> CreateCustomerAsync(string merchantCustomerId);
  Task<Result<string>> GenerateClientSecretAsync(string customerId);

  // Payment consent operations
  Task<Result<PaymentConsentInfo[]>> GetVerifiedPaymentConsentsAsync(string customerId);
  Task<Result<Unit>> DisablePaymentConsentAsync(string paymentConsentId);

  // Payment intent operations
  Task<Result<PaymentIntentResult>> CreatePaymentIntentAsync(CreatePaymentIntentRequest request);
  Task<Result<PaymentIntentResult>> RetrievePaymentIntentAsync(string paymentIntentId);
  // beforeSend: invoked after authentication, immediately before the confirm request is sent. It may
  // throw to stop the confirm (nothing is sent, no money moves). afterAttemptId: the intent's latest
  // failed attempt when this confirm retries it (null for its first confirm).
  Task<Result<PaymentIntentResult>> ConfirmPaymentIntentAsync(string paymentIntentId, string paymentConsentId, string customerId,
    Func<Task>? beforeSend = null, string? afterAttemptId = null);
}