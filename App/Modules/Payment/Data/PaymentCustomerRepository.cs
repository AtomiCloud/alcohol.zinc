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
        .Include(x => x.Consents)
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
        .Include(x => x.Consents)
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

      var query = db.PaymentCustomers.Include(x => x.Consents).AsQueryable();

      if (!string.IsNullOrEmpty(search.UserId))
        query = query.Where(x => x.UserId == search.UserId);

      if (!string.IsNullOrEmpty(search.AirwallexCustomerId))
        query = query.Where(x => x.AirwallexCustomerId == search.AirwallexCustomerId);

      if (search.HasPaymentConsent.HasValue)
      {
        // Historic semantics: filters on the penalty consent's existence.
        if (search.HasPaymentConsent.Value)
          query = query.Where(x => x.Consents.Any(c => c.Purpose == (int)ConsentPurpose.Penalty));
        else
          query = query.Where(x => !x.Consents.Any(c => c.Purpose == (int)ConsentPurpose.Penalty));
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
    PaymentConsentStatus? consentStatus,
    ConsentPurpose purpose)
  {
    try
    {
      logger.LogInformation("Updating PaymentConsent for AirwallexCustomerId: {AirwallexCustomerId}", airwallexCustomerId);

      var data = await db
        .PaymentCustomers
        .Include(x => x.Consents)
        .Where(x => x.AirwallexCustomerId == airwallexCustomerId)
        .FirstOrDefaultAsync();

      if (data == null)
      {
        logger.LogWarning("PaymentCustomer not found for AirwallexCustomerId: {AirwallexCustomerId}", airwallexCustomerId);
        return (PaymentCustomerPrincipal?)null;
      }

      // Convert enum to string for database storage
      var statusString = PaymentCustomerMapper.ConsentStatusToString(consentStatus);
      var now = DateTime.UtcNow;

      var consent = data.Consents.FirstOrDefault(c => c.Purpose == (int)purpose);
      if (paymentConsentId == null)
      {
        if (consent != null) db.PaymentConsents.Remove(consent);
      }
      else if (consent == null)
      {
        db.PaymentConsents.Add(new PaymentConsentData
        {
          Id = Guid.NewGuid(),
          PaymentCustomerId = data.Id,
          Purpose = (int)purpose,
          ConsentId = paymentConsentId,
          Status = statusString,
          CreatedAt = now,
          UpdatedAt = now
        });
      }
      else
      {
        consent.ConsentId = paymentConsentId;
        consent.Status = statusString;
        consent.UpdatedAt = now;
      }
      data.UpdatedAt = now;

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

  public async Task<Result<PaymentCustomerPrincipal?>> DisablePaymentConsentAsync(string userId, ConsentPurpose purpose)
  {
    try
    {
      logger.LogInformation("Disabling {Purpose} PaymentConsent for UserId: {UserId}", purpose, userId);

      var now = DateTime.UtcNow;
      // Deleting the row (not nulling) keeps the invariant: a consent row always
      // carries a consent id. The customer's UpdatedAt still marks the change.
      await db.PaymentConsents
        .Where(c => c.Purpose == (int)purpose && c.PaymentCustomer!.UserId == userId)
        .ExecuteDeleteAsync();
      var rowsAffected = await db.PaymentCustomers
        .Where(x => x.UserId == userId)
        .ExecuteUpdateAsync(setter => setter.SetProperty(p => p.UpdatedAt, now));

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
