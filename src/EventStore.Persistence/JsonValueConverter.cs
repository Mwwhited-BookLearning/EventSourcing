using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EventStore.Persistence;

// A handful of small, structured envelope fields (RequiredClaims, RequiredSignature,
// Signature) have no independent identity and are never queried by sub-field at this
// stage of the build -- so they're stored as portable JSON text via a value converter,
// never a provider's native JSON column type (ADR-004 -- that rule is about the two big
// text blobs, Payload/JsonSchema, but the same "plain text, not a native JSON column"
// spirit applies here too, for the same cross-provider-portability reason).
internal static class JsonValueConverter
{
    public static ValueConverter<T, string> For<T>() where T : class => new(
        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
        v => JsonSerializer.Deserialize<T>(v, (JsonSerializerOptions?)null)!);

    public static ValueConverter<T?, string?> ForNullable<T>() where T : class => new(
        v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
        v => v == null ? null : JsonSerializer.Deserialize<T>(v, (JsonSerializerOptions?)null));

    public static ValueComparer<T> ListComparer<T>() where T : System.Collections.IEnumerable => new(
        (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
        v => v);
}
