using System.IO.Compression;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Net.Http.Headers;

namespace App.StartUp.Services;

/// <summary>
///   Response compression (Brotli + Gzip) for JSON replies.
///   A phone on a new or idle connection can only receive about 14.5 KB in the first burst, so an
///   uncompressed 18 KB reply costs one extra round trip; gzipped it is about 2.5 KB.
/// </summary>
public static class CompressionService
{
  // Replies that carry a secret. Compression is skipped for them so that no response mixes a
  // secret with anything an attacker could influence (BREACH-style length oracle).
  private static readonly string[] SecretSuffixes =
  [
    "/client-secret",
    "/customers",
    "/web-handoff"
  ];

  private static readonly string[] MimeTypes =
  [
    "application/json",
    "application/problem+json",
    "text/json"
  ];

  public static bool IsCompressible(PathString path)
  {
    var p = path.Value ?? string.Empty;
    var trimmed = p.TrimEnd('/');
    foreach (var s in SecretSuffixes)
      if (trimmed.EndsWith(s, StringComparison.OrdinalIgnoreCase))
        return false;
    return true;
  }

  public static IServiceCollection AddCompressionService(this IServiceCollection services)
  {
    services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
    services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
    services.AddResponseCompression(o =>
    {
      // Safe here: replies never mix a secret with attacker-controlled input (see SecretSuffixes).
      o.EnableForHttps = true;
      o.Providers.Add<BrotliCompressionProvider>();
      o.Providers.Add<GzipCompressionProvider>();
      o.MimeTypes = MimeTypes;
    });
    return services;
  }

  public static IApplicationBuilder UseCompressionService(this IApplicationBuilder app)
  {
    return app.UseWhen(c => IsCompressible(c.Request.Path), a =>
    {
      // The compression middleware only sets Vary when the request asks for compression, so a
      // plain reply (e.g. the cacheable plans list) could be cached and replayed to a gzip client.
      // Always say the reply depends on Accept-Encoding.
      a.Use((c, next) =>
      {
        c.Response.OnStarting(() =>
        {
          if (!c.Response.Headers.Vary.Contains(HeaderNames.AcceptEncoding))
            c.Response.Headers.Append(HeaderNames.Vary, HeaderNames.AcceptEncoding);
          return Task.CompletedTask;
        });
        return next();
      });
      a.UseResponseCompression();
    });
  }
}
