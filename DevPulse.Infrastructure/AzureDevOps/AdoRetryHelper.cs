using System.Diagnostics;

namespace DevPulse.Infrastructure.AzureDevOps;

internal static class AdoRetryHelper
{
    // Total retry time cap — prevents shutdown hangs if ADO is rate-limiting when DisposeAsync fires.
    private static readonly TimeSpan MaxTotalRetryTime = TimeSpan.FromSeconds(30);

    internal static Task<HttpResponseMessage> GetWithRetryAsync(HttpClient http, string url, CancellationToken ct)
        => SendWithRetryAsync("GET", http, url, content: null, ct);

    internal static Task<HttpResponseMessage> PostWithRetryAsync(HttpClient http, string url, HttpContent content, CancellationToken ct)
        => SendWithRetryAsync("POST", http, url, content, ct);

    private static async Task<HttpResponseMessage> SendWithRetryAsync(
        string verb, HttpClient http, string url, HttpContent? content, CancellationToken ct)
    {
        var ep = SafeEndpoint(url);
        var delay = TimeSpan.FromSeconds(2);
        var sw = Stopwatch.StartNew();
        HttpResponseMessage? last = null;
        // attempt counter applies to 5xx retries only; 429 retries loop independently within the
        // wall-clock cap because Retry-After is server-directed and shouldn't count against it.
        int attempt = 0;
        try
        {
            while (true)
            {
                attempt++;
                last?.Dispose();
                last = await SendOnceAsync(verb, http, url, content, ct).ConfigureAwait(false);
                if (last.IsSuccessStatusCode)
                {
                    var ok = last;
                    last = null;
                    return ok;
                }

                var code = (int)last.StatusCode;
                if (code == 401) throw new HttpRequestException($"ADO {verb} unauthorized (401) — check PAT: {ep}", null, last.StatusCode);
                if (code == 403) throw new HttpRequestException($"ADO {verb} forbidden (403) — missing permission: {ep}", null, last.StatusCode);

                if (code == 429)
                {
                    var retryAfter = last.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(30);
                    if (retryAfter > TimeSpan.FromSeconds(60)) retryAfter = TimeSpan.FromSeconds(60);
                    if (retryAfter < TimeSpan.Zero) retryAfter = TimeSpan.Zero;
                    var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500));
                    var wait = retryAfter + jitter;
                    if (sw.Elapsed + wait > MaxTotalRetryTime)
                        throw new HttpRequestException($"ADO {verb} rate-limited (429) — wall-clock retry cap exceeded: {ep}", null, last.StatusCode);
                    await Task.Delay(wait, ct).ConfigureAwait(false);
                    // Continue looping on 429 — server told us to wait, we wait, we retry until the
                    // wall-clock cap or until the response is no longer 429.
                    continue;
                }

                if (code < 500 || attempt >= 3) break;

                var backoff = delay + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500));
                if (sw.Elapsed + backoff > MaxTotalRetryTime) break;
                await Task.Delay(backoff, ct).ConfigureAwait(false);
                delay *= 2;
            }
            throw new HttpRequestException($"ADO {verb} failed [{(int)last!.StatusCode}]: {ep}", null, last.StatusCode);
        }
        finally
        {
            last?.Dispose();
        }
    }

    private static Task<HttpResponseMessage> SendOnceAsync(string verb, HttpClient http, string url, HttpContent? content, CancellationToken ct)
    {
        if (verb == "GET") return http.GetAsync(url, ct);
        if (verb == "POST") return http.PostAsync(url, content, ct);
        throw new ArgumentException($"Unsupported verb: {verb}", nameof(verb));
    }

    private static string SafeEndpoint(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath + uri.Query : "(invalid url)";
}
