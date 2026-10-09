extern alias DevIdpAssembly;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace EventStore.IntegrationTests;

// One real DevIdp plus one real Host process-under-test (in-process
// TestServer, every real BackgroundService running), wired so the Host
// trusts the DevIdp's signing keys -- the same wiring the per-provider
// *HttpSqliteTests already use, shared here so each provider's e2e class
// only supplies its own factory.
public sealed class ProviderE2EHarness : IDisposable
{
    private readonly IDisposable _hostFactory;
    private readonly WebApplicationFactory<DevIdpAssembly::Program> _devIdpFactory;

    public HttpClient Host { get; }
    public HttpClient DevIdp { get; }

    private ProviderE2EHarness(IDisposable hostFactory, HttpClient host, WebApplicationFactory<DevIdpAssembly::Program> devIdpFactory, HttpClient devIdp)
    {
        _hostFactory = hostFactory;
        Host = host;
        _devIdpFactory = devIdpFactory;
        DevIdp = devIdp;
    }

    public static async Task<ProviderE2EHarness> StartAsync<THost>(string connectionStringName, string connectionString)
        where THost : class
    {
        var devIdpFactory = DevIdpTestFactory.Create();
        var devIdpClient = devIdpFactory.CreateClient();
        var configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            new Uri(devIdpClient.BaseAddress!, ".well-known/openid-configuration").ToString(),
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever(devIdpClient) { RequireHttps = false });
        var devIdpConfiguration = await configManager.GetConfigurationAsync();

        var hostFactory = new WebApplicationFactory<THost>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting($"ConnectionStrings:{connectionStringName}", connectionString);
            builder.ConfigureLogging(l => l.AddProvider(new ErrorSinkProvider())); // surfaces a server-side 500's real exception in the test output
            builder.ConfigureServices(services => services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
            {
                o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(devIdpConfiguration);
                o.RequireHttpsMetadata = false;
            }));
        });
        return new ProviderE2EHarness(hostFactory, hostFactory.CreateClient(), devIdpFactory, devIdpClient);
    }

    public void Dispose()
    {
        Host.Dispose();
        _hostFactory.Dispose();
        DevIdp.Dispose();
        _devIdpFactory.Dispose();
    }
}

internal sealed class ErrorSinkProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new Sink(categoryName);
    public void Dispose() { }

    private sealed class Sink(string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                Console.WriteLine($"[server {logLevel}] {category}: {formatter(state, exception)} {exception}");
        }
    }
}
