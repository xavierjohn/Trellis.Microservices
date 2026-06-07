using Mediator;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Trellis;
using Trellis.Asp;
using Trellis.Authorization;
using Trellis.Mediator;
using Trellis.Microservices.AspNetCore;
using Trellis.Microservices.Sample.Orders.Application;
using Trellis.Microservices.Sample.Orders.Domain;
using Trellis.Microservices.Sample.Orders.Infrastructure;

// Orders microservice — first of the two downstream services behind the YARP gateway.
//
// Audience: "orders" (matches the YARP cluster name → AudiencePerCluster).
// Path:     /api/orders (list), /api/orders/{id} (get + put)
//
// This service demonstrates BOTH halves of the Trellis security pyramid:
//   1. Trust-boundary  — JwtBearer validation + TrellisInternalJwtActorProvider hydration
//      (Recipe 1 strict profile from the microservices cookbook)
//   2. Resource-based  — IAuthorizeResource<Order> + the v4 IAuthorizedResource<,>
//      accessor pattern (Recipe 31 from the upstream Trellis cookbook)
//      → orders.resource_loads counter PROVES "load once" — see ARCHITECTURE.md §11.5
//
// Billing/ is intentionally trust-boundary-only as the counter-example so readers
// can contrast the two shapes.

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Surface the Sample.Orders meter (orders.resource_loads) in the Aspire dashboard
// Metrics tab. ServiceDefaults already wired the OTLP exporter + the stock
// AspNetCore/HttpClient/Runtime instrumentation; this adds the per-service meter.
builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m.AddMeter(OrdersMetrics.MeterName));

// === Trust-boundary layer (Recipe 1) =========================================

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        // Gate dev-only options on IsDevelopment so a copy/paste into a
        // production composition root keeps RequireHttpsMetadata=true (the
        // ASP.NET Core default) and IncludeErrorDetails=false (don't leak
        // JWT validation failure reasons to the wire).
        var isDev = builder.Environment.IsDevelopment();

        o.Authority = "http://localhost:5001";
        o.Audience = "orders";
        o.RequireHttpsMetadata = !isDev;         // dev only: allow http JWKS discovery
        o.IncludeErrorDetails = isDev;           // dev only: surface real failure reason in WWW-Authenticate
        o.MapInboundClaims = false;
        o.SaveToken = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "http://localhost:5001",
            ValidateAudience = true,
            ValidAudience = "orders",
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ClockSkew = TimeSpan.FromSeconds(30),
            TryAllIssuerSigningKeys = false,
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddTrellisInternalJwtActorProvider(o =>
{
    o.ExpectedIssuer = "http://localhost:5001";
    o.ExpectedAudience = "orders";

    // Project the tenant_id ABAC claim through to Actor.Attributes + require it
    // (Recipe 2 "Tenant-isolation defense in depth" in the microservices cookbook).
    o.AttributeClaimMap["tenant_id"] = "tenant_id";
    o.RequiredAttributes = ["tenant_id"];
});

// === Domain + infrastructure ================================================

builder.Services.AddSingleton<IOrderRepository, InMemoryOrderRepository>();

// === Mediator + resource-based authorization layer (Recipe 7 + Recipe 31) ===

// Handlers MUST be Scoped because IAuthorizedResource<TMessage, TResource>
// (the v4 typed accessor that ResourceAuthorizationBehavior populates) is
// registered as scoped. Mediator's default is Singleton, so this opt-in is
// required for the resource-auth pipeline to wire correctly.
builder.Services.AddMediator(o => o.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddTrellisBehaviors();      // Tracing/Logging/Authorization/Exception
builder.Services.AddResourceAuthorization(typeof(Order).Assembly);
                                             // Scans for IAuthorizeResource<Order>
                                             // + IIdentifyResource<Order, OrderId>
                                             // → bridges to OrderResourceLoader
                                             // + registers IAuthorizedResource<,> accessor

var app = builder.Build();
app.MapDefaultEndpoints();
app.UseAuthentication();
app.UseAuthorization();

// === Endpoints — dispatch via mediator, translate Result→HTTP via ToHttpResponse ===

app.MapGet("/api/orders", async (IMediator mediator, CancellationToken ct) =>
{
    var result = await mediator.Send(new ListOrdersQuery(), ct);
    return result.ToHttpResponse(orders => orders.Select(OrderResponse.From).ToArray());
}).RequireAuthorization();

app.MapGet("/api/orders/{id}", async (string id, IMediator mediator, CancellationToken ct) =>
{
    if (!OrderId.TryCreate(id).TryGetValue(out var orderId))
        return Results.BadRequest(new { error = "invalid_order_id" });

    var result = await mediator.Send(new GetOrderQuery(orderId), ct);
    return result.ToHttpResponse(OrderResponse.From);
}).RequireAuthorization();

app.MapPut("/api/orders/{id}", async (string id, UpdateOrderRequest body, IMediator mediator, CancellationToken ct) =>
{
    if (!OrderId.TryCreate(id).TryGetValue(out var orderId))
        return Results.BadRequest(new { error = "invalid_order_id" });

    var result = await mediator.Send(new UpdateOrderCommand(orderId, body.Customer, body.Total), ct);
    return result.ToHttpResponse();   // Result<Unit> → 204 No Content on success
}).RequireAuthorization();

app.Run();

// === Wire-format DTOs (kept inline for readability — single Program.cs scan) ===

// Intentionally NOT validated — see Order.Update for the rationale. A null
// Customer or negative Total deserializes successfully here and flows
// straight into the aggregate, which stores it. The sample's focus is the
// auth pipeline; production would either add FluentValidation here or
// switch UpdateOrderCommand to IValidate so Trellis.Mediator's
// ValidationBehavior runs before resource auth.
internal sealed record UpdateOrderRequest(string Customer, decimal Total);

internal sealed record OrderResponse(string Id, string OwnerId, string Customer, decimal Total)
{
    public static OrderResponse From(Order order) =>
        new(order.Id.Value, order.OwnerId, order.Customer, order.Total);
}
