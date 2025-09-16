using System.Security.Claims;
using App.StartUp.Options.Auth;
using App.StartUp.Services.Auth;
using App.StartUp.Services.Auth.Logto;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace App.StartUp.Services;

public static class AuthService
{
  public static WebApplication UseAuthService(this WebApplication app)
  {
    app.UseAuthentication();
    app.UseAuthorization();
    return app;
  }

  public static JwtBearerOptions ConfigureJwtBearerOptions(this JwtBearerOptions options, AuthOption o)
  {
    var s = o.Settings!;
    // Use the Issuer as the Authority for OIDC discovery
    options.Authority = s.Issuer;
    options.Audience = s.Audience;

    // Let the JWT Bearer authentication automatically discover and retrieve signing keys
    // from the OIDC discovery endpoint at {Authority}/.well-known/openid-configuration
    options.RequireHttpsMetadata = s.Issuer.StartsWith("https://");

    if (s.TokenValidation is { } to)
    {
      options.TokenValidationParameters = new TokenValidationParameters
      {
        ValidAudience = s.Audience,
        ValidIssuer = s.Issuer,
        NameClaimType = ClaimTypes.NameIdentifier,
        ValidateIssuer = to.ValidateIssuer,
        ValidateAudience = to.ValidateAudience,
        ClockSkew = TimeSpan.FromSeconds(to.ClockSkew),
        ValidateIssuerSigningKey = to.ValidateIssuerSigningKey,
        ValidateLifetime = to.ValidateLifetime,
        // IssuerSigningKeys will be automatically populated from the OIDC discovery endpoint
      };
    }
    else
    {
      options.TokenValidationParameters =
        new TokenValidationParameters
        {
          NameClaimType = ClaimTypes.NameIdentifier,
          ValidateIssuerSigningKey = true,
          // IssuerSigningKeys will be automatically populated from the OIDC discovery endpoint
        };
    }

    return options;
  }

  public static IServiceCollection AddAuthService(this IServiceCollection services, AuthOption o)
  {
    if (o.Settings is null) throw new ApplicationException("Auth is enabled but Domain or Audience is null");

    services.AddSingleton<IAuthorizationHandler, HasAnyHandler>()
      .AutoTrace<IAuthorizationHandler>();

    services.AddSingleton<IAuthorizationHandler, HasAllHandler>()
      .AutoTrace<IAuthorizationHandler>();

    services.AddSingleton<IAuthHelper, AuthHelper>()
      .AutoTrace<IAuthHelper>();

    services.AddScoped<IAuthManagement, LogtoAuthManagement>()
      .AutoTrace<IAuthManagement>();

    services.AddScoped<ILogtoAuthenticator, LogtoAuthenticator>()
      .AutoTrace<ILogtoAuthenticator>();

    var s = o.Settings!;
    services
      .AddAuthentication(options =>
      {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
      })
      .AddJwtBearer(options => options.ConfigureJwtBearerOptions(o));

    var p = s.Policies ?? [];

    services.AddAuthorization(opt =>
      {
        foreach (var (k, v) in p)
        {
          opt.AddPolicy(k, pb =>
          {
            switch (v)
            {
              case { Type: "Any" }:
                pb.Requirements.AddAnyScope(s.Issuer, v.Field, v.Target);
                break;
              case { Type: "All" }:
                pb.Requirements.AddAllScope(s.Issuer, v.Field, v.Target);
                break;
              default:
                throw new ApplicationException($"Auth Policy Type is not supported: {v.Type}");
            }
          });
        }
      })
      .AddSingleton<IAuthorizationMiddlewareResultHandler, AuthorizationResultTransformer>();

    return services;
  }
}
