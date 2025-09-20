using App.Modules.Common;
using App.StartUp.Options;
using App.StartUp.Services.Auth;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace App.Modules.System;

public record Info(
  string Landscape,
  string Platform,
  string Service,
  string Module,
  string Version,
  string Status,
  DateTime TimeStamp
);

[ApiVersionNeutral]
[ApiController]
[Route("/")]
public class SystemController(IOptionsSnapshot<AppOption> app, IAuthHelper h)
  : AtomiControllerBase(h)
{
  [HttpGet]
  public ActionResult<Info> SystemInfo()
  {
    var v = app.Value;
    var info = new
      Info(
        v.Landscape,
        v.Platform,
        v.Service,
        v.Module,
        v.Version,
        "OK",
        DateTime.UtcNow);
    return this.Ok(info);
  }
}
