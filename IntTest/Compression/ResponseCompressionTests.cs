using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using App.StartUp.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace IntTest.Compression;

// Runs the real compression registration (AddCompressionService / UseCompressionService) on a
// TestServer, over the same pipeline position Server.cs uses (first).
public sealed class ResponseCompressionTests : IAsyncLifetime
{
  private WebApplication _app = null!;
  private HttpClient _client = null!;

  // A reply shaped like the Today overview: 20 habits with nested versions (about 18 KB).
  private static readonly string TodayBody = BuildToday();

  public async Task InitializeAsync()
  {
    var builder = WebApplication.CreateBuilder();
    builder.WebHost.UseTestServer();
    builder.Services.AddCompressionService();
    _app = builder.Build();

    _app.UseCompressionService();
    _app.MapGet("/api/v1/habit/u1/overview", () => Results.Text(TodayBody, "application/json"));
    _app.MapGet("/", () => Results.Json(new { status = "OK" }));
    _app.MapGet("/problem", () => Results.Text(TodayBody, "application/problem+json"));
    _app.MapGet("/image", () => Results.Bytes(Encoding.UTF8.GetBytes(TodayBody), "image/png"));
    _app.MapGet("/api/v1/payment/u1/client-secret", () => Results.Text(TodayBody, "application/json"));
    _app.MapPut("/api/v1/payment/u1/customers", () => Results.Text(TodayBody, "application/json"));
    _app.MapPost("/api/v1/auth/web-handoff", () => Results.Text(TodayBody, "application/json"));
    await _app.StartAsync();

    // No automatic decompression: we want to see the bytes the server sent.
    _client = _app.GetTestClient();
  }

  public async Task DisposeAsync()
  {
    _client.Dispose();
    await _app.DisposeAsync();
  }

  private async Task<(HttpResponseMessage Res, byte[] Body)> Get(string path, string? acceptEncoding,
    HttpMethod? method = null)
  {
    var req = new HttpRequestMessage(method ?? HttpMethod.Get, path);
    if (acceptEncoding != null) req.Headers.TryAddWithoutValidation("Accept-Encoding", acceptEncoding);
    var res = await _client.SendAsync(req);
    return (res, await res.Content.ReadAsByteArrayAsync());
  }

  [Fact]
  public async Task Json_Reply_Is_Gzipped_When_Asked()
  {
    var (res, body) = await Get("/api/v1/habit/u1/overview", "gzip");
    res.Content.Headers.ContentEncoding.Should().ContainSingle().Which.Should().Be("gzip");
    res.Headers.Vary.Should().Contain("Accept-Encoding");
    Gunzip(body).Should().Be(TodayBody);
    body.Length.Should().BeLessThan(TodayBody.Length / 3);
  }

  [Fact]
  public async Task Json_Reply_Is_Brotli_When_Asked()
  {
    var (res, body) = await Get("/api/v1/habit/u1/overview", "br");
    res.Content.Headers.ContentEncoding.Should().ContainSingle().Which.Should().Be("br");
    Unbrotli(body).Should().Be(TodayBody);
  }

  [Fact]
  public async Task Brotli_Wins_When_Both_Asked()
  {
    var (res, _) = await Get("/api/v1/habit/u1/overview", "gzip, br");
    res.Content.Headers.ContentEncoding.Should().ContainSingle().Which.Should().Be("br");
  }

  [Fact]
  public async Task Json_Reply_Is_Plain_When_Not_Asked()
  {
    var (res, body) = await Get("/api/v1/habit/u1/overview", null);
    res.Content.Headers.ContentEncoding.Should().BeEmpty();
    Encoding.UTF8.GetString(body).Should().Be(TodayBody);
  }

  [Theory]
  [InlineData(null)]
  [InlineData("identity")]
  [InlineData("gzip")]
  [InlineData("br")]
  public async Task Vary_Accept_Encoding_Is_Set_Once_On_Every_Variant(string? enc)
  {
    var (res, _) = await Get("/api/v1/habit/u1/overview", enc);
    res.Headers.Vary.Count(v => v.Equals("Accept-Encoding", StringComparison.OrdinalIgnoreCase)).Should().Be(1);
  }

  [Fact]
  public async Task Identity_Only_Is_Plain()
  {
    var (res, _) = await Get("/api/v1/habit/u1/overview", "identity");
    res.Content.Headers.ContentEncoding.Should().BeEmpty();
  }

  [Fact]
  public async Task Problem_Json_Is_Gzipped()
  {
    var (res, _) = await Get("/problem", "gzip");
    res.Content.Headers.ContentEncoding.Should().ContainSingle().Which.Should().Be("gzip");
  }

  [Fact]
  public async Task Non_Json_Is_Not_Compressed()
  {
    var (res, _) = await Get("/image", "gzip, br");
    res.Content.Headers.ContentEncoding.Should().BeEmpty();
  }

  [Theory]
  [InlineData("GET", "/api/v1/payment/u1/client-secret")]
  [InlineData("PUT", "/api/v1/payment/u1/customers")]
  [InlineData("POST", "/api/v1/auth/web-handoff")]
  public async Task Secret_Bearing_Replies_Are_Never_Compressed(string method, string path)
  {
    var (res, body) = await Get(path, "gzip, br", new HttpMethod(method));
    res.Content.Headers.ContentEncoding.Should().BeEmpty();
    Encoding.UTF8.GetString(body).Should().Be(TodayBody);
  }

  [Theory]
  [InlineData("/api/v1/payment/u1/client-secret", false)]
  [InlineData("/api/v1/payment/u1/client-secret/", false)]
  [InlineData("/API/V1/AUTH/WEB-HANDOFF", false)]
  [InlineData("/api/v1/habit/u1/overview", true)]
  [InlineData("/", true)]
  public void IsCompressible_Matches_Secret_Routes(string path, bool expected)
  {
    CompressionService.IsCompressible(new PathString(path)).Should().Be(expected);
  }

  [Fact]
  public async Task Today_Sized_Reply_Raw_Vs_Compressed_And_Cost()
  {
    const int n = 200;
    var raw = Encoding.UTF8.GetByteCount(TodayBody);
    var (_, gz) = await Get("/api/v1/habit/u1/overview", "gzip");
    var (_, br) = await Get("/api/v1/habit/u1/overview", "br");

    var plainMs = await Time("/api/v1/habit/u1/overview", null, n);
    var gzMs = await Time("/api/v1/habit/u1/overview", "gzip", n);
    var brMs = await Time("/api/v1/habit/u1/overview", "br", n);

    Console.WriteLine(
      $"COMPRESSION-MEASURE raw={raw}B gzip={gz.Length}B br={br.Length}B " +
      $"per-request plain={plainMs:F3}ms gzip={gzMs:F3}ms br={brMs:F3}ms");

    gz.Length.Should().BeLessThan(raw / 3);
    br.Length.Should().BeLessThan(raw / 3);
    // The first burst a server may send on a fresh connection is about 14.5 KB.
    raw.Should().BeGreaterThan(14_500);
    gz.Length.Should().BeLessThan(14_500);
  }

  private async Task<double> Time(string path, string? enc, int n)
  {
    for (var i = 0; i < 20; i++) await Get(path, enc); // warm up
    var sw = Stopwatch.StartNew();
    for (var i = 0; i < n; i++) await Get(path, enc);
    sw.Stop();
    return sw.Elapsed.TotalMilliseconds / n;
  }

  private static string Gunzip(byte[] b)
  {
    using var s = new GZipStream(new MemoryStream(b), CompressionMode.Decompress);
    using var r = new StreamReader(s, Encoding.UTF8);
    return r.ReadToEnd();
  }

  private static string Unbrotli(byte[] b)
  {
    using var s = new BrotliStream(new MemoryStream(b), CompressionMode.Decompress);
    using var r = new StreamReader(s, Encoding.UTF8);
    return r.ReadToEnd();
  }

  private static string BuildToday()
  {
    var rnd = new Random(7);
    var habits = Enumerable.Range(0, 20).Select(i => new
    {
      id = Guid.NewGuid().ToString(),
      name = $"Habit number {i}",
      habitVersionId = Guid.NewGuid().ToString(),
      userId = "user_2abcdefghijklmnop",
      cron = "0 9 * * *",
      timezone = "America/Los_Angeles",
      stake = new { type = "charity", amount = rnd.Next(1, 20), currency = "USD", charityId = Guid.NewGuid().ToString() },
      streak = new { current = rnd.Next(0, 90), longest = rnd.Next(0, 200), frozen = false },
      executions = Enumerable.Range(0, 3).Select(j => new
      {
        id = Guid.NewGuid().ToString(),
        date = $"2026-11-{10 + j:00}",
        status = "completed",
        createdAt = DateTime.UtcNow.AddDays(-j).ToString("O")
      }).ToArray(),
      createdAt = DateTime.UtcNow.AddDays(-i).ToString("O"),
      updatedAt = DateTime.UtcNow.ToString("O")
    });
    return JsonSerializer.Serialize(new { habits });
  }
}
