using System.Net.Http.Headers;

namespace DevPulse.Infrastructure.AzureDevOps;

/// <summary>
/// Adds Basic auth header only for requests targeting the configured ADO host,
/// preventing PAT leakage to non-ADO endpoints.
/// </summary>
public sealed class AzureDevOpsAuthHandler : DelegatingHandler
{
    private readonly string _adoHost;
    private readonly AuthenticationHeaderValue _authHeader;

    public AzureDevOpsAuthHandler(string orgUrl, string pat)
        : base(new HttpClientHandler())
    {
        _adoHost = new Uri(orgUrl.TrimEnd('/')).Host;
        var encoded = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($":{pat}"));
        _authHeader = new AuthenticationHeaderValue("Basic", encoded);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri?.Host.Equals(_adoHost, StringComparison.OrdinalIgnoreCase) == true)
            request.Headers.Authorization = _authHeader;

        return base.SendAsync(request, ct);
    }
}
