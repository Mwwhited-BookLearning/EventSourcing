namespace EventStore.SpecGeneration;

// ADR-025 -- AsyncAPI has no .NET-native renderer to lean on; this is the
// single static HTML page that renders it instead, using @asyncapi/react-
// component's own documented standalone/UMD browser bundle (verified
// against that project's own docs/usage/standalone-bundle.md before
// writing this, not recalled from memory), loaded from a CDN, with no
// build step of any kind. schema.url is a relative path -- this page and
// /asyncapi.json are always served by the SAME Host process, so a
// relative URL is both correct and avoids a CORS round trip a same-origin
// request never needed.
public static class AsyncApiUiPage
{
    public const string Html = """
        <!DOCTYPE html>
        <html>
          <head>
            <meta charset="utf-8">
            <title>AsyncAPI</title>
            <link rel="stylesheet" href="https://unpkg.com/@asyncapi/react-component@latest/styles/default.min.css">
          </head>
          <body>
            <div id="asyncapi"></div>
            <script src="https://unpkg.com/@asyncapi/react-component@latest/browser/standalone/index.js"></script>
            <script>
              AsyncApiStandalone.render({
                schema: {
                  url: '/asyncapi.json',
                  options: { method: 'GET', mode: 'same-origin' },
                },
                config: {
                  show: { sidebar: true },
                },
              }, document.getElementById('asyncapi'));
            </script>
          </body>
        </html>
        """;
}
