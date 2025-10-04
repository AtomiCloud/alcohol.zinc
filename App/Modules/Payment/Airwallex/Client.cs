using System.Net.Http.Headers;
using App.StartUp.Registry;
using App.Utility;
using CSharp_Result;

namespace App.Modules.Payment.Airwallex;

public class AirwallexClient(
  IHttpClientFactory factory,
  IAirwallexAuthenticator authenticator,
  ILogger<AirwallexClient> logger
)
{
  private HttpClient HttpClient => factory.CreateClient(HttpClients.Airwallex);

  public Task<Result<AirwallexCreateCustomerRes>> CreateCustomerAsync(AirwallexCreateCustomerReq req)
  {
    return authenticator
      .GetToken()
      .ThenAwait(async token =>
      {
        var request = new HttpRequestMessage
        {
          Method = HttpMethod.Post,
          RequestUri = new Uri("api/v1/pa/customers/create", UriKind.Relative),
          Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
          Content = JsonContent.Create(req),
        };

        using var response = await HttpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        try
        {
          response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException e)
        {
          logger.LogError(e, "Failed to create customer with Airwallex (HTTP Error), Response: {Body}", body);
          return e;
        }
        catch (Exception e)
        {
          logger.LogError(e, "Failed to create customer with Airwallex");
          throw;
        }

        return body.ToObj<AirwallexCreateCustomerRes>().ToResult();
      });
  }

  public Task<Result<AirwallexClientSecretRes>> GenerateClientSecretAsync(string customerId)
  {
    return authenticator
      .GetToken()
      .ThenAwait(async token =>
      {
        var request = new HttpRequestMessage
        {
          Method = HttpMethod.Get,
          RequestUri = new Uri($"api/v1/pa/customers/{customerId}/generate_client_secret", UriKind.Relative),
          Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
        };

        using var response = await HttpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        try
        {
          response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException e)
        {
          logger.LogError(e, "Failed to generate client secret with Airwallex (HTTP Error), Response: {Body}", body);
          return e;
        }
        catch (Exception e)
        {
          logger.LogError(e, "Failed to generate client secret with Airwallex");
          throw;
        }

        return body.ToObj<AirwallexClientSecretRes>().ToResult();
      });
  }

  public Task<Result<AirwallexPaymentConsentsRes>> GetPaymentConsentsAsync(string customerId, string status)
  {
    return authenticator
      .GetToken()
      .ThenAwait(async token =>
      {
        var request = new HttpRequestMessage
        {
          Method = HttpMethod.Get,
          RequestUri = new Uri($"api/v1/pa/payment_consents?page_num=0&page_size=10&status={status}&customer_id={customerId}", UriKind.Relative),
          Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
        };

        using var response = await HttpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        try
        {
          response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException e)
        {
          logger.LogError(e, "Failed to get payment consents with Airwallex (HTTP Error), Response: {Body}", body);
          return e;
        }
        catch (Exception e)
        {
          logger.LogError(e, "Failed to get payment consents with Airwallex");
          throw;
        }

        return body.ToObj<AirwallexPaymentConsentsRes>().ToResult();
      });
  }

  public Task<Result<AirwallexPaymentIntentRes>> CreatePaymentIntentAsync(AirwallexCreatePaymentIntentReq req)
  {
    return authenticator
      .GetToken()
      .ThenAwait(async token =>
      {
        var request = new HttpRequestMessage
        {
          Method = HttpMethod.Post,
          RequestUri = new Uri("api/v1/pa/payment_intents/create", UriKind.Relative),
          Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
          Content = JsonContent.Create(req),
        };

        using var response = await HttpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        try
        {
          response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException e)
        {
          logger.LogError(e, "Failed to create payment intent with Airwallex (HTTP Error), Response: {Body}", body);
          return e;
        }
        catch (Exception e)
        {
          logger.LogError(e, "Failed to create payment intent with Airwallex");
          throw;
        }

        return body.ToObj<AirwallexPaymentIntentRes>().ToResult();
      });
  }

  public Task<Result<AirwallexPaymentIntentRes>> ConfirmPaymentIntentAsync(string intentId, AirwallexConfirmPaymentIntentReq req)
  {
    return authenticator
      .GetToken()
      .ThenAwait(async token =>
      {
        var request = new HttpRequestMessage
        {
          Method = HttpMethod.Post,
          RequestUri = new Uri($"api/v1/pa/payment_intents/{intentId}/confirm", UriKind.Relative),
          Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
          Content = JsonContent.Create(req),
        };

        using var response = await HttpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        try
        {
          response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException e)
        {
          logger.LogError(e, "Failed to confirm payment intent with Airwallex (HTTP Error), Response: {Body}", body);
          return e;
        }
        catch (Exception e)
        {
          logger.LogError(e, "Failed to confirm payment intent with Airwallex");
          throw;
        }

        return body.ToObj<AirwallexPaymentIntentRes>().ToResult();
      });
  }

  public Task<Result<AirwallexAuthTokenRes>> RefreshAccessTokenAsync()
  {
    return authenticator.RefreshTokenAsync()
      .Then(token => new AirwallexAuthTokenRes
      {
        Token = token,
        ExpiresAt = DateTime.UtcNow.AddHours(1).ToString("O") // Default 1 hour expiry
      }, Errors.MapNone);
  }

  public Task<Result<bool>> VerifyWebhookSignatureAsync(string payload, string timestamp, string signature)
  {
    // Webhook signature verification logic would go here
    // For now, return true as placeholder
    logger.LogDebug(payload, timestamp, signature);
    return Task.FromResult(true.ToResult());
  }
}
