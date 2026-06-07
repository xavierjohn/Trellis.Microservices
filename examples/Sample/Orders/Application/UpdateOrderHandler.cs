using Mediator;
using Trellis.Authorization;
using Trellis.Microservices.Sample.Orders.Domain;

namespace Trellis.Microservices.Sample.Orders.Application;

// The mutation path. Reads the SAME Order instance ResourceAuthorizationBehavior
// loaded for Authorize, mutates it in place, returns Result.Ok(Unit.Value).
//
// In-memory store quirk: the dictionary holds the SAME reference, so mutating
// the instance from the accessor automatically updates the "stored" order
// without a save call. A real EF service would call SaveChangesAsync (or rely
// on TransactionalCommandBehavior from Trellis.EntityFrameworkCore).
public sealed class UpdateOrderHandler : ICommandHandler<UpdateOrderCommand, Result<Unit>>
{
    private readonly IAuthorizedResource<UpdateOrderCommand, Order> _authorized;

    public UpdateOrderHandler(IAuthorizedResource<UpdateOrderCommand, Order> authorized) => _authorized = authorized;

    public ValueTask<Result<Unit>> Handle(UpdateOrderCommand command, CancellationToken cancellationToken)
    {
        _authorized.GetRequiredResource().Update(command.Customer, command.Total);
        return new(Result.Ok(Unit.Value));
    }
}
