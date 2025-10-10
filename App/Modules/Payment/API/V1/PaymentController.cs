using System.Net.Mime;
using System.Text.Encodings.Web;
using System.Text.Json;
using App.Modules.Common;
using App.Modules.Payment.Airwallex;
using App.StartUp.Registry;
using App.StartUp.Services.Auth;
using App.Utility;
using Asp.Versioning;
using CSharp_Result;
using Domain.Payment;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace App.Modules.Payment.API.V1;

[ApiVersion(1.0)]
[ApiController]
[Consumes(MediaTypeNames.Application.Json)]
[Route("api/v{version:apiVersion}/[controller]")]
public class PaymentController(
  IPaymentService service,
  CreatePaymentIntentReqValidator createPaymentIntentReqValidator,
  ConfirmPaymentIntentReqValidator confirmPaymentIntentReqValidator,
  AirwallexWebhookService webhookService,
  IAuthManagement authManagement,
  IAuthHelper authHelper
) : AtomiControllerBase(authHelper)
{
  // 1. PUT /api/v1/payment/{userId}/customers
  [Authorize, HttpPut("{userId}/customers")]
  public async Task<ActionResult<CreateCustomerRes>> CreateCustomer(string userId)
  {
    var result = await this.GuardAsync(userId)
      .ThenAwait(_ => service.CreateCustomerAsync(userId))
      .ThenAwait(customer => service.GenerateClientSecretAsync(userId)
        .Then(secret => customer.ToRes(secret.ClientSecret), Errors.MapNone));

    return this.ReturnResult(result);
  }

  // 2. GET /api/v1/payment/{userId}/client-secret
  [Authorize, HttpGet("{userId}/client-secret")]
  public async Task<ActionResult<ClientSecretRes>> GetClientSecret(string userId)
  {
    var result = await this.GuardAsync(userId)
      .ThenAwait(_ => service.GenerateClientSecretAsync(userId))
      .Then(x => x.ToRes(), Errors.MapNone);

    return this.ReturnResult(result);
  }

  // 3. GET /api/v1/payment/{userId}/consent
  [Authorize, HttpGet("{userId}/consent")]
  public async Task<ActionResult<PaymentConsentRes>> GetPaymentConsent(string userId)
  {
    var result = await this.GuardAsync(userId)
      .ThenAwait(_ => service.GetPaymentConsentAsync(userId))
      .Then(x => x.ToRes(), Errors.MapNone);

    return this.ReturnResult(result);
  }

  // 3b. DELETE /api/v1/payment/{userId}/consent
  [Authorize, HttpDelete("{userId}/consent")]
  public async Task<ActionResult> DisablePaymentConsent(string userId)
  {
    var result = await this.GuardAsync(userId)
      .ThenAwait(_ => service.DisablePaymentConsentAsync(userId));

    return this.ReturnUnitResult(result);
  }

  // 4. POST /api/v1/payment/{userId}/intent
  [Authorize, HttpPost("{userId}/intent")]
  public async Task<ActionResult<CreatePaymentIntentRes>> CreatePaymentIntent(
    string userId,
    [FromBody] CreatePaymentIntentReq req
  )
  {
    var result = await this.GuardAsync(userId)
      .ThenAwait(_ => createPaymentIntentReqValidator.ValidateAsyncResult(req, "Invalid CreatePaymentIntentReq"))
      .ThenAwait(r => service.CreatePaymentIntentAsync(userId, r.ToDomain(userId)))
      .Then(x => x.ToRes(), Errors.MapNone);

    return this.ReturnResult(result);
  }

  // 5. POST /api/v1/payment/{userId}/intent/{intentId}
  [Authorize, HttpPost("{userId}/intent/{intentId}")]
  public async Task<ActionResult<ConfirmPaymentIntentRes>> ConfirmPaymentIntent(
    string userId,
    string intentId,
    [FromBody] ConfirmPaymentIntentReq req
  )
  {
    var result = await this.GuardAsync(userId)
      .ThenAwait(_ => confirmPaymentIntentReqValidator.ValidateAsyncResult(req, "Invalid ConfirmPaymentIntentReq"))
      .ThenAwait(r => service.ConfirmPaymentIntentAsync(userId, intentId, r.ToDomain(userId, intentId)))
      .Then(x => x.ToConfirmRes(), Errors.MapNone);

    return this.ReturnResult(result);
  }

  // 6. POST webhook
  [HttpPost("webhook")]
  public async Task<ActionResult> Webhook([FromBody] AirwallexEvent evt)
  {
    this.Request.Headers.TryGetValue("x-timestamp", out var timestamp);
    this.Request.Headers.TryGetValue("x-signature", out var signature);

    this.Request.Body.Seek(0, SeekOrigin.Begin);
    using var stream = new StreamReader(this.HttpContext.Request.Body);
    var body = await stream.ReadToEndAsync();
    var o = body.ToObj<object>();
    var payload = JsonSerializer.Serialize(
      o,
      new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }
    );

    var result = await webhookService.ProcessEvent(
      evt,
      timestamp.ToString(),
      payload,
      signature.ToString()
    );

    return this.ReturnUnitResult(result);
  }
}
