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

  // Payment intent operations
  Task<Result<PaymentIntentResult>> CreatePaymentIntentAsync(CreatePaymentIntentRequest request);
  Task<Result<PaymentIntentResult>> ConfirmPaymentIntentAsync(string paymentIntentId, string paymentConsentId, string customerId);

  // Webhook verification
  Task<Result<bool>> VerifyWebhookSignatureAsync(string payload, string timestamp, string signature);
}