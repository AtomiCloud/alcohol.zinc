using System.Security.Claims;
using System.Text.Json;
using App.Modules.Subscription.API.V1;
using Domain.Subscription;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace UnitTest.Subscription;

public class SubscriptionResponseTests
{
  public static IEnumerable<object[]> States()
  {
    foreach (var status in Enum.GetValues<SubscriptionStatus>())
    foreach (var cancelled in new[] { false, true })
    foreach (var days in new[] { -9, -3, -1, 2 })
    foreach (var tier in new[] { "pro", "removed" })
    {
      // Explicit expected product matrix, independent of the resolver.
      var paid = tier == "pro" && status is SubscriptionStatus.Active or SubscriptionStatus.Grace
        && days != -9 && (!cancelled || days > 0);
      yield return [status, cancelled, days, tier, paid ? "pro" : "free",
        paid ? (status == SubscriptionStatus.Active ? "active" : "grace") : "none"];
    }
  }

  [Theory]
  [MemberData(nameof(States))]
  public async Task Get_ResponseAndEntitlementMatchExpectedStateWithoutWriting(
    SubscriptionStatus status, bool cancelled, int daysUntilEnd, string tier,
    string expectedTier, string expectedStatus)
  {
    var now = DateTime.UtcNow;
    var row = new UserSubscriptionPrincipal
    {
      Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now,
      Record = new UserSubscriptionRecord
      {
        UserId = "u1", Tier = tier, Status = status,
        PeriodStart = now.AddDays(-30), PeriodEnd = now.AddDays(daysUntilEnd),
        CancelAtPeriodEnd = cancelled, NextTier = "ultimate"
      }
    };
    var repo = new FakeSubscriptionRepository(row);
    var controller = new SubscriptionController(repo, null!, null!, null!, new(), new(), new(), null!, FakePlanProvider.Default())
    {
      ControllerContext = new ControllerContext
      {
        HttpContext = new DefaultHttpContext
        {
          User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "u1")], "test"))
        }
      }
    };

    var result = await controller.Get("u1");

    var response = ((OkObjectResult)result.Result!).Value.Should().BeOfType<SubscriptionRes>().Subject;
    response.Tier.Should().Be(expectedTier);
    response.Status.Should().Be(expectedStatus);
    ((string)await new SubscriptionService(repo, FakePlanProvider.Default()).GetUserTier("u1"))
      .Should().Be(expectedTier);
    if (expectedTier == "free")
    {
      response.PeriodStart.Should().BeNull();
      response.PeriodEnd.Should().BeNull();
      response.CancelAtPeriodEnd.Should().BeFalse();
      response.NextTier.Should().BeNull();
    }
    else
    {
      response.PeriodStart.Should().Be(row.Record.PeriodStart.ToString("O"));
      response.PeriodEnd.Should().Be(row.Record.PeriodEnd.ToString("O"));
      response.CancelAtPeriodEnd.Should().Be(cancelled);
      response.NextTier.Should().Be("ultimate");
    }

    var json = JsonSerializer.SerializeToElement(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    json.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
      "userId", "tier", "status", "periodStart", "periodEnd", "cancelAtPeriodEnd", "nextTier");
    json.GetProperty("cancelAtPeriodEnd").GetBoolean().Should().Be(response.CancelAtPeriodEnd);
    repo.Row("u1").Should().Be(row);
  }
}
