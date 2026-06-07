using Mediator;
using Trellis.Authorization;
using Trellis.Microservices.Sample.Orders.Domain;

namespace Trellis.Microservices.Sample.Orders.Application;

// Edit one order. ResourceAuthorizationBehavior loads the resource and calls
// Authorize(actor, order). The owner check is the real authorization decision:
// only the actor whose id matches order.OwnerId may mutate. Anyone else gets 403.
//
// The handler then reads the SAME instance via IAuthorizedResource<TCommand,Order>
// — no second repository roundtrip. See Application/UpdateOrderHandler.cs.
public sealed record UpdateOrderCommand(OrderId Id, string Customer, decimal Total)
    : ICommand<Result<Unit>>, IAuthorize, IAuthorizeResource<Order>, IIdentifyResource<Order, OrderId>
{
    public IReadOnlyList<string> RequiredPermissions => ["orders:write"];

    public OrderId GetResourceId() => Id;

    public IResult Authorize(Actor actor, Order resource) =>
        string.Equals(resource.OwnerId, actor.Id.Value, StringComparison.Ordinal)
            ? Result.Ok()
            : Result.Fail(new Error.Forbidden("orders.not_owner")
            {
                Detail = "Only the order's owner can edit it.",
            });
}
