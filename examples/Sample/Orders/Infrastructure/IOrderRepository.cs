using Trellis;
using Trellis.Microservices.Sample.Orders.Domain;

namespace Trellis.Microservices.Sample.Orders.Infrastructure;

// Repository contract for Order. Find* returns Maybe<T> per the Trellis repo
// convention (Get* would return Result<T> with Error.NotFound when missing).
public interface IOrderRepository
{
    ValueTask<Maybe<Order>> FindByIdAsync(OrderId id, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<Order>> ListAllAsync(CancellationToken cancellationToken);
}
