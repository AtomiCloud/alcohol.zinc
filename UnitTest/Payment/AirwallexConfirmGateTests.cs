using App.Modules.Payment.Airwallex;
using CSharp_Result;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace UnitTest.Payment;

// The confirm gate (beforeSend) must run AFTER authentication, which can wait on a token refresh, and
// right before the confirm request leaves. A gate that throws must stop the request from being sent.
public class AirwallexConfirmGateTests
{
  private readonly List<string> _events = [];

  private sealed class RecordingAuthenticator(List<string> events) : IAirwallexAuthenticator
  {
    public Task<Result<string>> GetToken()
    {
      events.Add("token");
      return Task.FromResult<Result<string>>("tok");
    }
  }

  private sealed class RecordingHandler(List<string> events) : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
      events.Add("send");
      return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
      {
        Content = new StringContent(
          """{"id":"int_1","status":"SUCCEEDED","amount":1,"currency":"USD","customer_id":"c","merchant_order_id":"m"}"""),
      });
    }
  }

  private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
  {
    public HttpClient CreateClient(string name) => new(handler) { BaseAddress = new Uri("https://example.invalid/") };
  }

  private AirwallexClient Client() => new(
    new Factory(new RecordingHandler(_events)), new RecordingAuthenticator(_events), NullLogger<AirwallexClient>.Instance);

  private static AirwallexConfirmPaymentIntentReq Req() => new()
  {
    RequestId = "confirm-int_1", PaymentConsentId = "cst", CustomerId = "c", ReturnUrl = "https://example.invalid/r",
  };

  [Fact]
  public async Task Gate_RunsAfterAuthentication_RightBeforeTheRequest()
  {
    await Client().ConfirmPaymentIntentAsync("int_1", Req(), () =>
    {
      _events.Add("gate");
      return Task.CompletedTask;
    });

    _events.Should().Equal("token", "gate", "send");
  }

  [Fact]
  public async Task Gate_Throwing_SendsNothing()
  {
    static Task Stop() => throw new InvalidOperationException("on hold");

    try
    {
      var res = await Client().ConfirmPaymentIntentAsync("int_1", Req(), Stop);
      res.IsSuccess().Should().BeFalse();
    }
    catch (InvalidOperationException)
    {
      // Propagating the gate's exception is also a stop.
    }

    _events.Should().Equal("token");
  }
}
