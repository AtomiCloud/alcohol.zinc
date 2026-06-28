using CSharp_Result;
using NodaMoney;

namespace Domain.Payment;

public interface IPaymentService
{
  // Customer management
  Task<Result<PaymentCustomerPrincipal>> CreateCustomerAsync(string userId);
  Task<Result<ClientSecretResult>> GenerateClientSecretAsync(string userId);
  Task<Result<PaymentCustomerPrincipal?>> UpdatePaymentConsentAsync(
    string airwallexCustomerId,
    string? paymentConsentId,
    PaymentConsentStatus? consentStatus);

  // Payment consent operations
  Task<Result<PaymentConsentStatusResult>> GetPaymentConsentAsync(string userId);
  Task<Result<bool>> HasPaymentConsentAsync(string userId);
  Task<Result<Unit>> DisablePaymentConsentAsync(string userId);

  // Payment intent operations
  Task<Result<PaymentIntentResult>> CreatePaymentIntentAsync(string userId, CreatePaymentIntentRequest request);
  Task<Result<PaymentIntentResult>> ConfirmPaymentIntentAsync(string userId, string intentId, ConfirmPaymentIntentRequest request);

  // Merchant-initiated charge against the user's stored, verified consent.
  // idempotencyKey: stable per-logical-charge key (e.g. penalty Id) so a retried/
  //   concurrent attempt collapses onto the same Airwallex intent instead of a new one.
  // existingIntentId: if a prior attempt already created an intent for this charge,
  //   the gateway intent is retrieved and reconciled (already SUCCEEDED -> returned as-is;
  //   confirmable -> confirmed) BEFORE any new intent is created, so settled money is
  //   never charged twice.
  // onIntentCreated: invoked with the new intent id right after a fresh create succeeds
  //   and BEFORE confirm, so the caller can persist it; this keeps the id durable across
  //   a confirm failure so the retry reconciles the existing intent instead of re-creating.
  Task<Result<PaymentIntentResult>> ChargeStoredConsentAsync(
    string userId, Money amount, string description,
    string? idempotencyKey = null, string? existingIntentId = null,
    Func<string, Task>? onIntentCreated = null);

  // Query operations
  Task<Result<IEnumerable<PaymentCustomerPrincipal>>> SearchCustomers(PaymentCustomerSearch search);
  Task<Result<PaymentCustomer?>> GetCustomerByUserId(string userId);
  Task<Result<PaymentCustomer?>> GetCustomerById(Guid id);

  // Payment completion and status updates
  Task<Result<Unit>> CompletePaymentAsync(Guid requestId, PaymentRecord record);
  Task<Result<Unit>> UpdatePaymentStatusAsync(Guid requestId, PaymentRecord record);
}