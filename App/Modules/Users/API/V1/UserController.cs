using System.Net.Mime;
using App.Error;
using App.Error.V1;
using App.Modules.Common;
using App.StartUp.Options;
using App.StartUp.Registry;
using App.StartUp.Services.Auth;
using App.Utility;
using Asp.Versioning;
using CSharp_Result;
using Domain.Payment;
using Domain.User;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace App.Modules.Users.API.V1;

[ApiVersion(1.0)]
[ApiController]
[Consumes(MediaTypeNames.Application.Json)]
[Route("api/v{version:apiVersion}/[controller]")]
public class UserController(
  IUserService service,
  IAuthManagement authManagement,
  CreateUserReqValidator createUserReqValidator,
  UpdateUserReqValidator updateUserReqValidator,
  UserSearchQueryValidator userSearchQueryValidator,
  ITokenDataExtractor tokenDataExtractor,
  IOptions<AppOption> appOption,
  IPaymentService paymentService,
  IAuthHelper h
) : AtomiControllerBase(h)
{
  [Authorize(Policy = AuthPolicies.OnlyAdmin), HttpGet]
  public async Task<ActionResult<IEnumerable<UserPrincipalRes>>> Search([FromQuery] SearchUserQuery query)
  {
    var x = await userSearchQueryValidator
      .ValidateAsyncResult(query, "Invalid SearchUserQuery")
      .ThenAwait(q => service.Search(q.ToDomain()))
      .Then(x => x.Select(u => u.ToRes()).ToResult());
    return this.ReturnResult(x);
  }

  [Authorize, HttpGet("Me")]
  [Produces("text/plain")]
  public string Me()
  {
    return this.Sub() ?? "none";
  }

  [Authorize, HttpGet("Me/All")]
  public async Task<ActionResult<UserRes>> MeAll()
  {
    var sub = this.Sub();

    Result<UserRes?> nr = (UserRes?)null;

    if (sub == null) this.ReturnNullableResult(nr, new EntityNotFound("User Not Found", typeof(User), sub ?? "none"));

    var user = await this.GuardAsync(sub)
      .ThenAwait(_ => service.GetById(sub!))
      .Then(x => x?.ToRes(), Errors.MapAll);

    return this.ReturnNullableResult(user, new EntityNotFound(
      "User Not Found", typeof(User), sub ?? "none"));
  }

  [Authorize, HttpGet("{id}")]
  public async Task<ActionResult<UserRes>> GetById(string id)
  {
    var user = await this.GuardOrAnyAsync(id, AuthRoles.Field, AuthRoles.Admin)
      .ThenAwait(_ => service.GetById(id))
      .Then(x => x?.ToRes(), Errors.MapAll);

    return this.ReturnNullableResult(user, new EntityNotFound(
      "User Not Found", typeof(User), id));
  }

  [Authorize, HttpGet("username/{username}")]
  public async Task<ActionResult<UserRes>> GetByUsername(string username)
  {
    var user = await service.GetByUsername(username)
      .Then(x => x?.ToRes(), Errors.MapAll)
      .Then(x => this.GuardOrAll(x?.Principal?.Id ?? "", AuthRoles.Field, AuthRoles.Admin)
        .Then(_ => x, Errors.MapAll)
      );
    return this.ReturnNullableResult(user, new EntityNotFound(
      "User Not Found", typeof(User), username));
  }

  [Authorize, HttpPost]
  public async Task<ActionResult<UserPrincipalRes>> Create([FromBody] CreateUserReq req)
  {
    var id = this.Sub();
    if (id == null)
    {
      Result<UserPrincipalRes> x = new Unauthenticated(
        "You are not authenticated"
      ).ToException();
      return this.ReturnResult(x);
    }

    var user = await createUserReqValidator
      .ValidateAsyncResult(req, "Invalid CreateUserReq")
      .ThenAwait(x => tokenDataExtractor.ExtractFromToken(x.IdToken, x.AccessToken))
      .Then<UserToken, UserToken>(x => x.Sub == id
        ? x
        : new DomainProblemException(new InvalidUserToken("Sub of tokens do not match auth token", "ID/Access", []))
      )
      .ThenAwait(x =>
        service.Create(id, x.ToRecord(), () => authManagement.SetClaim(id, LogtoClaims.ZincUpdated, "true")))
      .Then(x => x.ToRes(), Errors.MapAll);
    return this.ReturnResult(user);
  }

  [Authorize, HttpPut("{id}")]
  public async Task<ActionResult<UserPrincipalRes>> Update(string id, [FromBody] UpdateUserReq req)
  {
    var user = await this.GuardAsync(id)
      .ThenAwait(_ => updateUserReqValidator.ValidateAsyncResult(req, "Invalid UpdateUserReq"))
      .ThenAwait(x => tokenDataExtractor.ExtractFromToken(x.IdToken, x.AccessToken))
      .Then<UserToken, UserToken>(x => x.Sub == id
        ? x
        : new DomainProblemException(new InvalidUserToken("Sub of tokens do not match auth token", "ID/Access", []))
      )
      .ThenAwait(x => service.Update(id, x.ToRecord(), null))
      .Then(x => (x?.ToRes()).ToResult());

    return this.ReturnNullableResult(user, new EntityNotFound(
      "User Not Found", typeof(UserPrincipal), id));
  }

  [Authorize, HttpDelete("Me")]
  public async Task<ActionResult> DeleteMe()
  {
    // Self-service account deletion: the user id is taken from the token (Sub), never a route
    // param, so a caller can only ever delete THEIR OWN account.
    var sub = this.Sub();

    // DB-first, then Logto (safe partial-failure ordering):
    //  1. DeleteAccount enforces the debt gate BEFORE any destructive write, then hard-deletes all
    //     personal data + anonymize-retains the donation ledger in one transaction. It is idempotent
    //     (a missing user is treated as success), so a retry after a partial failure re-runs cleanly.
    //  2. Only after the DB side commits do we purge the Logto identity (DeleteUser treats 404 as
    //     success). If Logto fails, the request errors but the next retry no-ops the DB and finishes
    //     the Logto purge — no orphaned, unrecoverable PII.
    var result = await this.GuardAsync(sub)
      .ThenAwait(_ => service.DeleteAccount(sub!, appOption.Value.BlockAccountDeletionOnDebt, async () =>
      {
        // Best-effort: revoke the stored Airwallex payment consent/mandate before the payment row
        // is purged (runs only after the debt gate passes). A missing consent (most users) or a
        // provider error must NOT block deletion, so we ignore the outcome and always succeed.
        await paymentService.DisablePaymentConsentAsync(sub!);
        return new Unit();
      }))
      .Then(_ => new Unit(), Errors.MapAll)
      .DoAwait(DoType.MapErrors, _ => authManagement.DeleteUser(sub!));

    return this.ReturnUnitResult(result);
  }

  [Authorize(Policy = AuthPolicies.OnlyAdmin), HttpDelete("{id}")]
  public async Task<ActionResult> Delete(string id)
  {
    // DB-first, then Logto (mirrors DeleteMe's safe ordering): never leave DB PII orphaned behind a
    // deleted Logto identity. Delete all DB remnants first; then clean up the Logto identity
    // (best-effort — DeleteUser treats 404 as success and we don't fail the op on it). We attempt the
    // Logto purge even when the DB row was already gone, so an orphaned Logto identity (DB deleted but
    // Logto not) is still cleaned up by this endpoint.
    var dbResult = await service.DeleteAllRemnants(id);
    if (dbResult.IsSuccess())
      await authManagement.DeleteUser(id);

    return this.ReturnUnitNullableResult(dbResult, new EntityNotFound(
      "User Not Found", typeof(UserPrincipal), id));
  }
}
