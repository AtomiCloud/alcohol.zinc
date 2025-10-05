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
}