using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using App.Modules.Habit.Data;
using App.Modules.Payment.Airwallex;
using App.Modules.Payment.Data;
using App.Modules.System;
using App.Modules.Users.Data;
using App.StartUp.Database;
using App.StartUp.Options;
using CSharp_Result;
using Domain;
using Domain.Payment;
using Domain.User;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IntTest.User;

/// <summary>
/// In-app end-to-end test: drives the REAL deletion code path
/// (UserService.DeleteAccount -> onBeforePurge -> PaymentService -> AirwallexGateway -> AirwallexClient)
/// against a real Postgres AND the real Airwallex SANDBOX. It seeds a user whose PaymentCustomer
/// points at a real VERIFIED sandbox consent, runs the delete exactly as the controller wires it,
/// then asserts the consent flipped to DISABLED in the sandbox and the DB rows are gone.
///
/// ⚠️ Disabling a consent is PERMANENT — supply a THROWAWAY VERIFIED sandbox consent you own.
///
/// Gated by env vars (skipped unless ALL are set):
///   ZINC_DELETE_TEST_DB   (Postgres host, e.g. localhost)
///   AWX_CLIENT_ID, AWX_API_KEY        (Airwallex sandbox creds — lapras Infisical)
///   AWX_CONSENT_ID, AWX_CUSTOMER_ID   (a throwaway VERIFIED sandbox consent + its customer)
/// </summary>
public class DeleteAccountAirwallexE2ETests : IAsyncLifetime
{
  private const string AwxBase = "https://api-demo.airwallex.com";
  private MainDbContext _db = null!;
  private bool _enabled;
  private string _awxId = "", _awxKey = "", _consentId = "", _customerId = "";

  public async Task InitializeAsync()
  {
    var host = Environment.GetEnvironmentVariable("ZINC_DELETE_TEST_DB");
    _awxId = Environment.GetEnvironmentVariable("AWX_CLIENT_ID") ?? "";
    _awxKey = Environment.GetEnvironmentVariable("AWX_API_KEY") ?? "";
    _consentId = Environment.GetEnvironmentVariable("AWX_CONSENT_ID") ?? "";
    _customerId = Environment.GetEnvironmentVariable("AWX_CUSTOMER_ID") ?? "";
    _enabled = !string.IsNullOrWhiteSpace(host) && _awxId != "" && _awxKey != "" && _consentId != "" && _customerId != "";
    if (!_enabled) return;

    var opt = new DatabaseOption
    {
      Host = host!,
      Port = int.Parse(Environment.GetEnvironmentVariable("ZINC_DELETE_TEST_PORT") ?? "55432"),
      User = Environment.GetEnvironmentVariable("ZINC_DELETE_TEST_USER") ?? "test",
      Password = Environment.GetEnvironmentVariable("ZINC_DELETE_TEST_PASS") ?? "test",
      Database = Environment.GetEnvironmentVariable("ZINC_DELETE_TEST_DBNAME") ?? "zinctest",
      AutoMigrate = false,
      Timeout = 30,
    };
    var monitor = new StaticOptionsMonitor<Dictionary<string, DatabaseOption>>(
      new Dictionary<string, DatabaseOption> { [MainDbContext.Key] = opt });
    _db = new MainDbContext(monitor, NullLoggerFactory.Instance);
    await _db.Database.EnsureDeletedAsync();
    await _db.Database.EnsureCreatedAsync();
  }

  public async Task DisposeAsync()
  {
    if (!_enabled) return;
    await _db.Database.EnsureDeletedAsync();
    await _db.DisposeAsync();
  }

  [Fact]
  public async Task DeleteAccount_RevokesAirwallexConsentInSandbox_AndPurgesData()
  {
    if (!_enabled) return; // skipped unless DB + Airwallex sandbox + a real consent are provided

    // Seed the user + a PaymentCustomer pointing at the real sandbox consent.
    _db.Users.Add(new UserData { Id = "user-A", Username = "awx_e2e", Email = "awx_e2e@test.local", Active = true });
    _db.PaymentCustomers.Add(new PaymentCustomerData
    {
      Id = Guid.NewGuid(),
      UserId = "user-A",
      AirwallexCustomerId = _customerId,
      PaymentConsentId = _consentId,
      PaymentConsentStatus = "VERIFIED",
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow,
    });
    await _db.SaveChangesAsync();

    // Build the REAL payment stack (only the Redis-backed token authenticator is swapped for a
    // direct sandbox auth; everything else — Client, Gateway, Service — is the production code).
    var httpFactory = new SandboxHttpClientFactory(AwxBase);
    var authenticator = new SandboxAirwallexAuthenticator(new HttpClient { BaseAddress = new Uri(AwxBase) }, _awxId, _awxKey);
    var awxClient = new AirwallexClient(httpFactory, authenticator, NullLogger<AirwallexClient>.Instance);
    var gateway = new AirwallexGateway(awxClient);
    var paymentRepo = new PaymentCustomerRepository(_db, NullLogger<PaymentCustomerRepository>.Instance);
    var paymentService = new PaymentService(paymentRepo, gateway);

    var userRepo = new UserRepository(_db, NullLogger<UserRepository>.Instance);
    var userService = new UserService(
      userRepo,
      new StreakRepository(_db, NullLogger<StreakRepository>.Instance),
      // The REAL TransactionManager (TransactionScope) — exercises the production transaction path,
      // so a regression that put external work back inside the scope would surface here.
      new TransactionManager(NullLogger<TransactionManager>.Instance));

    (await ConsentStatus(authenticator)).Should().Be("VERIFIED", "precondition: supply a VERIFIED throwaway consent");

    // ACT — exactly how UserController.DeleteMe wires it.
    var result = await userService.DeleteAccount("user-A", blockOnDebt: false, onBeforePurge: async () =>
    {
      await paymentService.DisablePaymentConsentAsync("user-A");
      return new Unit();
    });

    result.IsSuccess().Should().BeTrue();

    // The consent is now revoked at Airwallex (sandbox)…
    (await ConsentStatus(authenticator)).Should().Be("DISABLED");
    // …and the user's data is purged.
    (await _db.Users.CountAsync(x => x.Id == "user-A")).Should().Be(0);
    (await _db.PaymentCustomers.CountAsync(x => x.UserId == "user-A")).Should().Be(0);
  }

  private async Task<string> ConsentStatus(SandboxAirwallexAuthenticator auth)
  {
    var token = (await auth.GetToken()).Get();
    using var http = new HttpClient { BaseAddress = new Uri(AwxBase) };
    var req = new HttpRequestMessage(HttpMethod.Get, $"api/v1/pa/payment_consents/{_consentId}");
    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    using var res = await http.SendAsync(req);
    var json = await res.Content.ReadFromJsonAsync<JsonElement>();
    return json.GetProperty("status").GetString()!;
  }
}

/// <summary>Hands out an HttpClient pointed at the Airwallex sandbox (name is ignored).</summary>
internal sealed class SandboxHttpClientFactory(string baseUrl) : IHttpClientFactory
{
  public HttpClient CreateClient(string name) => new() { BaseAddress = new Uri(baseUrl) };
}

/// <summary>
/// Real Airwallex sandbox token, fetched directly (no Redis/MemoryCache) — a thin stand-in for
/// the production AirwallexAuthenticator so the rest of the payment stack runs unchanged.
/// </summary>
internal sealed class SandboxAirwallexAuthenticator(HttpClient http, string clientId, string apiKey) : IAirwallexAuthenticator
{
  public async Task<Result<string>> GetToken()
  {
    var req = new HttpRequestMessage(HttpMethod.Post, "api/v1/authentication/login");
    req.Headers.Add("x-client-id", clientId);
    req.Headers.Add("x-api-key", apiKey);
    using var res = await http.SendAsync(req);
    res.EnsureSuccessStatusCode();
    var json = await res.Content.ReadFromJsonAsync<JsonElement>();
    return json.GetProperty("token").GetString()!;
  }
}
