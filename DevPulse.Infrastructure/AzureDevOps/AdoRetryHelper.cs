using System.Diagnostics;

namespace DevPulse.Infrastructure.AzureDevOps;

internal static class AdoRetryHelper
{
    // Total retry time cap — prevents shutdown hangs if ADO is rate-limiting when DisposeAsync fires.
    private static readonly TimeSpan MaxTotalRetryTime = TimeSpan.FromSeconds(30);

    internal static async Task<HttpResponseMessage> GetWithRetryAsync(HttpClient http, string url, CancellationToken ct)
    {
        var ep = SafeEndpoint(url);
        var delay = TimeSpan.FromSeconds(2);
        var sw = Stopwatch.StartNew();
        HttpResponseMessage? last = null;
        try
        {
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                last?.Dispose();
                last = await http.GetAsync(url, ct);
                if (last.IsSuccessStatusCode)
                {
                    var ok = last;
                    last = null;
                    return ok;
                }

                var code = (int)last.StatusCode;
                if (code == 401) throw new HttpRequestException($"ADO GET unauthorized (401) — check PAT: {ep}", null, last.StatusCode);
                if (code == 403) throw new HttpRequestException($"ADO GET forbidden (403) — missing permission: {ep}", null, last.StatusCode);
                if (code == 429)
                {
                    var retryAfter = last.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(30);
                    if (retryAfter > TimeSpan.FromSeconds(60)) retryAfter = TimeSpan.FromSeconds(60);
                    if (sw.Elapsed + retryAfter > MaxTotalRetryTime)
                        throw new HttpRequestException($"ADO GET rate-limited (429) — wall-clock retry cap exceeded: {ep}", null, last.StatusCode);
                    await Task.Delay(retryAfter, ct);
                    last.Dispose();
                    last = await http.GetAsync(url, ct);
                    if (last.IsSuccessStatusCode)
                    {
                        var ok = last;
                        last = null;
                        return ok;
                    }
                    throw new HttpRequestException($"ADO GET rate-limited (429) after retry: {ep}", null, last.StatusCode);
                }
                if (code < 500 || attempt == 3) break;

                var backoff = delay + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500));
                if (sw.Elapsed + backoff > MaxTotalRetryTime) break;
                await Task.Delay(backoff, ct);
                delay *= 2;
            }
            throw new HttpRequestException($"ADO GET failed [{(int)last!.StatusCode}]: {ep}", null, last.StatusCode);
        }
        finally
        {
            last?.Dispose();
        }
    }

    internal static async Task<HttpResponseMessage> PostWithRetryAsync(HttpClient http, string url, HttpContent content, CancellationToken ct)
    {
        var ep = SafeEndpoint(url);
        var delay = TimeSpan.FromSeconds(2);
        var sw = Stopwatch.StartNew();
        HttpResponseMessage? last = null;
        try
        {
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                last?.Dispose();
                last = await http.PostAsync(url, content, ct);
                if (last.IsSuccessStatusCode)
                {
                    var ok = last;
                    last = null;
                    return ok;
                }

                var code = (int)last.StatusCode;
                if (code == 401) throw new HttpRequestException($"ADO POST unauthorized (401) — check PAT: {ep}", null, last.StatusCode);
                if (code == 403) throw new HttpRequestException($"ADO POST forbidden (403) — missing permission: {ep}", null, last.StatusCode);
                if (code == 429)
                {
                    var retryAfter = last.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(30);
                    if (retryAfter > TimeSpan.FromSeconds(60)) retryAfter = TimeSpan.FromSeconds(60);
                    if (sw.Elapsed + retryAfter > MaxTotalRetryTime)
                        throw new HttpRequestException($"ADO POST rate-limited (429) — wall-clock retry cap exceeded: {ep}", null, last.StatusCode);
                    await Task.Delay(retryAfter, ct);
                    last.Dispose();
                    last = await http.PostAsync(url, content, ct);
                    if (last.IsSuccessStatusCode)
                    {
                        var ok = last;
                        last = null;
                        return ok;
                    }
                    throw new HttpRequestException($"ADO POST rate-limited (429) after retry: {ep}", null, last.StatusCode);
                }
                if (code < 500 || attempt == 3) break;

                var backoff = delay + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500));
                if (sw.Elapsed + backoff > MaxTotalRetryTime) break;
                await Task.Delay(backoff, ct);
                delay *= 2;
            }
            throw new HttpRequestException($"ADO POST failed [{(int)last!.StatusCode}]: {ep}", null, last.StatusCode);
        }
        finally
        {
            last?.Dispose();
        }
    }

    private static string SafeEndpoint(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath + uri.Query : "(invalid url)";
}
