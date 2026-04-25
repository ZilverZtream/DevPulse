using System.Net;
using System.Net.Http.Headers;
using DevPulse.Infrastructure.AzureDevOps;
using FluentAssertions;

namespace DevPulse.Tests;

public class AdoRetryHelperTests
{
    [Fact]
    public async Task GetWithRetryAsync_RetriesThrough429Sequence_ReturnsSuccess()
    {
        // 3 × 429, then 200. Helper must retry past the first 429 (old behavior bailed after one).
        var responses = new Queue<HttpResponseMessage>();
        for (int i = 0; i < 3; i++)
        {
            var r429 = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            r429.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMilliseconds(20));
            responses.Enqueue(r429);
        }
        responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") });

        var handler = new QueuedHandler(responses);
        using var http = new HttpClient(handler);

        using var resp = await AdoRetryHelper.GetWithRetryAsync(http, "https://example.com/api", CancellationToken.None);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        handler.RequestCount.Should().Be(4);
    }

    [Fact]
    public async Task GetWithRetryAsync_PersistentRateLimit_ThrowsWithCapMessage()
    {
        // Constant 429 with a long Retry-After: helper should give up cleanly when the wall-clock
        // cap (30s) is exceeded by the next requested wait.
        var handler = new AlwaysHandler(() =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(45));
            return r;
        });
        using var http = new HttpClient(handler);

        var act = async () => await AdoRetryHelper.GetWithRetryAsync(http, "https://example.com/api", CancellationToken.None);

        var ex = await act.Should().ThrowAsync<HttpRequestException>();
        ex.Which.Message.Should().Contain("wall-clock retry cap exceeded");
        // Should not have looped indefinitely — at most 2 attempts (first + cap check failure).
        handler.RequestCount.Should().BeLessThanOrEqualTo(2);
    }

    [Fact]
    public async Task GetWithRetryAsync_Returns401Immediately_NoRetry()
    {
        var handler = new AlwaysHandler(() => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var http = new HttpClient(handler);

        var act = async () => await AdoRetryHelper.GetWithRetryAsync(http, "https://example.com/api", CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
        handler.RequestCount.Should().Be(1);
    }

    [Fact]
    public async Task GetWithRetryAsync_5xxStillRetried_AndDoesNotInterfereWith429Path()
    {
        // First a 503 (retryable 5xx), then 200.
        var responses = new Queue<HttpResponseMessage>();
        responses.Enqueue(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") });

        var handler = new QueuedHandler(responses);
        using var http = new HttpClient(handler);

        using var resp = await AdoRetryHelper.GetWithRetryAsync(http, "https://example.com/api", CancellationToken.None);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        handler.RequestCount.Should().Be(2);
    }

    private sealed class QueuedHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _queue;
        public int RequestCount { get; private set; }

        public QueuedHandler(Queue<HttpResponseMessage> queue) => _queue = queue;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            if (_queue.Count == 0)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            return Task.FromResult(_queue.Dequeue());
        }
    }

    private sealed class AlwaysHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _factory;
        public int RequestCount { get; private set; }

        public AlwaysHandler(Func<HttpResponseMessage> factory) => _factory = factory;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(_factory());
        }
    }
}
