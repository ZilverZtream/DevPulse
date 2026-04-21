using System.Net;
using System.Text;
using System.Text.Json;
using DevPulse.Core.Enums;
using DevPulse.Core.Models;
using DevPulse.Infrastructure.Ai;
using FluentAssertions;

namespace DevPulse.Tests;

public class OpenRouterProviderTests
{
    private static HttpClient MakeClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new StubHandler(responder);
        return new HttpClient(handler);
    }

    private static string SuccessBody(string content) =>
        JsonSerializer.Serialize(new
        {
            model = "anthropic/claude-3.5-sonnet",
            choices = new[] { new { message = new { content } } },
            usage = new { prompt_tokens = 100, completion_tokens = 250 }
        });

    [Fact]
    public void Kind_IsHttp_PolicyIsCloud()
    {
        var sut = new OpenRouterProvider(MakeClient(_ => new HttpResponseMessage()), () => "key");
        sut.Kind.Should().Be(AiProviderKind.Http);
        sut.DataPolicy.Should().Be(AiDataPolicy.Cloud);
        sut.Id.Should().Be("openrouter");
    }

    [Fact]
    public async Task GenerateAsync_SuccessReturnsMarkdown()
    {
        var http = MakeClient(req =>
        {
            req.RequestUri!.ToString().Should().Contain("openrouter.ai/api/v1/chat/completions");
            req.Headers.Authorization!.Parameter.Should().Be("my-key");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SuccessBody("## Result\nbody"), Encoding.UTF8, "application/json")
            };
        });
        var sut = new OpenRouterProvider(http, () => "my-key");

        var result = await sut.GenerateAsync(new AiGenerateRequest("p", "model", TimeSpan.FromSeconds(30)));

        result.Markdown.Should().Contain("## Result");
        result.TokensIn.Should().Be(100);
        result.TokensOut.Should().Be(250);
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task GenerateAsync_401SurfacesAuthError()
    {
        var http = MakeClient(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        { Content = new StringContent("{\"error\":\"bad key\"}") });
        var sut = new OpenRouterProvider(http, () => "bad");

        var act = () => sut.GenerateAsync(new AiGenerateRequest("p", "m", TimeSpan.FromSeconds(5)));

        (await act.Should().ThrowAsync<HttpRequestException>())
            .Which.Message.Should().Contain("401");
    }

    [Fact]
    public async Task HealthCheckAsync_NoKeyReturnsNotOk()
    {
        var http = MakeClient(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var sut = new OpenRouterProvider(http, () => "");

        var h = await sut.HealthCheckAsync();

        h.Ok.Should().BeFalse();
        h.ErrorMessage.Should().Contain("API key");
    }

    [Fact]
    public async Task GenerateAsync_NoKeyThrows()
    {
        var http = MakeClient(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var sut = new OpenRouterProvider(http, () => "");

        var act = () => sut.GenerateAsync(new AiGenerateRequest("p", "m", TimeSpan.FromSeconds(1)));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _r;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> r) => _r = r;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(_r(request));
    }
}
