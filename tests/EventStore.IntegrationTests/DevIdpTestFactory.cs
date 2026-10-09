extern alias DevIdpAssembly;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EventStore.IntegrationTests;

// The one place a test builds a DevIdp. In-memory signing keys (DevIdp:EphemeralKeys) rather than the
// certificate persisted in the OS user store, which every concurrent test host on the machine shares
// and which intermittently threw CryptographicException "The supplied handle is invalid" while signing
// (docs/bugs/framework/test/devidp-shared-signing-key-flake.md). Always create a DevIdp through this.
internal static class DevIdpTestFactory
{
    public static WebApplicationFactory<DevIdpAssembly::Program> Create() =>
        new WebApplicationFactory<DevIdpAssembly::Program>().WithWebHostBuilder(b => b.UseSetting("DevIdp:EphemeralKeys", "true"));
}
