using CSharp_Result;

namespace Domain.Payment;

public interface IPaymentService
{
  // Customer management
  Task<Result<PaymentCustomerPrincipal>> CreateCustomerAsync(string userId);
  Task<Result<ClientSecretResult>> GenerateClientSecretAsync(string userId);
  Task<Result<PaymentCustomerPrincipal?>> UpdatePaymentConsentAsync(string userId, bool hasConsent);

  // Payment consent operations
  Task<Result<PaymentConsentInfo>> GetPaymentConsentAsync(string userId);
  Task<Result<bool>> HasPaymentConsentAsync(string userId);

  // Payment intent operations
  Task<Result<PaymentIntentResult>> CreatePaymentIntentAsync(string userId, CreatePaymentIntentRequest request);
  Task<Result<PaymentIntentResult>> ConfirmPaymentIntentAsync(string userId, string intentId, ConfirmPaymentIntentRequest request);

  // Query operations
  Task<Result<IEnumerable<PaymentCustomerPrincipal>>> SearchCustomers(PaymentCustomerSearch search);
  Task<Result<PaymentCustomer?>> GetCustomerByUserId(string userId);
  Task<Result<PaymentCustomer?>> GetCustomerById(Guid id);

  // Webhook processing
  Task<Result<Unit>> ProcessWebhookAsync(string payload, string timestamp, string signature);

  // Payment completion and status updates
  Task<Result<Unit>> CompletePaymentAsync(Guid requestId, PaymentRecord record);
  Task<Result<Unit>> UpdatePaymentStatusAsync(Guid requestId, PaymentRecord record);

  // Token management
  Task<Result<string>> RefreshAccessTokenAsync();
}