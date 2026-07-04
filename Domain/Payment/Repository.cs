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
    PaymentConsentStatus? consentStatus,
    ConsentPurpose purpose);
  // Deletes the purpose's consent row ONLY if it still holds `expectedConsentId`
  // — a consent stored concurrently (e.g. a webhook landing between the gateway
  // revoke and this delete) must survive. Zero rows affected is not an error.
  Task<Result<PaymentCustomerPrincipal?>> DisablePaymentConsentAsync(string userId, ConsentPurpose purpose, string expectedConsentId);
}