using CSharp_Result;

namespace Domain.Payment;

public interface IPaymentCustomerRepository
{
  Task<Result<PaymentCustomer?>> GetByUserId(string userId);
  Task<Result<PaymentCustomer?>> GetById(Guid id);
  Task<Result<IEnumerable<PaymentCustomerPrincipal>>> Search(PaymentCustomerSearch search);
  Task<Result<PaymentCustomerPrincipal>> Create(string userId, string airwallexCustomerId);
  Task<Result<PaymentCustomerPrincipal?>> UpdatePaymentConsentByAirwallexCustomerId(
    string airwallexCustomerId,
    string? paymentConsentId,
    PaymentConsentStatus? consentStatus);
  Task<Result<PaymentCustomerPrincipal?>> DisablePaymentConsentAsync(string userId);
}

public interface IPaymentIntentRepository
{
  Task<Result<PaymentIntent?>> GetByAirwallexId(string airwallexPaymentIntentId);
  Task<Result<PaymentIntent?>> GetByMerchantOrderId(string merchantOrderId);
  Task<Result<IEnumerable<PaymentIntentPrincipal>>> GetByUserId(string userId);
  Task<Result<PaymentIntentPrincipal>> Create(PaymentIntentRecord record);
  Task<Result<PaymentIntentPrincipal?>> UpdateStatus(
    string airwallexPaymentIntentId,
    PaymentIntentStatus status,
    decimal capturedAmount);
}