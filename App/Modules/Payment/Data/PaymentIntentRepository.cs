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

  public async Task<Result<PaymentIntentPrincipal>> Create(Guid id, PaymentIntentRecord record)
  {
    try
    {
      logger.LogInformation("Creating PaymentIntent with Id: {Id} for UserId: {UserId}", id, record.UserId);

      var now = DateTime.UtcNow;
      var data = new PaymentIntentData
      {
        Id = id,
        UserId = record.UserId,
        AirwallexPaymentIntentId = record.AirwallexPaymentIntentId,
        AirwallexCustomerId = record.AirwallexCustomerId,
        AmountCents = (long)(record.Amount * 100),  // Convert decimal to cents
        Currency = record.Currency,
        CapturedAmountCents = (long)(record.CapturedAmount * 100),  // Convert decimal to cents
        Status = record.Status,
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
      logger.LogError(e, "Failed to create PaymentIntent with Id: {Id} for UserId: {UserId}", id, record.UserId);
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

      data.Status = status;
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

  public async Task<Result<Unit>> LinkExecutions(Guid paymentIntentId, IEnumerable<Guid> habitExecutionIds)
  {
    try
    {
      logger.LogInformation("Linking {Count} habit executions to PaymentIntent: {PaymentIntentId}", habitExecutionIds.Count(), paymentIntentId);

      var now = DateTime.UtcNow;
      var links = habitExecutionIds.Select(executionId => new PaymentIntentExecutionData
      {
        PaymentIntentId = paymentIntentId,
        HabitExecutionId = executionId,
        CreatedAt = now
      });

      db.PaymentIntentExecutions.AddRange(links);
      await db.SaveChangesAsync();

      logger.LogInformation("Successfully linked {Count} habit executions to PaymentIntent: {PaymentIntentId}", habitExecutionIds.Count(), paymentIntentId);

      return new Unit();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Failed to link habit executions to PaymentIntent: {PaymentIntentId}", paymentIntentId);
      return e;
    }
  }

  public async Task<Result<IEnumerable<Guid>>> GetLinkedExecutions(Guid paymentIntentId)
  {
    try
    {
      logger.LogInformation("Retrieving linked habit executions for PaymentIntent: {PaymentIntentId}", paymentIntentId);

      var executionIds = await db
        .PaymentIntentExecutions
        .Where(x => x.PaymentIntentId == paymentIntentId)
        .Select(x => x.HabitExecutionId)
        .ToListAsync();

      logger.LogInformation("Found {Count} linked habit executions for PaymentIntent: {PaymentIntentId}", executionIds.Count, paymentIntentId);

      return executionIds;
    }
    catch (Exception e)
    {
      logger.LogError(e, "Failed to retrieve linked habit executions for PaymentIntent: {PaymentIntentId}", paymentIntentId);
      return e;
    }
  }
}
