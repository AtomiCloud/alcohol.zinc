using App.StartUp.Database;
using CSharp_Result;
using Domain.Payment;
using Microsoft.EntityFrameworkCore;

namespace App.Modules.Payment.Data;

public class PaymentIntentRepository(MainDbContext db, ILogger<PaymentIntentRepository> logger) : IPaymentIntentRepository
{
  public async Task<Result<PaymentIntent?>> GetByAirwallexId(string airwallexPaymentIntentId)
  {
    try
    {
      logger.LogInformation("Retrieving PaymentIntent by AirwallexPaymentIntentId: {AirwallexPaymentIntentId}", airwallexPaymentIntentId);

      var data = await db
        .PaymentIntents
        .Where(x => x.AirwallexPaymentIntentId == airwallexPaymentIntentId)
        .FirstOrDefaultAsync();

      if (data == null)
      {
        logger.LogWarning("PaymentIntent not found for AirwallexPaymentIntentId: {AirwallexPaymentIntentId}", airwallexPaymentIntentId);
        return (PaymentIntent?)null;
      }

      return data.ToDomain();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Failed retrieving PaymentIntent by AirwallexPaymentIntentId: {AirwallexPaymentIntentId}", airwallexPaymentIntentId);
      return e;
    }
  }

  public async Task<Result<PaymentIntent?>> GetByMerchantOrderId(string merchantOrderId)
  {
    try
    {
      logger.LogInformation("Retrieving PaymentIntent by MerchantOrderId: {MerchantOrderId}", merchantOrderId);

      var data = await db
        .PaymentIntents
        .Where(x => x.MerchantOrderId == merchantOrderId)
        .FirstOrDefaultAsync();

      if (data == null)
      {
        logger.LogWarning("PaymentIntent not found for MerchantOrderId: {MerchantOrderId}", merchantOrderId);
        return (PaymentIntent?)null;
      }

      return data.ToDomain();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Failed retrieving PaymentIntent by MerchantOrderId: {MerchantOrderId}", merchantOrderId);
      return e;
    }
  }

  public async Task<Result<IEnumerable<PaymentIntentPrincipal>>> GetByUserId(string userId)
  {
    try
    {
      logger.LogInformation("Retrieving PaymentIntents by UserId: {UserId}", userId);

      var data = await db
        .PaymentIntents
        .Where(x => x.UserId == userId)
        .OrderByDescending(x => x.CreatedAt)
        .ToListAsync();

      return data.Select(x => x.ToPrincipal()).ToList();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Failed retrieving PaymentIntents by UserId: {UserId}", userId);
      return e;
    }
  }

  public async Task<Result<PaymentIntentPrincipal>> Create(PaymentIntentRecord record)
  {
    try
    {
      logger.LogInformation("Creating PaymentIntent for UserId: {UserId}", record.UserId);

      var now = DateTime.UtcNow;
      var data = new PaymentIntentData
      {
        Id = Guid.NewGuid(),
        UserId = record.UserId,
        AirwallexPaymentIntentId = record.AirwallexPaymentIntentId,
        AirwallexCustomerId = record.AirwallexCustomerId,
        AmountCents = (long)(record.Amount * 100),  // Convert decimal to cents
        Currency = record.Currency,
        CapturedAmountCents = (long)(record.CapturedAmount * 100),  // Convert decimal to cents
        Status = PaymentIntentMapper.IntentStatusToString(record.Status),
        MerchantOrderId = record.MerchantOrderId,
        CreatedAt = now,
        UpdatedAt = now
      };

      var r = db.PaymentIntents.Add(data);
      await db.SaveChangesAsync();

      logger.LogInformation("PaymentIntent created with Id: {Id}", data.Id);

      return r.Entity.ToPrincipal();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Failed to create PaymentIntent for UserId: {UserId}", record.UserId);
      return e;
    }
  }

  public async Task<Result<PaymentIntentPrincipal?>> UpdateStatus(
    string airwallexPaymentIntentId,
    PaymentIntentStatus status,
    decimal capturedAmount)
  {
    try
    {
      logger.LogInformation("Updating PaymentIntent status for AirwallexPaymentIntentId: {AirwallexPaymentIntentId}", airwallexPaymentIntentId);

      var data = await db
        .PaymentIntents
        .Where(x => x.AirwallexPaymentIntentId == airwallexPaymentIntentId)
        .FirstOrDefaultAsync();

      if (data == null)
      {
        logger.LogWarning("PaymentIntent not found for AirwallexPaymentIntentId: {AirwallexPaymentIntentId}", airwallexPaymentIntentId);
        return (PaymentIntentPrincipal?)null;
      }

      data.Status = PaymentIntentMapper.IntentStatusToString(status);
      data.CapturedAmountCents = (long)(capturedAmount * 100);  // Convert decimal to cents
      data.UpdatedAt = DateTime.UtcNow;

      var updated = db.PaymentIntents.Update(data);
      await db.SaveChangesAsync();

      logger.LogInformation("PaymentIntent status updated for AirwallexPaymentIntentId: {AirwallexPaymentIntentId}", airwallexPaymentIntentId);

      return updated.Entity.ToPrincipal();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Failed to update PaymentIntent status for AirwallexPaymentIntentId: {AirwallexPaymentIntentId}", airwallexPaymentIntentId);
      return e;
    }
  }
}