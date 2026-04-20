namespace DevPulse.Infrastructure.AzureDevOps;

internal static class AdoRetryHelper
{
    internal static async Task<HttpResponseMessage> GetWithRetryAsync(HttpClient http, string url, CancellationToken ct)
    {
        var ep = SafeEndpoint(url);
        var delay = TimeSpan.FromSeconds(2);
        HttpResponseMessage? last = null;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            last = await http.GetAsync(url, ct);
            if (last.IsSuccessStatusCode) return last;

            var code = (int)last.StatusCode;
            if (code == 401) throw new HttpRequestException($"ADO GET unauthorized (401) — check PAT: {ep}", null, last.StatusCode);
            if (code == 403) throw new HttpRequestException($"ADO GET forbidden (403) — missing permission: {ep}", null, last.StatusCode);
            if (code == 429) throw new HttpRequestException($"ADO GET rate-limited (429): {ep}", null, last.StatusCode);
            if (code < 500 || attempt == 3) break;

            await Task.Delay(delay + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500)), ct);
            delay *= 2;
        }
        throw new HttpRequestException($"ADO GET failed [{(int)last!.StatusCode}]: {ep}", null, last.StatusCode);
    }

    internal static async Task<HttpResponseMessage> PostWithRetryAsync(HttpClient http, string url, HttpContent content, CancellationToken ct)
    {
        var ep = SafeEndpoint(url);
        var delay = TimeSpan.FromSeconds(2);
        HttpResponseMessage? last = null;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            last = await http.PostAsync(url, content, ct);
            if (last.IsSuccessStatusCode) return last;

            var code = (int)last.StatusCode;
            if (code == 401) throw new HttpRequestException($"ADO POST unauthorized (401) — check PAT: {ep}", null, last.StatusCode);
            if (code == 403) throw new HttpRequestException($"ADO POST forbidden (403) — missing permission: {ep}", null, last.StatusCode);
            if (code == 429) throw new HttpRequestException($"ADO POST rate-limited (429): {ep}", null, last.StatusCode);
            if (code < 500 || attempt == 3) break;

            await Task.Delay(delay + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500)), ct);
            delay *= 2;
        }
        throw new HttpRequestException($"ADO POST failed [{(int)last!.StatusCode}]: {ep}", null, last.StatusCode);
    }

    private static string SafeEndpoint(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : "(invalid url)";
}
