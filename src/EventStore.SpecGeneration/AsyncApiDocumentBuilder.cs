using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EventStore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.OpenApi;

namespace EventStore.SpecGeneration;

// docs/06-solution-structure.md, "Spec generation" section: no mature .NET
// library for AsyncAPI 3.0, so the envelope is hand-built as a
// System.Text.Json.Nodes.JsonObject tree, embedding each event type's
// MaskingSchemaTransformer-wrapped OpenApiSchema (serialized via the same
// Microsoft.OpenApi writer OpenApiDocumentBuilder uses) into
// components.schemas. Round-tripped against the published AsyncAPI 3.0 JSON
// Schema by EventStore.IntegrationTests, not by this class itself.
public class AsyncApiDocumentBuilder(EventStoreContext db, EventSchemaConverter converter, MaskingSchemaTransformer maskingTransformer, IMemoryCache cache)
{
    public const string CacheKey = "asyncapi-document";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    public async Task<string> GetOrBuildJsonAsync(CancellationToken ct = default)
    {
        if (cache.TryGetValue(CacheKey, out string? cached))
            return cached!;

        var json = await BuildJsonAsync(ct);
        cache.Set(CacheKey, json, CacheDuration);
        return json;
    }

    private async Task<string> BuildJsonAsync(CancellationToken ct)
    {
        var activeTypes = await db.EventTypeDefinitions
            .AsNoTracking()
            .Where(e => e.IsActive)
            .ToListAsync(ct);

        var channels = new JsonObject();
        var operations = new JsonObject();
        var messages = new JsonObject();
        var schemas = new JsonObject();

        foreach (var type in activeTypes)
        {
            var payloadSchema = converter.Parse(type.JsonSchema);
            var maskedSchema = maskingTransformer.Wrap(payloadSchema);
            schemas[type.Name] = SerializeSchema(maskedSchema);

            messages[$"{type.Name}Event"] = new JsonObject
            {
                ["payload"] = new JsonObject { ["$ref"] = $"#/components/schemas/{type.Name}" },
            };

            channels[type.Name] = new JsonObject
            {
                ["address"] = $"/follow/{type.Name}",
                ["messages"] = new JsonObject
                {
                    [$"{type.Name}Event"] = new JsonObject { ["$ref"] = $"#/components/messages/{type.Name}Event" },
                },
            };

            operations[$"follow{type.Name}"] = new JsonObject
            {
                ["action"] = "receive",
                ["channel"] = new JsonObject { ["$ref"] = $"#/channels/{type.Name}" },
            };
        }

        var document = new JsonObject
        {
            ["asyncapi"] = "3.0.0",
            ["info"] = new JsonObject { ["title"] = "EventStore Follow API", ["version"] = "1.0.0" },
            ["channels"] = channels,
            ["operations"] = operations,
            ["components"] = new JsonObject
            {
                ["messages"] = messages,
                ["schemas"] = schemas,
            },
        };

        return document.ToJsonString();
    }

    private static JsonNode SerializeSchema(OpenApiSchema schema)
    {
        using var stream = new MemoryStream();
        using (var streamWriter = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            var writer = new OpenApiJsonWriter(streamWriter);
            schema.SerializeAsV31(writer);
        }
        stream.Position = 0;
        return JsonNode.Parse(stream)!;
    }
}
