using System.Buffers;
using System.Text.Json.Nodes;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Transport.Formatters;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace EventStore.GraphQL;

// docs/03-api-contracts.md: "every GraphQL operation (Query, Mutation,
// Subscription) travels over the HTTP QUERY method (ADR-012), never GET."
// HotChocolate's own MapGraphQL() maps GET/POST/WebSocket, never a custom
// verb -- docs/libraries/dotnet/hotchocolate.md's own note ("needs a small
// custom endpoint mapping rather than MapGraphQL()'s default") is why this
// exists: a manual IRequestExecutor invocation, formatted via
// HotChocolate.Transport.Formatters.JsonResultFormatter (the same wire
// format MapGraphQL's own pipeline produces, verified directly against the
// installed v16 assemblies -- AcceptMediaType, the type MapGraphQL's own
// IHttpResponseFormatter needs, turned out to have an internal-only
// constructor, unusable from outside HotChocolate's own assembly).
public static class GraphQlEndpoints
{
    public static WebApplication MapGraphQlEndpoints(this WebApplication app)
    {
        app.MapMethods("/graphql", ["QUERY"], async (HttpContext context, IRequestExecutorProvider executorProvider) =>
        {
            var executor = await executorProvider.GetExecutorAsync(schemaName: null!, context.RequestAborted);

            using var reader = new StreamReader(context.Request.Body);
            var bodyText = await reader.ReadToEndAsync(context.RequestAborted);
            var bodyNode = string.IsNullOrWhiteSpace(bodyText) ? null : JsonNode.Parse(bodyText) as JsonObject;

            var requestBuilder = new OperationRequestBuilder()
                .SetDocument(bodyNode?["query"]?.GetValue<string>() ?? "")
                .SetServices(context.RequestServices);
            if (bodyNode?["operationName"]?.GetValue<string>() is { } operationName)
                requestBuilder.SetOperationName(operationName);
            if (bodyNode?["variables"] is JsonObject variables)
                requestBuilder.SetVariableValues(variables.ToJsonString());

            var operationRequest = requestBuilder.Build();
            var result = await executor.ExecuteAsync(operationRequest, context.RequestAborted);
            var formatter = new JsonResultFormatter(indented: false);

            if (result is IResponseStream responseStream)
            {
                // ADR-037: "this design adopts the GraphQL over Server-Sent
                // Events Protocol ('distinct connections mode')" -- the
                // identical transport Follow's own pre-GraphQL SSE endpoint
                // already uses (FollowEndpoints), just carrying a GraphQL
                // Subscription document instead of an OData $filter string.
                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.Headers.ContentType = "text/event-stream";
                context.Response.Headers.CacheControl = "no-cache";
                await context.Response.Body.FlushAsync(context.RequestAborted);

                await foreach (var message in responseStream.ReadResultsAsync().WithCancellation(context.RequestAborted))
                {
                    if (message is not OperationResult operationResult)
                        continue;
                    var buffer = new ArrayBufferWriter<byte>();
                    formatter.Format(operationResult, buffer);
                    await context.Response.WriteAsync("data: ", context.RequestAborted);
                    await context.Response.Body.WriteAsync(buffer.WrittenMemory, context.RequestAborted);
                    await context.Response.WriteAsync("\n\n", context.RequestAborted);
                    await context.Response.Body.FlushAsync(context.RequestAborted);
                }
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.ContentType = "application/graphql-response+json";
                await formatter.FormatAsync(result, context.Response.BodyWriter, default, context.RequestAborted);
            }
        }); // deliberately NOT .RequireAuthorization() here -- "GET /openapi.json, GraphQL schema introspection | none (anonymous)" (03-api-contracts.md's own scope table) shares this one endpoint with every real, scope-gated operation; each resolver checks its own scope/claim individually (GraphQlAuth), the same "per-field/per-operation... not a second auth stack" posture that doc explicitly describes

        return app;
    }
}
