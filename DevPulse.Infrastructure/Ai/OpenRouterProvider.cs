using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DevPulse.Core.Enums;
using DevPulse.Core.Interfaces;
using DevPulse.Core.Models;

namespace DevPulse.Infrastructure.Ai;

public sealed class OpenRouterProvider : IAiProvider
{
    private readonly HttpClient _http;
    private readonly Func<string?> _apiKeyProvider;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public OpenRouterProvider(HttpClient http, Func<string?> apiKeyProvider)
    {
        _http = http;
        _apiKeyProvider = apiKeyProvider;
    }

    public string Id => "openrouter";
    public string DisplayName => "OpenRouter";
    public AiProviderKind Kind => AiProviderKind.Http;
    public AiDataPolicy DataPolicy => AiDataPolicy.Cloud;

    public Task<AiHealthResult> HealthCheckAsync(CancellationToken ct = default)
    {
        var key = _apiKeyProvider();
        if (string.IsNullOrWhiteSpace(key))
            return Task.FromResult(new AiHealthResult(false, "OpenRouter API key not configured"));
        return Task.FromResult(new AiHealthResult(true, null));
    }

    public async Task<AiGenerateResult> GenerateAsync(AiGenerateRequest req, CancellationToken ct = default)
    {
        var key = _apiKeyProvider();
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("OpenRouter API key not configured");

        var sw = Stopwatch.StartNew();
        var payload = new
        {
            model = req.Model,
            messages = new[] { new { role = "user", content = req.Prompt } }
        };
        var body = JsonSerializer.Serialize(payload);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions")
        { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(req.Timeout);

        using var resp = await _http.SendAsync(request, linkedCts.Token).ConfigureAwait(false);
        var respBody = await resp.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"OpenRouter HTTP {(int)resp.StatusCode}: {respBody}", null, resp.StatusCode);

        var parsed = JsonSerializer.Deserialize<OrResponse>(respBody, JsonOpts)
            ?? throw new HttpRequestException("OpenRouter: unexpected null response body");
        var text = parsed.Choices?.FirstOrDefault()?.Message?.Content
            ?? throw new HttpRequestException("OpenRouter: no content in response");

        return new AiGenerateResult(
            Markdown: text,
            ModelUsed: parsed.Model ?? req.Model,
            TokensIn: parsed.Usage?.Prompt_tokens ?? 0,
            TokensOut: parsed.Usage?.Completion_tokens ?? 0,
            Duration: sw.Elapsed,
            ErrorMessage: null);
    }

    private sealed class OrResponse
    {
        public string? Model { get; set; }
        public List<OrChoice>? Choices { get; set; }
        public OrUsage? Usage { get; set; }
    }
    private sealed class OrChoice { public OrMessage? Message { get; set; } }
    private sealed class OrMessage { public string? Content { get; set; } }
    private sealed class OrUsage { public int Prompt_tokens { get; set; } public int Completion_tokens { get; set; } }
}
