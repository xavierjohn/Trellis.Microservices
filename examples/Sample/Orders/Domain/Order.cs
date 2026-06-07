namespace Trellis.Microservices.Sample.Orders.Domain;

// In-memory mutable POCO for the sample. A real service would derive from
// Trellis.Authorization.Aggregate<OrderId> + use value objects for OwnerId,
// Customer, Total — but the sample's focus is the AUTH pipeline, not the DDD
// primitives, so the body is intentionally minimal.
//
// Notably mutable: `Update(...)` mutates in place. That mutation is what
// proves the v4 accessor pattern works — the handler reads the SAME instance
// the auth pipeline loaded, mutates it, and the change persists in the
// in-memory store (no save call required because the store IS the instance).
public sealed class Order
{
    public Order(OrderId id, string ownerId, string customer, decimal total)
    {
        Id = id;
        OwnerId = ownerId;
        Customer = customer;
        Total = total;
    }

    public OrderId Id { get; }

    // Plain string for simplicity; ActorId from Trellis.Authorization could be
    // used in a real service. The sample compares against Actor.Id.Value
    // (ordinal string) so a plain string here keeps the read site obvious.
    public string OwnerId { get; }

    public string Customer { get; private set; }

    public decimal Total { get; private set; }

    public void Update(string customer, decimal total)
    {
        Customer = customer;
        Total = total;
    }
}
