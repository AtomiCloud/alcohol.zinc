using CSharp_Result;
using Domain.Exceptions;
using Domain.User;

namespace UnitTest.User;

/// <summary>
/// Unit tests for <see cref="UserService.DeleteAccount"/> — the debt gate and idempotency
/// semantics of self-service account deletion, exercised with hand-rolled fakes.
/// </summary>
public class DeleteAccountTests
{
  private static UserService Build(FakeUserRepository repo, FakeStreakRepository streak) =>
    new(repo, streak, new ImmediateTransactionManager());

  [Fact]
  public async Task BlockOnDebt_WithOutstandingDebt_IsRejected_AndNothingIsDeleted()
  {
    var repo = new FakeUserRepository();
    var streak = new FakeStreakRepository { OpenDebts = { FakeStreakRepository.Debt(12.50m, "SGD") } };
    var sut = Build(repo, streak);

    var result = await sut.DeleteAccount("user-1", blockOnDebt: true);

    result.IsSuccess().Should().BeFalse("an outstanding debt must block deletion when the flag is on");
    var error = result.FailureOrDefault();
    error.Should().BeOfType<AccountDeletionBlockedException>();
    var blocked = (AccountDeletionBlockedException)error;
    blocked.TotalDebt.Should().Be(12.50m);
    blocked.Currency.Should().Be("SGD");

    // Guardrail: the gate runs BEFORE any destructive write — no data may be deleted.
    repo.DeleteAllRemnantsCalls.Should().BeEmpty();
  }

  [Fact]
  public async Task BlockOnDebt_SumsAllOpenDebtItems()
  {
    var repo = new FakeUserRepository();
    var streak = new FakeStreakRepository
    {
      OpenDebts = { FakeStreakRepository.Debt(2m), FakeStreakRepository.Debt(3m), FakeStreakRepository.Debt(5m) },
    };
    var sut = Build(repo, streak);

    var result = await sut.DeleteAccount("user-1", blockOnDebt: true);

    result.IsSuccess().Should().BeFalse();
    ((AccountDeletionBlockedException)result.FailureOrDefault()).TotalDebt.Should().Be(10m);
  }

  [Fact]
  public async Task BlockOnDebt_WithNoDebt_ProceedsToDelete()
  {
    var repo = new FakeUserRepository();
    var streak = new FakeStreakRepository(); // no open debts
    var sut = Build(repo, streak);

    var result = await sut.DeleteAccount("user-1", blockOnDebt: true);

    result.IsSuccess().Should().BeTrue();
    repo.DeleteAllRemnantsCalls.Should().ContainSingle().Which.Should().Be("user-1");
  }

  [Fact]
  public async Task FlagOff_WithOutstandingDebt_StillDeletes_AndDebtIsNotEvenChecked()
  {
    var repo = new FakeUserRepository();
    var streak = new FakeStreakRepository { OpenDebts = { FakeStreakRepository.Debt(99m) } };
    var sut = Build(repo, streak);

    var result = await sut.DeleteAccount("user-1", blockOnDebt: false);

    result.IsSuccess().Should().BeTrue("with the flag off, debt must not block deletion");
    repo.DeleteAllRemnantsCalls.Should().ContainSingle();
    streak.GetOpenDebtsForUserCalls.Should().BeEmpty("debt should not be queried when the gate is off");
  }

  [Fact]
  public async Task MissingUser_IsIdempotentSuccess_NotAnError()
  {
    // Mimics a retry after a partial failure (DB already deleted): deleting an absent user
    // must succeed (null = already gone), so the caller can safely retry to finish Logto purge.
    var repo = new FakeUserRepository { UserExists = false };
    var streak = new FakeStreakRepository();
    var sut = Build(repo, streak);

    var result = await sut.DeleteAccount("ghost", blockOnDebt: true);

    result.IsSuccess().Should().BeTrue();
    result.Get().Should().BeNull("a missing user is treated as already-deleted, not a 404 error");
  }

  [Fact]
  public async Task OnBeforePurge_RunsAfterGate_ButBeforeAnyDataIsDeleted()
  {
    var repo = new FakeUserRepository();
    var streak = new FakeStreakRepository();
    var sut = Build(repo, streak);

    var purgeCountWhenCallbackRan = -1;
    var result = await sut.DeleteAccount("user-1", blockOnDebt: true, onBeforePurge: () =>
    {
      purgeCountWhenCallbackRan = repo.DeleteAllRemnantsCalls.Count;
      return new Unit().ToAsyncResult();
    });

    result.IsSuccess().Should().BeTrue();
    purgeCountWhenCallbackRan.Should().Be(0, "onBeforePurge must run before any data is deleted");
    repo.DeleteAllRemnantsCalls.Should().ContainSingle("the purge still runs after the callback");
  }

  [Fact]
  public async Task BlockedByDebt_DoesNotRunOnBeforePurge()
  {
    // The Airwallex revoke (onBeforePurge) must NOT fire when deletion is blocked by debt —
    // otherwise we'd disable a paying user's payment method while refusing to delete the account.
    var repo = new FakeUserRepository();
    var streak = new FakeStreakRepository { OpenDebts = { FakeStreakRepository.Debt(5m) } };
    var sut = Build(repo, streak);

    var callbackRan = false;
    var result = await sut.DeleteAccount("user-1", blockOnDebt: true, onBeforePurge: () =>
    {
      callbackRan = true;
      return new Unit().ToAsyncResult();
    });

    result.IsSuccess().Should().BeFalse();
    callbackRan.Should().BeFalse("provider cleanup must not run when the deletion is blocked");
    repo.DeleteAllRemnantsCalls.Should().BeEmpty();
  }

  [Fact(Skip = "Penalty module is not on main yet (lives on Yek-Khan/habit-penalty-feature). " +
              "Un-skip once it merges: deletion must ANONYMIZE-RETAIN penalty/charity-donation rows " +
              "(set UserId -> sentinel, keep amount/currency/charity/timestamp/PaymentIntentId), NOT delete them.")]
  public void AnonymizeRetain_PenaltyLedger_KeepsFinancialRecordButStripsUser()
  {
    // Expected behavior to assert when Penalty exists:
    //   - Penalty rows for the user are RETAINED (not deleted),
    //   - their UserId is replaced with the deleted-user sentinel,
    //   - AmountCents / Currency / CharityId / CreatedAt / PaymentIntentId are unchanged,
    //   - CharityBalance (no user PII) is untouched.
    throw new NotImplementedException();
  }
}
