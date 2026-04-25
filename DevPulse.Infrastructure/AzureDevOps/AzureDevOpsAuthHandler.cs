using System.Net;
using System.Net.Http.Headers;
using Serilog;

namespace DevPulse.Infrastructure.AzureDevOps;

/// <summary>
/// Adds Basic auth header only for requests targeting the configured ADO host,
/// preventing PAT leakage to non-ADO endpoints. Redirects are disabled on the
/// inner handler so a malicious 3xx cannot route the PAT to an attacker host.
/// </summary>
public sealed class AzureDevOpsAuthHandler : DelegatingHandler
{
    private readonly string _adoHost;
    private readonly AuthenticationHeaderValue _authHeader;

    public AzureDevOpsAuthHandler(string orgUrl, string pat)
        : base(CreateNonRedirectingInnerHandler())
    {
        _adoHost = new Uri(orgUrl.TrimEnd('/')).Host;
        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($":{pat}"));
        _authHeader = new AuthenticationHeaderValue("Basic", encoded);
    }

    /// <summary>
    /// Factory for the inner HttpClientHandler with auto-redirect disabled.
    /// Following 3xx automatically would re-attach the PAT on the redirected
    /// request, leaking it to whatever host the response Location pointed to.
    /// </summary>
    public static HttpClientHandler CreateNonRedirectingInnerHandler()
        => new() { AllowAutoRedirect = false };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri?.Host.Equals(_adoHost, StringComparison.OrdinalIgnoreCase) == true)
            request.Headers.Authorization = _authHeader;

        var response = await base.SendAsync(request, ct).ConfigureAwait(false);

        // Defense-in-depth: even if a future change re-enables auto-redirect on the inner handler,
        // refuse to silently follow 3xx responses. The caller's retry logic can decide what to do.
        var status = (int)response.StatusCode;
        if (status >= 300 && status < 400 && response.StatusCode != HttpStatusCode.NotModified)
        {
            var location = response.Headers.Location?.ToString() ?? "(no Location header)";
            Log.Warning("AzureDevOpsAuthHandler: refusing redirect {Status} from {RequestUri} to {Location}",
                status, request.RequestUri, location);
            response.Dispose();
            throw new HttpRequestException(
                $"Refusing to follow {status} redirect to '{location}'. HTTP redirects are disallowed to prevent PAT leakage.");
        }

        return response;
    }
}
