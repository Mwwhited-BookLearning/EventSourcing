using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ServiceDiscovery;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using EventStore.Domain.Observability;

namespace Microsoft.Extensions.Hosting;

// Adds common Aspire services: service discovery, health checks, and OpenTelemetry.
// This project should be referenced by each service project in your solution.
// To learn more about using this project, see https://aka.ms/aspire/service-defaults
//
// This started from Aspire's own scaffolded template, which also turns on
// AddStandardResilienceHandler() (Microsoft.Extensions.Http.Resilience, built
// on Polly v8) for every HttpClient by default -- deliberately removed here,
// not just left as-is. Its blanket 10s-per-attempt/30s-total timeout faulted
// EventStore.DevIdp's RbacProjectionWorker (and every other Follow-API
// consumer) constantly: FollowClient.TailAsync opens one long-lived SSE-style
// HTTP request and reads it for as long as there's anything to tail, which is
// meant to run far longer than 10 seconds -- Polly had no way to know that
// was intentional, so it kept cancelling and retrying a perfectly healthy
// connection. See docs/bugs/framework/service/follow-client-faults-under-
// default-http-resilience-timeout.md for the full diagnosis. Rather than
// hand-tune Polly's options per named client, this default was removed
// outright: every real retry-worthy path in this design already owns its own
// purpose-built, correctly-tuned resilience mechanism (ADR-033's outbox/
// inbox, ADR-060's webhook dispatcher, FollowClient/EventTailReader's own
// reconnect loops, EF Core's EnableRetryOnFailure) -- a generic HTTP-level
// wrapper was never doing real, non-redundant work here. See
// docs/references.md's own "considered and rejected" entry for the full
// reasoning, including Polly's new Open Source Maintenance Fee.
public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Turn on service discovery by default
            http.AddServiceDiscovery();
        });

        // Uncomment the following to restrict the allowed schemes for service discovery.
        // builder.Services.Configure<ServiceDiscoveryOptions>(options =>
        // {
        //     options.AllowedSchemes = ["https"];
        // });

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    // Direct request -- process-level metrics (CPU%, working
                    // set, thread/handle count), the one standard .NET
                    // instrumentation category AspNetCore/Http/Runtime above
                    // don't cover. See this project's own PackageReference
                    // comment for why it's a prerelease package.
                    .AddProcessInstrumentation()
                    // ADR-088 -- Router fold lag, peer-sync outbox depth/
                    // age, webhook delivery lag, hash-chain verification
                    // outcomes, plus this pass's own publish/derivation/
                    // archival/GraphQL/simulator additions.
                    // DuplexInstrumentation.Meter is the one shared instance
                    // every mechanism's own instrument is created against.
                    .AddMeter(DuplexInstrumentation.Name);
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName)
                    // ADR-088 -- the fold step, each outbox pump, and the
                    // hash-chain verifier each wrap their own work in a
                    // named Activity from this one shared ActivitySource.
                    .AddSource(DuplexInstrumentation.Name)
                    .AddAspNetCoreInstrumentation(tracing =>
                        // Exclude health check requests from tracing
                        tracing.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                            && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath)
                    )
                    // Uncomment the following line to enable gRPC instrumentation (requires the OpenTelemetry.Instrumentation.GrpcNetClient package)
                    //.AddGrpcClientInstrumentation()
                    .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        // Uncomment the following lines to enable the Azure Monitor exporter (requires the Azure.Monitor.OpenTelemetry.AspNetCore package)
        //if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        //{
        //    builder.Services.AddOpenTelemetry()
        //       .UseAzureMonitor();
        //}

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            // Add a default liveness check to ensure app is responsive
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // ADR-084's own addendum -- exposed unconditionally, in every
        // environment, not just Development: an orchestrator's readiness/
        // liveness probes need to reach these endpoints in production too,
        // and the default MapHealthChecks response (no custom
        // ResponseWriter configured here) is a bare status string, never
        // exception details/connection strings/per-check descriptions --
        // safe to expose publicly, the same posture most real production
        // Kubernetes deployments already take for their own probes.

        // All health checks must pass for app to be considered ready to accept traffic after starting
        app.MapHealthChecks(HealthEndpointPath);

        // Only health checks tagged with the "live" tag must pass for app to be considered alive
        app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live")
        });

        return app;
    }
}
