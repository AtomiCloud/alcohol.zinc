using App.StartUp.Database;
using CSharp_Result;
using Domain.Payment;
using Microsoft.EntityFrameworkCore;

namespace App.Modules.Payment.Data;

public class PaymentCustomerRepository(MainDbContext db, ILogger<PaymentCustomerRepository> logger) : IPaymentCustomerRepository
{
  public async Task<Result<PaymentCustomer?>> GetByUserId(string userId)
  {
    try
    {
      logger.LogInformation("Retrieving PaymentCustomer by UserId: {UserId}", userId);

      var data = await db
        .PaymentCustomers
        .Where(x => x.UserId == userId)
        .FirstOrDefaultAsync();

      if (data == null)
      {
        logger.LogWarning("PaymentCustomer not found for UserId: {UserId}", userId);
        return (PaymentCustomer?)null;
      }

      return data.ToDomain();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Failed retrieving PaymentCustomer by UserId: {UserId}", userId);
      return e;
    }
  }

  public async Task<Result<PaymentCustomer?>> GetById(Guid id)
  {
    try
    {
      logger.LogInformation("Retrieving PaymentCustomer by Id: {Id}", id);

      var data = await db
        .PaymentCustomers
        .Where(x => x.Id == id)
        .FirstOrDefaultAsync();

      if (data == null)
      {
        logger.LogWarning("PaymentCustomer not found for Id: {Id}", id);
        return (PaymentCustomer?)null;
      }

      return data.ToDomain();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Failed retrieving PaymentCustomer by Id: {Id}", id);
      return e;
    }
  }

  public async Task<Result<IEnumerable<PaymentCustomerPrincipal>>> Search(PaymentCustomerSearch search)
  {
    try
    {
      logger.LogInformation("Searching PaymentCustomers");

      var query = db.PaymentCustomers.AsQueryable();

      if (!string.IsNullOrEmpty(search.UserId))
        query = query.Where(x => x.UserId == search.UserId);

      if (!string.IsNullOrEmpty(search.AirwallexCustomerId))
        query = query.Where(x => x.AirwallexCustomerId == search.AirwallexCustomerId);

      if (search.HasPaymentConsent.HasValue)
      {
        if (search.HasPaymentConsent.Value)
          query = query.Where(x => x.PaymentConsentId != null);
        else
          query = query.Where(x => x.PaymentConsentId == null);
      }

      if (search.CreatedBefore.HasValue)
        query = query.Where(x => x.CreatedAt < search.CreatedBefore.Value);

      if (search.CreatedAfter.HasValue)
        query = query.Where(x => x.CreatedAt > search.CreatedAfter.Value);

      var data = await query
        .Skip(search.Skip)
        .Take(search.Limit)
        .ToListAsync();

      return data.Select(x => x.ToPrincipal()).ToList();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Failed searching PaymentCustomers");
      return e;
    }
  }

  public async Task<Result<PaymentCustomerPrincipal>> Create(string userId, string airwallexCustomerId)
  {
    try
    {
      logger.LogInformation("Creating PaymentCustomer for UserId: {UserId}", userId);

      var now = DateTime.UtcNow;
      var data = new PaymentCustomerData
      {
        Id = Guid.NewGuid(),
        UserId = userId,
        AirwallexCustomerId = airwallexCustomerId,
        PaymentConsentId = null,
        PaymentConsentStatus = null,
        CreatedAt = now,
        UpdatedAt = now
      };

      var r = db.PaymentCustomers.Add(data);
      await db.SaveChangesAsync();

      logger.LogInformation("PaymentCustomer created with Id: {Id}", data.Id);

      return r.Entity.ToPrincipal();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Failed to create PaymentCustomer for UserId: {UserId}", userId);
      return e;
    }
  }

  public async Task<Result<PaymentCustomerPrincipal?>> UpdatePaymentConsentByAirwallexCustomerId(
    string airwallexCustomerId,
    string? paymentConsentId,
    PaymentConsentStatus? consentStatus)
  {
    try
    {
      logger.LogInformation("Updating PaymentConsent for AirwallexCustomerId: {AirwallexCustomerId}", airwallexCustomerId);

      var data = await db
        .PaymentCustomers
        .Where(x => x.AirwallexCustomerId == airwallexCustomerId)
        .FirstOrDefaultAsync();

      if (data == null)
      {
        logger.LogWarning("PaymentCustomer not found for AirwallexCustomerId: {AirwallexCustomerId}", airwallexCustomerId);
        return (PaymentCustomerPrincipal?)null;
      }

      // Convert enum to string for database storage
      var statusString = PaymentCustomerMapper.ConsentStatusToString(consentStatus);

      data.PaymentConsentId = paymentConsentId;
      data.PaymentConsentStatus = statusString;
      data.UpdatedAt = DateTime.UtcNow;

      var updated = db.PaymentCustomers.Update(data);
      await db.SaveChangesAsync();

      logger.LogInformation("PaymentConsent updated for AirwallexCustomerId: {AirwallexCustomerId}", airwallexCustomerId);

      return updated.Entity.ToPrincipal();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Failed to update PaymentConsent for AirwallexCustomerId: {AirwallexCustomerId}", airwallexCustomerId);
      return e;
    }
  }

  public async Task<Result<PaymentCustomerPrincipal?>> DisablePaymentConsentAsync(string userId)
  {
    try
    {
      logger.LogInformation("Disabling PaymentConsent for UserId: {UserId}", userId);

      var now = DateTime.UtcNow;
      var rowsAffected = await db.PaymentCustomers
        .Where(x => x.UserId == userId)
        .ExecuteUpdateAsync(setter => setter
          .SetProperty(p => p.PaymentConsentId, (string?)null)
          .SetProperty(p => p.PaymentConsentStatus, (string?)null)
          .SetProperty(p => p.UpdatedAt, now)
        );

      if (rowsAffected == 0)
      {
        logger.LogWarning("PaymentCustomer not found for UserId: {UserId}", userId);
        return (PaymentCustomerPrincipal?)null;
      }

      logger.LogInformation("PaymentConsent disabled for UserId: {UserId}", userId);

      // Fetch the updated record
      var data = await db.PaymentCustomers
        .Where(x => x.UserId == userId)
        .FirstOrDefaultAsync();

      return data?.ToPrincipal();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Failed to disable PaymentConsent for UserId: {UserId}", userId);
      return e;
    }
  }
}
