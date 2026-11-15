namespace E2EHarness;

using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Trace;

public sealed class ServiceDefaultsTelemetryTests
{
    [Fact]
    public async Task ConfigureOpenTelemetry_Request_ExportsNativeServerSpanWithSemanticAttributes()
    {
        var exported = new TaskCompletionSource<Activity>(TaskCreationOptions.RunContinuationsAsynchronously);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] = "";
        builder.ConfigureOpenTelemetry();
        builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing.AddProcessor(
            new SimpleActivityExportProcessor(new ServerActivityExporter(exported))));

        await using var app = builder.Build();
        app.MapGet("/telemetry/{id}", (string id) => id);
        await app.StartAsync(TestContext.Current.CancellationToken);

        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/telemetry/42", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var activity = await exported.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        activity.GetTagItem("http.request.method").Should().Be("GET");
        activity.GetTagItem("http.response.status_code").Should().Be(200);
        activity.GetTagItem("http.route").Should().Be("/telemetry/{id}");

        await app.StopAsync(TestContext.Current.CancellationToken);
    }

    private sealed class ServerActivityExporter(TaskCompletionSource<Activity> completion) : BaseExporter<Activity>
    {
        public override ExportResult Export(in Batch<Activity> batch)
        {
            foreach (var activity in batch)
                if (activity.Source.Name == "Microsoft.AspNetCore" &&
                    activity.Kind == ActivityKind.Server &&
                    activity.GetTagItem("url.path") is "/telemetry/42")
                    completion.TrySetResult(activity);

            return ExportResult.Success;
        }
    }
}