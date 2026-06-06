// Aspire AppHost — orchestrates the Trellis.Microservices sample.
//
//   Sample.AppHost
//     ├── orders   (audience="orders",  /api/orders endpoint)
//     ├── billing  (audience="billing", /api/billing endpoint)
//     └── gateway  (YARP, fixed port 5001, references orders + billing)
//
// `dotnet run --project Sample.AppHost` boots all three processes, wires
// service-discovery env vars so YARP destinations resolve `https+http://orders`
// to the dynamically-assigned Orders port, and opens the Aspire dashboard with
// logs, traces, and metrics flowing in from every service.

var builder = DistributedApplication.CreateBuilder(args);

// The two backend microservices. Aspire assigns them dynamic ports — Gateway
// only needs the logical name, service discovery handles the rest.
var orders = builder.AddProject<Projects.Orders>("orders");
var billing = builder.AddProject<Projects.Billing>("billing");

// The gateway is pinned to a stable port so its issuer URL stays constant
// across runs (the JWT 'iss' claim and Service-side JwtBearerOptions.Authority
// both have to agree, and using service discovery for an OIDC discovery doc
// fetch is more friction than value for a learning sample).
builder.AddProject<Projects.Gateway>("gateway")
    .WithReference(orders)
    .WithReference(billing)
    .WaitFor(orders)
    .WaitFor(billing)
    .WithHttpEndpoint(port: 5001, name: "http")
    .WithExternalHttpEndpoints();

builder.Build().Run();
