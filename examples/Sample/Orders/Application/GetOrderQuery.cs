using Mediator;
using Trellis;
using Trellis.Authorization;
using Trellis.Microservices.Sample.Orders.Domain;

namespace Trellis.Microservices.Sample.Orders.Application;

// Read one order.
//
// IAuthorizeResource<Order>: Authorize is invoked AFTER the pipeline loads the
// order (via OrderResourceLoader). For the read action ANY actor with the
// orders:read static permission may view ANY order — the resource-side check
// is a no-op (Result.Ok). That's the read-all half of the read-all/write-mine
// model the sample demonstrates.
//
// IIdentifyResource<Order, OrderId>: lets ResourceAuthorizationBehavior route
// to SharedResourceLoaderById<Order, OrderId> automatically (no per-command
// loader needed).
//
// IAuthorize: static-permission gate that runs BEFORE resource loading. If
// the actor lacks orders:read the pipeline short-circuits with 403 — and
// the load never happens (proves the orders.resource_loads counter is gated
// on permissions too).
public sealed record GetOrderQuery(OrderId Id)
    : IQuery<Result<Order>>, IAuthorize, IAuthorizeResource<Order>, IIdentifyResource<Order, OrderId>
{
    public IReadOnlyList<string> RequiredPermissions => ["orders:read"];

    public OrderId GetResourceId() => Id;

    public IResult Authorize(Actor actor, Order resource) => Result.Ok();
}
