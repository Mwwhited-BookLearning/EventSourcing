namespace EventStore.IntegrationTests;

// Test-cleanup helper: a host's background workers can still hold the SQLite
// file for a moment after the factory is disposed, so a bare File.Delete
// intermittently throws IOException in ClassCleanup. Retry briefly, then give
// up quietly -- a leftover temp file must never fail a test run.
internal static class TempDbFile
{
    public static void Delete(string path)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
                return;
            }
            catch (IOException) { Thread.Sleep(100); }
            catch (UnauthorizedAccessException) { Thread.Sleep(100); }
        }
    }
}
