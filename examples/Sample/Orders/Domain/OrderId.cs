namespace Trellis.Microservices.Sample.Orders.Domain;

// Typed identifier for the Order aggregate. Using RequiredString<TSelf> from
// Trellis.Primitives gives us value-object semantics, validation, JSON conversion,
// and EF/scalar-binding integration for free — without re-implementing equality.
public sealed partial class OrderId : RequiredString<OrderId>;
