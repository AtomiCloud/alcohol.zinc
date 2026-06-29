using Domain.Disbursement;
using Microsoft.Extensions.Logging.Abstractions;
using NodaMoney;

namespace UnitTest.Disbursement;

// Service-level tests for the payout matrix: donate-success, donate-failure-release,
// per-group isolation, and the reconcile (found -> mark, not-found -> release) paths.
public class DisbursementServiceTests
{
  private static readonly DonorIdentity Donor = new()
  {
    FirstName = "LazyTax",
    LastName = "Donations",
    Email = "donations@lazytax.club"
  };

  private static ClaimedPayout Payout(decimal amount = 5.00m, string currency = "USD", string org = "org_1")
    => new()
    {
      DisbursementId = Guid.NewGuid(),
      CharityId = Guid.NewGuid(),
      PledgeOrganizationId = org,
      Amount = new Money(amount, Currency.FromCode(currency))
    };

  private static DisbursementPrincipal Inflight(decimal amount = 5.00m, string currency = "USD")
    => new()
    {
      Id = Guid.NewGuid(),
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow,
      Record = new DisbursementRecord
      {
        CharityId = Guid.NewGuid(),
        Amount = new Money(amount, Currency.FromCode(currency)),
        PledgeOrganizationId = "org_1",
        Status = DisbursementStatus.Pending,
        ProviderDonationId = null,
        Attempts = 0,
        LastError = null
      }
    };

  private static DisbursementService Build(FakeDisbursementRepository repo, FakeDonationGateway gw)
    => new(repo, gw, NullLogger<DisbursementService>.Instance);

  [Fact]
  public async Task DonationSucceeds_MarksDisbursedOnce_AndCounts()
  {
    var p = Payout();
    var repo = new FakeDisbursementRepository(claim: [p]);
    var gw = FakeDonationGateway.Succeeds("don_123");
    var svc = Build(repo, gw);

    var res = await svc.ProcessPending(Donor, minPayoutCents: 0, maxGroups: 100);

    res.IsSuccess().Should().BeTrue();
    ((int)res).Should().Be(1);
    repo.MarkDisbursedCalls.Should().ContainSingle();
    repo.MarkDisbursedCalls[0].Should().Be((p.DisbursementId, "don_123"));
    repo.MarkFailedCalls.Should().BeEmpty();
    // Donation carried the disbursement id as the reconcile reference + the LazyTax donor.
    gw.CreateCalls.Should().ContainSingle();
    gw.CreateCalls[0].Reference.Should().Be(p.DisbursementId.ToString());
    gw.CreateCalls[0].DonorEmail.Should().Be("donations@lazytax.club");
    gw.CreateCalls[0].Amount.Amount.Should().Be(5.00m);
  }

  [Fact]
  public async Task DefinitiveRejection_MarksFailed_ReleasesForRetry_NotCounted()
  {
    // A 4xx provider rejection means nothing was created -> safe to release the penalties.
    var p = Payout();
    var repo = new FakeDisbursementRepository(claim: [p]);
    var gw = FakeDonationGateway.Rejects("rejected with status 422");
    var svc = Build(repo, gw);

    var res = await svc.ProcessPending(Donor, 0, 100);

    ((int)res).Should().Be(0);
    repo.MarkFailedCalls.Should().ContainSingle();
    repo.MarkFailedCalls[0].Id.Should().Be(p.DisbursementId);
    repo.MarkDisbursedCalls.Should().BeEmpty();
  }

  [Fact]
  public async Task AmbiguousFailure_StaysPending_DoesNotReleaseOrReDonate()
  {
    // The money-twice guard: a 5xx/timeout/unknown failure may mean the donation landed. The
    // disbursement must stay Pending (no MarkFailed -> penalties NOT released) so reconciliation
    // settles it; otherwise a re-claim would donate a second time.
    var p = Payout();
    var repo = new FakeDisbursementRepository(claim: [p]);
    var gw = FakeDonationGateway.Fails(new Exception("503 gateway timeout"));
    var svc = Build(repo, gw);

    var res = await svc.ProcessPending(Donor, 0, 100);

    ((int)res).Should().Be(0);
    repo.MarkFailedCalls.Should().BeEmpty();    // penalties NOT released
    repo.MarkDisbursedCalls.Should().BeEmpty(); // not completed either -> left Pending
  }

  [Fact]
  public async Task CreateThrows_IsolatedToThatGroup_LeavesItPending_BatchKeepsDonating()
  {
    // An unhandled throw on one group must NOT abort the batch — and (money-twice guard) must NOT
    // release that group's penalties (the throw can follow an accepted donation). It stays Pending;
    // the next group still donates.
    var p1 = Payout();
    var p2 = Payout();
    var repo = new FakeDisbursementRepository(claim: [p1, p2]);
    var gw = FakeDonationGateway.ThrowsOnFirstThenSucceeds(new Exception("malformed"), "don_ok");
    var svc = Build(repo, gw);

    var res = await svc.ProcessPending(Donor, 0, 100);

    res.IsSuccess().Should().BeTrue();                                   // pass did not crash
    repo.MarkFailedCalls.Should().BeEmpty();                             // p1 throw -> left Pending, NOT released
    repo.MarkDisbursedCalls.Should().ContainSingle().Which.Should().Be((p2.DisbursementId, "don_ok")); // p2 still donated
    ((int)res).Should().Be(1);
  }

  [Fact]
  public async Task EachGroupDonatedExactlyOnce()
  {
    var p1 = Payout();
    var p2 = Payout();
    var repo = new FakeDisbursementRepository(claim: [p1, p2]);
    var gw = FakeDonationGateway.Succeeds("don_x");
    var svc = Build(repo, gw);

    var res = await svc.ProcessPending(Donor, 0, 100);

    ((int)res).Should().Be(2);
    repo.MarkDisbursedCalls.Select(c => c.Id).Should().BeEquivalentTo(new[] { p1.DisbursementId, p2.DisbursementId });
    repo.MarkDisbursedCalls.Select(c => c.Id).Distinct().Should().HaveCount(2);
  }

  [Fact]
  public async Task Reconcile_FindsExistingDonation_MarksDisbursed_DoesNotReDonate()
  {
    // Crash-after-donation-before-mark: the donation already landed at the vendor. Reconcile must
    // mark it Completed via the looked-up id, NOT issue a second donation.
    var stale = Inflight();
    var repo = new FakeDisbursementRepository(reconcilable: [stale]);
    var gw = FakeDonationGateway.FindsExisting("don_found");
    var svc = Build(repo, gw);

    await svc.ProcessPending(Donor, 0, 100);

    gw.FindCalls.Should().ContainSingle().Which.Should().Be(stale.Id.ToString());
    repo.MarkDisbursedCalls.Should().ContainSingle();
    repo.MarkDisbursedCalls[0].Should().Be((stale.Id, "don_found"));
    gw.CreateCalls.Should().BeEmpty();          // never re-donated
    repo.MarkFailedCalls.Should().BeEmpty();
  }

  [Fact]
  public async Task Reconcile_FindsNothing_ReleasesForRetry()
  {
    // The donation never landed at the vendor -> release the penalties (MarkFailed) for a fresh retry.
    var stale = Inflight();
    var repo = new FakeDisbursementRepository(reconcilable: [stale]);
    var gw = FakeDonationGateway.FindsNothing();
    var svc = Build(repo, gw);

    await svc.ProcessPending(Donor, 0, 100);

    gw.FindCalls.Should().ContainSingle();
    repo.MarkFailedCalls.Should().ContainSingle().Which.Id.Should().Be(stale.Id);
    repo.MarkDisbursedCalls.Should().BeEmpty();
  }
}
