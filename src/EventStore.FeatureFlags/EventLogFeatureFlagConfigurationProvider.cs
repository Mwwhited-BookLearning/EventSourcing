using System.Data.Common;
using Microsoft.Extensions.Configuration;

namespace EventStore.FeatureFlags;

// ADR-077 -- polls the FeatureFlagState table (folded from the reserved
// FeatureFlagSet event, see FeatureFlagService) on a short interval and
// fires a reload token when a value actually changes, so IOptionsMonitor<T>/
// a fresh IConfiguration read/IOptionsSnapshot<T> sees the new value with
// no restart -- the exact GetReloadToken() mechanism the file provider
// already uses for appsettings.json's own reloadOnChange, applied to a
// live, network-backed store instead of local disk (ADR-041's own chained-
// provider pattern, not a new one). Every flag surfaces under the
// "FeatureFlags:{key}" configuration key, one AppId's own silo (ADR-075) --
// scoped by the appId this instance is constructed with, never cross-tenant.
public class EventLogFeatureFlagConfigurationProvider(Func<DbConnection> connectionFactory, string appId, TimeSpan pollInterval)
    : ConfigurationProvider, IDisposable
{
    private readonly PeriodicTimer _timer = new(pollInterval);
    private CancellationTokenSource? _pollCts;

    // ConfigurationProvider.Load() runs synchronously, once, during
    // WebApplicationBuilder.Build() -- before this Host can serve a single
    // request, its flags must already be populated. GetAwaiter().GetResult()
    // is the standard, sanctioned way to satisfy that synchronous contract
    // for an inherently-async data source (the file provider's own
    // FileConfigurationProvider.Load() does the equivalent for its first
    // synchronous read too).
    public override void Load()
    {
        LoadOnceAsync(CancellationToken.None).GetAwaiter().GetResult();

        _pollCts = new CancellationTokenSource();
        _ = PollForeverAsync(_pollCts.Token);
    }

    private async Task PollForeverAsync(CancellationToken ct)
    {
        try
        {
            while (await _timer.WaitForNextTickAsync(ct))
                await LoadOnceAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Disposed -- an ordinary shutdown, not a real failure.
        }
    }

    private async Task LoadOnceAsync(CancellationToken ct)
    {
        var newData = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        await using var connection = connectionFactory();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        // Double-quoted identifiers: valid across SQLite, PostgreSQL, and
        // SQL Server's own default QUOTED_IDENTIFIER ON session setting --
        // no provider-specific SQL dialect needed for a query this simple.
        command.CommandText = """SELECT "Key", "Value" FROM "FeatureFlags" WHERE "AppId" = @appId""";
        var appIdParameter = command.CreateParameter();
        appIdParameter.ParameterName = "@appId";
        appIdParameter.Value = appId;
        command.Parameters.Add(appIdParameter);

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            newData[$"FeatureFlags:{reader.GetString(0)}"] = reader.GetString(1);

        // Compares against the CURRENT set for a change -- there's no
        // "changed since last poll" column in FeatureFlagState, only the
        // folded current state, so a full diff is the correct comparison,
        // not an optimization concern at this table's expected size.
        var changed = !HasSameContent(newData);
        Data = newData;
        if (changed)
            OnReload();
    }

    private bool HasSameContent(Dictionary<string, string?> newData)
    {
        if (Data.Count != newData.Count)
            return false;
        foreach (var (key, value) in newData)
        {
            if (!Data.TryGetValue(key, out var existing) || existing != value)
                return false;
        }
        return true;
    }

    public void Dispose()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _timer.Dispose();
    }
}
