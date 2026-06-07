using Mediator;
using Trellis.Authorization;
using Trellis.Microservices.Sample.Orders.Domain;

namespace Trellis.Microservices.Sample.Orders.Application;

// List all orders. Static permission check only — there is no per-row resource
// auth on a list endpoint (the resource the request authorizes against doesn't
// have a single id). A real service might switch to a tenant-scoped query or
// per-row visibility filter; this sample keeps it simple to focus on the
// load-once + ownership pattern on the single-resource endpoints.
public sealed record ListOrdersQuery
    : IQuery<Result<IReadOnlyList<Order>>>, IAuthorize
{
    public IReadOnlyList<string> RequiredPermissions => ["orders:read"];
}
