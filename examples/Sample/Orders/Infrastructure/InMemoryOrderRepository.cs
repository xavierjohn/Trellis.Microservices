using System.Collections.Concurrent;
using Trellis.Microservices.Sample.Orders.Domain;

namespace Trellis.Microservices.Sample.Orders.Infrastructure;

// In-memory order store seeded with one order per actor (john + jill). Real
// services would resolve a DbContext / Cosmos client / HTTP client from DI;
// the seed pattern makes the sample's outcome matrix self-contained.
//
// FindByIdAsync emits the orders.resource_loads counter + a structured log
// line on EVERY call. That instrumentation IS the proof of the "load once"
// invariant — see ARCHITECTURE.md §11.5.
//
// Registered as SINGLETON so the seeded dictionary persists across requests.
// Singleton means no scoped service injection — that's why the counter is
// tagged only with order.id (not actor.id). Per-actor breakdown would
// require either Scoped lifetime + a singleton store wrapper, or
// per-call IServiceProvider resolution; the sample keeps the simpler shape.
public sealed partial class InMemoryOrderRepository : IOrderRepository
{
    private readonly ConcurrentDictionary<string, Order> _orders;
    private readonly ILogger<InMemoryOrderRepository> _logger;

    public InMemoryOrderRepository(ILogger<InMemoryOrderRepository> logger)
    {
        _logger = logger;

        _orders = new ConcurrentDictionary<string, Order>(StringComparer.Ordinal);

        // Seed: one order per demo actor.
        Seed(new Order(MakeId("order-1"), ownerId: "john", customer: "Contoso",     total: 99m));
        Seed(new Order(MakeId("order-2"), ownerId: "jill", customer: "Globex Inc",  total: 149m));
    }

    private static OrderId MakeId(string id) => OrderId.TryCreate(id).GetValueOrThrow($"seed OrderId('{id}') must be valid");

    private void Seed(Order order) => _orders[order.Id.Value] = order;

    public ValueTask<Maybe<Order>> FindByIdAsync(OrderId id, CancellationToken cancellationToken)
    {
        OrdersMetrics.ResourceLoads.Add(
            1,
            new KeyValuePair<string, object?>("order.id", id.Value));

        LogOrderResourceLoaded(_logger, id.Value);

        return ValueTask.FromResult(_orders.TryGetValue(id.Value, out var order)
            ? Maybe.From(order)
            : Maybe<Order>.None);
    }

    public ValueTask<IReadOnlyList<Order>> ListAllAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<Order>>(_orders.Values.OrderBy(o => o.Id.Value, StringComparer.Ordinal).ToArray());

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        EventName = "OrderResourceLoaded",
        Message = "Loaded Order {OrderId} from the ACL repository. This is the signal that proves load-once.")]
    private static partial void LogOrderResourceLoaded(ILogger logger, string orderId);
}
