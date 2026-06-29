using CSharp_Result;
using Domain.Disbursement;

namespace UnitTest.Disbursement;

// Hand-rolled fakes implementing the real L3/L4 contracts so the service unit tests can assert
// the donate/reconcile/isolation matrix without a mocking framework (Moq is not referenced).

public sealed class FakeDisbursementRepository(
  IEnumerable<ClaimedPayout>? claim = null,
  IEnumerable<DisbursementPrincipal>? reconcilable = null) : IDisbursementRepository
{
  private readonly List<ClaimedPayout> _claim = [.. claim ?? []];
  private readonly List<DisbursementPrincipal> _reconcilable = [.. reconcilable ?? []];

  public List<(Guid Id, string ProviderDonationId)> MarkDisbursedCalls { get; } = [];
  public List<(Guid Id, string Error)> MarkFailedCalls { get; } = [];
  public int ClaimCalls { get; private set; }

  public Task<Result<List<ClaimedPayout>>> ClaimPendingPayouts(long minPayoutCents, int maxGroups)
  {
    ClaimCalls++;
    return Task.FromResult<Result<List<ClaimedPayout>>>(_claim.Take(maxGroups).ToList());
  }

  public Task<Result<Unit>> MarkDisbursed(Guid disbursementId, string providerDonationId)
  {
    MarkDisbursedCalls.Add((disbursementId, providerDonationId));
    return Task.FromResult<Result<Unit>>(new Unit());
  }

  public Task<Result<Unit>> MarkFailed(Guid disbursementId, string error)
  {
    MarkFailedCalls.Add((disbursementId, error));
    return Task.FromResult<Result<Unit>>(new Unit());
  }

  public Task<Result<List<DisbursementPrincipal>>> GetReconcilable(TimeSpan olderThan, int batchSize)
    => Task.FromResult<Result<List<DisbursementPrincipal>>>(_reconcilable.Take(batchSize).ToList());
}

public sealed class FakeDonationGateway : IDonationGateway
{
  private readonly Func<DonationRequest, Result<DonationResult>> _create;
  private readonly Func<string, Result<DonationResult?>> _find;

  // If set, the FIRST CreateDonation throws (simulates an unhandled vendor exception); later
  // calls run normally — to test per-group batch isolation.
  public Exception? ThrowOnFirstCreate { get; init; }
  private int _createCalls;

  private FakeDonationGateway(
    Func<DonationRequest, Result<DonationResult>> create,
    Func<string, Result<DonationResult?>> find)
  {
    _create = create;
    _find = find;
  }

  public List<DonationRequest> CreateCalls { get; } = [];
  public List<string> FindCalls { get; } = [];

  public static FakeDonationGateway Succeeds(string donationId = "don_ok")
    => new(_ => new DonationResult { DonationId = donationId, Status = "processed" },
           _ => (DonationResult?)null);

  // Ambiguous failure (5xx/timeout/etc.): the donation may have landed -> caller must keep Pending.
  public static FakeDonationGateway Fails(Exception ex)
    => new(_ => ex, _ => (DonationResult?)null);

  // Definitive provider rejection (4xx): no donation created -> caller releases for retry.
  public static FakeDonationGateway Rejects(string reason = "rejected")
    => new(_ => new DonationRejectedException(reason), _ => (DonationResult?)null);

  // First create throws, later creates succeed.
  public static FakeDonationGateway ThrowsOnFirstThenSucceeds(Exception ex, string donationId = "don_ok")
    => new(_ => new DonationResult { DonationId = donationId, Status = "processed" },
           _ => (DonationResult?)null)
    { ThrowOnFirstCreate = ex };

  // Reconcile: the lookup finds an already-created donation (so the service must NOT re-donate).
  public static FakeDonationGateway FindsExisting(string donationId)
    => new(_ => new DonationResult { DonationId = "should_not_be_called", Status = "processed" },
           _ => new DonationResult { DonationId = donationId, Status = "processed" });

  // Reconcile: the lookup finds nothing (the donation never landed -> release for retry).
  public static FakeDonationGateway FindsNothing()
    => new(_ => new DonationResult { DonationId = "don_ok", Status = "processed" },
           _ => (DonationResult?)null);

  public Task<Result<DonationResult>> CreateDonation(DonationRequest req)
  {
    CreateCalls.Add(req);
    if (ThrowOnFirstCreate != null && _createCalls++ == 0) throw ThrowOnFirstCreate;
    return Task.FromResult(_create(req));
  }

  public Task<Result<DonationResult?>> FindDonationByReference(string reference)
  {
    FindCalls.Add(reference);
    return Task.FromResult(_find(reference));
  }
}
