using Trellis;
using Trellis.Authorization;
using Trellis.Microservices.Sample.Orders.Domain;

namespace Trellis.Microservices.Sample.Orders.Infrastructure;

// Shared loader used by every command/query that implements
// IIdentifyResource<Order, OrderId>. Bridges from Result<Order> (what the
// authorization pipeline expects) to Maybe<Order> (what the repository
// returns) — translating "not found" into Error.NotFound on the way through.
public sealed class OrderResourceLoader : SharedResourceLoaderById<Order, OrderId>
{
    private readonly IOrderRepository _repository;

    public OrderResourceLoader(IOrderRepository repository) => _repository = repository;

    public override async Task<Result<Order>> GetByIdAsync(OrderId id, CancellationToken cancellationToken)
    {
        var maybe = await _repository.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);

        return maybe.HasValue
            ? Result.Ok(maybe.Value)
            : Result.Fail<Order>(new Error.NotFound(ResourceRef.For<Order>(id.Value)));
    }
}
