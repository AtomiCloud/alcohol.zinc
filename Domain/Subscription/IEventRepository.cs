using CSharp_Result;

namespace Domain.Subscription;

public interface ISubscriptionEventRepository
{
  // Append-only: there is deliberately no update or delete.
  Task<Result<SubscriptionEventPrincipal>> Append(SubscriptionEventRecord record);
  // Newest first, for the billing-history surface.
  Task<Result<List<SubscriptionEventPrincipal>>> ListByUser(string userId, int limit);
}
