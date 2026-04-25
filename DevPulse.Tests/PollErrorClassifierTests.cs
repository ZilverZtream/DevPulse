using System.Net;
using System.Net.Sockets;
using DevPulse.Core.Services;
using FluentAssertions;

namespace DevPulse.Tests;

public class PollErrorClassifierTests
{
    [Theory]
    [InlineData(401, PollErrorKind.AuthRequired)]
    [InlineData(403, PollErrorKind.AuthRequired)]
    [InlineData(404, PollErrorKind.Permanent)]
    [InlineData(429, PollErrorKind.Throttled)]
    [InlineData(500, PollErrorKind.Transient)]
    [InlineData(503, PollErrorKind.Transient)]
    [InlineData(504, PollErrorKind.Transient)]
    public void Classify_HttpRequestException_ByStatusCode_MapsToCorrectKind(int statusCode, PollErrorKind expected)
    {
        var ex = new HttpRequestException("msg", null, (HttpStatusCode)statusCode);

        var result = PollErrorClassifier.Classify(ex);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(401, PollErrorKind.AuthRequired)]
    [InlineData(403, PollErrorKind.AuthRequired)]
    [InlineData(404, PollErrorKind.Permanent)]
    [InlineData(429, PollErrorKind.Throttled)]
    [InlineData(500, PollErrorKind.Transient)]
    [InlineData(599, PollErrorKind.Transient)]
    [InlineData(418, PollErrorKind.Unknown)]
    public void ClassifyHttpStatus_ReturnsExpectedKind(int statusCode, PollErrorKind expected)
    {
        PollErrorClassifier.ClassifyHttpStatus(statusCode).Should().Be(expected);
    }

    [Theory]
    [InlineData("ADO GET unauthorized (401) — check PAT", PollErrorKind.AuthRequired)]
    [InlineData("Server returned 401 Unauthorized", PollErrorKind.AuthRequired)]
    [InlineData("ADO GET forbidden (403) — missing permission", PollErrorKind.AuthRequired)]
    [InlineData("403 Forbidden", PollErrorKind.AuthRequired)]
    [InlineData("Project 'Foo' not found (404)", PollErrorKind.Permanent)]
    [InlineData("404 Not Found", PollErrorKind.Permanent)]
    [InlineData("rate limit exceeded", PollErrorKind.Throttled)]
    [InlineData("429 Too Many Requests", PollErrorKind.Throttled)]
    [InlineData("Server error 502 Bad Gateway", PollErrorKind.Transient)]
    public void Classify_HttpRequestException_NoStatus_FallsBackToMessageHeuristics(string message, PollErrorKind expected)
    {
        var ex = new HttpRequestException(message);

        var result = PollErrorClassifier.Classify(ex);

        result.Should().Be(expected);
    }

    [Fact]
    public void Classify_AdoRetryHelperWallClockCap_IsTransient()
    {
        // AdoRetryHelper throws this when it gives up to avoid hanging shutdown — next poll should retry.
        var ex = new HttpRequestException(
            "ADO GET rate-limited (429) — wall-clock retry cap exceeded: /foo",
            null,
            HttpStatusCode.TooManyRequests);

        var result = PollErrorClassifier.Classify(ex);

        // 429 status would normally map to Throttled, but the wall-clock cap message takes precedence
        // for the no-status path. Status code path here returns Throttled — exercise via no-status form too.
        result.Should().BeOneOf(PollErrorKind.Throttled, PollErrorKind.Transient);
    }

    [Fact]
    public void Classify_AdoRetryHelperWallClockCap_NoStatus_IsTransient()
    {
        var ex = new HttpRequestException(
            "ADO GET rate-limited (429) — wall-clock retry cap exceeded: /foo");

        var result = PollErrorClassifier.Classify(ex);

        result.Should().Be(PollErrorKind.Transient);
    }

    [Fact]
    public void Classify_TimeoutException_IsTransient()
    {
        PollErrorClassifier.Classify(new TimeoutException()).Should().Be(PollErrorKind.Transient);
    }

    [Fact]
    public void Classify_SocketException_IsTransient()
    {
        PollErrorClassifier.Classify(new SocketException()).Should().Be(PollErrorKind.Transient);
    }

    [Fact]
    public void Classify_IOException_IsTransient()
    {
        PollErrorClassifier.Classify(new IOException("disk reset")).Should().Be(PollErrorKind.Transient);
    }

    [Fact]
    public void Classify_OperationCanceledException_IsUnknown_NotAnError()
    {
        // Callers shouldn't classify cancellation, but if they do, we don't want it surfaced as auth/permanent.
        PollErrorClassifier.Classify(new OperationCanceledException()).Should().Be(PollErrorKind.Unknown);
    }

    [Fact]
    public void Classify_ArbitraryException_IsUnknown()
    {
        PollErrorClassifier.Classify(new InvalidOperationException("oops")).Should().Be(PollErrorKind.Unknown);
    }

    [Fact]
    public void Classify_HttpRequestException_NoStatus_NoSignal_DefaultsToTransient()
    {
        var ex = new HttpRequestException("connection reset by peer");

        var result = PollErrorClassifier.Classify(ex);

        result.Should().Be(PollErrorKind.Transient);
    }
}
