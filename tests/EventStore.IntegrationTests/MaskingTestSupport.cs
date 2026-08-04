using EventStore.Masking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EventStore.IntegrationTests;

// Shared test wiring for "Property-Level Masking" (docs/08-build-plan.md) --
// builds a real IPayloadMasker/IMaskingStrategy/IRedactorProvider graph via
// EventStore.Masking's own AddMasking, the same composition root every real
// Host uses, rather than hand-constructing PayloadMasker directly (it needs
// its own keyed-service resolution against the exact provider that registered
// the strategies).
internal static class MaskingTestSupport
{
    // A valid, dev-only HMAC key (Base64, >= 32 bytes) -- HmacRedactorOptions'
    // own validation requirement. Not the same key any real Host's
    // appsettings.Development.json uses; test-only.
    public const string TestHmacKeyId = "test";
    private const string TestHmacKey = "kM3F9v2yYbL9m3S+eYQ0/aH4T+79q0kLDfQ4v6zjOgE=";

    public static (IPayloadMasker PayloadMasker, CapturingLoggerProvider Logs) CreatePayloadMasker()
    {
        var logs = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Debug).AddProvider(logs)); // PayloadMasker's own redaction log call is Debug-level
        services.AddMasking(new Dictionary<string, string> { [TestHmacKeyId] = TestHmacKey });
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IPayloadMasker>(), logs);
    }
}

// Captures every formatted log message across every category -- used to
// verify PayloadMasker's own log-redaction path (ADR-050) never lets a
// classified field's real value reach a log sink unredacted.
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly List<string> _messages = [];
    public IReadOnlyList<string> Messages => _messages;

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(_messages);

    public void Dispose() { }

    private sealed class CapturingLogger(List<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (messages)
                messages.Add(formatter(state, exception));
        }
    }
}
