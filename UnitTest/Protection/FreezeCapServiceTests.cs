using CSharp_Result;
using Domain.Protection;
using UnitTest.User;

namespace UnitTest.Protection;

// FreezeCapService.Reconcile: a user never holds more banked freezes than their current cap.
public class FreezeCapServiceTests
{
  private const string UserId = "user-cap-1";

  private static (FreezeCapService Svc, FakeProtectionRepository Prot, FakeEntitlementService Ent) Build(
    int balance, int cap, int maxStreak = 0)
  {
    var prot = new FakeProtectionRepository(freezeBalance: balance);
    var ent = new FakeEntitlementService { FreezeCap = cap };
    var streaks = new FakeStreakRepository { MaxStreak = maxStreak };
    return (new FreezeCapService(prot, streaks, ent), prot, ent);
  }

  [Fact]
  public async Task Reconcile_BalanceAboveCap_ClampsAndReturnsCap()
  {
    var (svc, prot, ent) = Build(balance: 10, cap: 3, maxStreak: 42);

    var res = await svc.Reconcile(UserId);

    res.Get().Should().Be(new FreezeBalance(3, 3));
    prot.ClampFreezeToCapCalls.Should().ContainSingle().Which.Should().Be((UserId, 3));
    prot.BalanceFor(UserId).Should().Be(3);
    ent.GetFreezeCapForUserCalls.Should().ContainSingle().Which.Should().Be((UserId, 42),
      "the cap includes the user's streak bonus");
  }

  [Fact]
  public async Task Reconcile_CapZero_ZeroesBalance()
  {
    var (svc, prot, _) = Build(balance: 4, cap: 0);

    var res = await svc.Reconcile(UserId);

    res.Get().Should().Be(new FreezeBalance(0, 0));
    prot.BalanceFor(UserId).Should().Be(0);
  }

  [Theory]
  [InlineData(2, 3)]
  [InlineData(3, 3)]
  [InlineData(0, 0)]
  public async Task Reconcile_BalanceAtOrBelowCap_LeavesBalance(int balance, int cap)
  {
    var (svc, prot, _) = Build(balance, cap);

    var res = await svc.Reconcile(UserId);

    res.Get().Should().Be(new FreezeBalance(balance, cap));
    prot.ClampFreezeToCapCalls.Should().BeEmpty();
    prot.BalanceFor(UserId).Should().Be(balance);
  }

  [Fact]
  public async Task Reconcile_CapLookupFails_ReturnsFailureAndDoesNotClamp()
  {
    var (svc, prot, ent) = Build(balance: 10, cap: 3);
    var failure = new InvalidOperationException("plan lookup failed");
    ent.FreezeCapFails = failure;

    var res = await svc.Reconcile(UserId);

    res.IsFailure().Should().BeTrue();
    res.FailureOrDefault().Should().BeSameAs(failure);
    prot.ClampFreezeToCapCalls.Should().BeEmpty();
    prot.BalanceFor(UserId).Should().Be(10);
  }
}
