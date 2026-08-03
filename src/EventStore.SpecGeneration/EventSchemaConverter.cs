using System.Text.Json;
using Microsoft.OpenApi;

namespace EventStore.SpecGeneration;

// Parses a registered EventTypeDefinition.JsonSchema (a bare JSON Schema
// document) into the shared Microsoft.OpenApi OpenApiSchema object model,
// per docs/06-solution-structure.md's "Spec generation" section. Unlike
// EventStore.SchemaRegistry/EventStore.Inbox's hand-written JSON Schema
// checks (which exist specifically because JsonSchema.Net rejects the
// undeclared "x-masking" vendor keyword), Microsoft.OpenApi's own
// OpenApiSchemaJsonConverter is designed to carry unrecognized keywords
// through via OpenApiSchema.Extensions rather than reject them -- no
// compatibility problem here.
public class EventSchemaConverter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new OpenApiSchemaJsonConverter(OpenApiSpecVersion.OpenApi3_1) },
    };

    public OpenApiSchema Parse(string jsonSchemaText) =>
        JsonSerializer.Deserialize<OpenApiSchema>(jsonSchemaText, Options)!;
}
