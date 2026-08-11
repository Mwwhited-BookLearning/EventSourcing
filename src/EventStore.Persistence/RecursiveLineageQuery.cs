using System.Data;
using Microsoft.EntityFrameworkCore;

namespace EventStore.Persistence;

// Shared ADO.NET plumbing for IEventLineageQueryProvider's three implementations
// -- only the recursive-CTE SQL text itself is provider-specific.
internal static class RecursiveLineageQuery
{
    public static async Task<IReadOnlyList<Guid>> ExecuteAsync(EventStoreContext db, string sql, Guid rootId, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@rootId";
        parameter.Value = rootId;
        command.Parameters.Add(parameter);

        var results = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add(reader.GetGuid(0));

        return results;
    }
}
