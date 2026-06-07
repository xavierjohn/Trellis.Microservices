using Mediator;
using Trellis;
using Trellis.Authorization;
using Trellis.Microservices.Sample.Orders.Domain;

namespace Trellis.Microservices.Sample.Orders.Application;

// Reads the SAME Order instance that ResourceAuthorizationBehavior loaded for
// Authorize. Does NOT inject IOrderRepository. The accessor GetRequiredResource()
// returns the in-memory instance — zero second roundtrip, zero second metric tick.
//
// If this handler were rewritten to inject IOrderRepository and call FindByIdAsync
// directly, the orders.resource_loads counter would tick TWICE per request: once
// from the pipeline load (in ResourceAuthorizationBehavior) and once from the
// handler load. That counter is the falsifiable proof of the load-once invariant.
public sealed class GetOrderHandler : IQueryHandler<GetOrderQuery, Result<Order>>
{
    private readonly IAuthorizedResource<GetOrderQuery, Order> _authorized;

    public GetOrderHandler(IAuthorizedResource<GetOrderQuery, Order> authorized) => _authorized = authorized;

    public ValueTask<Result<Order>> Handle(GetOrderQuery query, CancellationToken cancellationToken) =>
        new(Result.Ok(_authorized.GetRequiredResource()));
}
