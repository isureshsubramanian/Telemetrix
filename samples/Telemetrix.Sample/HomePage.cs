namespace Telemetrix.Sample;

/// <summary>The sample application's landing page.</summary>
public static class HomePage
{
    /// <summary>Static HTML for <c>GET /</c>.</summary>
    public const string Html = """
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>Telemetrix Sample</title>
  <style>
    :root { color-scheme: dark; }
    body { margin: 0; font-family: -apple-system, "Segoe UI", Roboto, sans-serif;
           background: #0b0d12; color: #e7e9ef; line-height: 1.55; }
    .wrap { max-width: 760px; margin: 0 auto; padding: 56px 24px; }
    h1 { font-size: 28px; margin: 0 0 6px; letter-spacing: -0.02em; }
    .lead { color: #98a1b2; margin: 0 0 28px; }
    .cta { display: inline-block; background: #6366f1; color: #fff; font-weight: 600;
           padding: 12px 22px; border-radius: 10px; text-decoration: none; }
    .cta:hover { background: #5457e0; }
    .grid { margin-top: 34px; display: grid; gap: 10px; }
    .ep { background: #14171f; border: 1px solid #262b37; border-radius: 10px;
          padding: 12px 16px; display: flex; gap: 14px; align-items: baseline; }
    .ep code { font-family: ui-monospace, Menlo, Consolas, monospace; color: #818cf8; font-size: 13px; }
    .ep span { color: #98a1b2; font-size: 13px; }
    .note { margin-top: 28px; color: #626b7d; font-size: 13px; }
    a.inline { color: #818cf8; }
  </style>
</head>
<body>
  <div class="wrap">
    <h1>Telemetrix Sample</h1>
    <p class="lead">A small ASP.NET Core + EF Core app wired with Telemetrix. Synthetic traffic is
       already running, so the dashboard has data to show.</p>
    <a class="cta" href="/telemetrix">Open the Telemetrix dashboard &rarr;</a>
    <div class="grid">
      <div class="ep"><code>GET /api/products</code><span>Catalog search &mdash; EF Core SQL with parameters</span></div>
      <div class="ep"><code>GET /api/products/4</code><span>Single product lookup</span></div>
      <div class="ep"><code>GET /api/slow</code><span>Nested custom spans &mdash; a deep waterfall</span></div>
      <div class="ep"><code>GET /api/chain</code><span>HttpClient fan-out &mdash; cross-request hierarchy</span></div>
      <div class="ep"><code>GET /api/boom</code><span>Returns 500 &mdash; a failed, red trace</span></div>
      <div class="ep"><code>POST /api/orders</code><span>Places an order &mdash; multiple SQL round-trips</span></div>
    </div>
    <p class="note">Telemetrix only activates in the Development environment. See the
       <a class="inline" href="/telemetrix">dashboard</a> for live traces, logs and metrics.</p>
  </div>
</body>
</html>
""";
}
