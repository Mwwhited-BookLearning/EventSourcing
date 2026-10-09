# DevIdp token endpoint intermittently 500s under concurrent test hosts

- **Found:** 2026-10-09, provider e2e suite (`ProviderE2EPostgres`); 5 of 7 tests failed in roughly one run in four, mostly right after a rebuild or with several test processes running.
- **Symptom:** `/connect/token` returned 500: `CryptographicException: The supplied handle is invalid` in `RSACng.TrySignHash`, via OpenIddict's `GenerateIdentityModelToken`.
- **Root cause:** `AddDevelopmentSigningCertificate()` uses an RSA key from a certificate persisted in the OS user certificate store. Every DevIdp instance/process on the machine shares that one persisted key, and its CNG handle can be invalidated under a concurrent user.
- **Reproduction:** three extra `dotnet test` processes of the same class running at once (the single-process, uncontended run passed 12 times in a row).
- **Resolution:** `DevIdp:EphemeralKeys=true` makes the DevIdp use in-memory signing/encryption keys (`Program.cs`). `ProviderE2EHarness` sets it. Default is unchanged, so the AppHost keeps keys that survive a DevIdp restart. After the fix, 8 contended runs passed 7 of 7 each.
- **Not yet done:** the other `WebApplicationFactory<DevIdp>` call sites in the test suite still use the persisted key and have the same latent exposure.
