using System.Diagnostics.Metrics;

namespace Trellis.Microservices.Sample.Orders.Infrastructure;

// Sample.Orders meter — registered in Program.cs via
//   builder.Services.AddOpenTelemetry().WithMetrics(m => m.AddMeter(OrdersMetrics.MeterName))
// so the Aspire dashboard surfaces it in the Metrics tab alongside the
// stock AspNetCore / HttpClient / Runtime instrumentation.
//
// The ResourceLoads counter increments INSIDE InMemoryOrderRepository.FindByIdAsync
// (the ACL boundary). That placement matters: it counts every load that crosses
// the boundary, including any handler that bypasses the v4 accessor and re-loads
// via the repository directly. If the counter ever shows N=2 per single request,
// the v4 accessor pattern has regressed.
//
// See examples/Sample/ARCHITECTURE.md §11 — "Proving load-once with metrics".
public static class OrdersMetrics
{
    public const string MeterName = "Sample.Orders";

    public static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> ResourceLoads = Meter.CreateCounter<long>(
        name: "orders.resource_loads",
        unit: "{load}",
        description: "Number of times an Order aggregate was loaded from the repository (ACL boundary).");
}
