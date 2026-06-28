using Mediator;
using Trellis.Microservices.Sample.Orders.Domain;
using Trellis.Microservices.Sample.Orders.Infrastructure;

namespace Trellis.Microservices.Sample.Orders.Application;

// Lists everything from the repository. Does NOT trigger the per-id
// orders.resource_loads counter because it doesn't call FindByIdAsync —
// the load-once counter is intentionally per-id only.
public sealed class ListOrdersHandler : IQueryHandler<ListOrdersQuery, Result<IReadOnlyList<Order>>>
{
    private readonly IOrderRepository _repository;

    public ListOrdersHandler(IOrderRepository repository) => _repository = repository;

    public async ValueTask<Result<IReadOnlyList<Order>>> Handle(ListOrdersQuery query, CancellationToken cancellationToken)
    {
        var orders = await _repository.ListAllAsync(cancellationToken);
        return Result.Ok(orders);
    }
}
