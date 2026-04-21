using DevPulse.App.Services;
using DevPulse.Core.Enums;
using DevPulse.Core.Interfaces;
using DevPulse.Core.Models;
using FluentAssertions;

namespace DevPulse.Tests;

public class AiPipelineServiceTests
{
    private sealed class FakeProvider : IAiProvider
    {
        public string Id => "fake";
        public string DisplayName => "Fake";
        public AiProviderKind Kind => AiProviderKind.Http;
        public AiDataPolicy DataPolicy => AiDataPolicy.Local;
        public Func<AiGenerateRequest, Task<AiGenerateResult>> OnGenerate = _ =>
            Task.FromResult(new AiGenerateResult(
                "## Context summary\n x\n## Functional requirements\n y\n## Acceptance criteria\n z\n## Edge cases\n a\n## Test plan\n b\n## Risks and dependencies\n c",
                "fake-model", 10, 20, TimeSpan.FromMilliseconds(100), null));
        public Task<AiHealthResult> HealthCheckAsync(CancellationToken ct = default) =>
            Task.FromResult(new AiHealthResult(true, null));
        public Task<AiGenerateResult> GenerateAsync(AiGenerateRequest r, CancellationToken ct = default) => OnGenerate(r);
    }

    private sealed class FakeWriter : IAiSpecWriter
    {
        public List<string> SpecCalls = [];
        public List<string> PromptCalls = [];
        public Task<AiFilePaths> WriteAsync(string root, string slug, int id, DateTimeOffset ts,
            string spec, string prompt, IReadOnlyList<AiAttempt> history, CancellationToken ct = default)
        {
            SpecCalls.Add(spec);
            PromptCalls.Add(prompt);
            return Task.FromResult(new AiFilePaths("spec.md", "prompt.md", "meta.json"));
        }
    }

    private sealed class FakeAttemptStore : IAiAttemptStore
    {
        public List<AiAttempt> Recorded = [];
        public Task RecordAttemptAsync(AiAttempt a, CancellationToken ct = default) { Recorded.Add(a); return Task.CompletedTask; }
        public Task<IReadOnlyList<AiAttempt>> GetAttemptsForWorkItemAsync(int id, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AiAttempt>>(Recorded.Where(a => a.WorkItemId == id).ToList());
    }

    private sealed class FakeTemplateStore : IAiTemplateStore
    {
        public AiTemplate Template { get; set; } = new()
        {
            Id = "t1", Name = "T",
            AppliesTo = ["Bug"],
            RequiredHeaders = ["Context summary", "Functional requirements", "Acceptance criteria",
                               "Edge cases", "Test plan", "Risks and dependencies"],
            PromptBody = "Draft for {title}"
        };
        public Task<List<AiTemplate>> GetTemplatesAsync(CancellationToken ct = default) =>
            Task.FromResult(new List<AiTemplate> { Template });
        public Task SaveTemplatesAsync(List<AiTemplate> templates, CancellationToken ct = default) => Task.CompletedTask;
        public Task<AiTemplate?> GetDefaultTemplateForAsync(string wit, CancellationToken ct = default) =>
            Task.FromResult<AiTemplate?>(Template);
    }

    private static WorkItem Wi() => new() { Id = 1, Title = "t", Type = WorkItemType.Bug, State = "New" };

    private static AiPipelineService Sut(FakeProvider p, FakeWriter w, FakeAttemptStore a, FakeTemplateStore ts)
        => new(new[] { (IAiProvider)p }, ts, w, a, @"C:\devops", "Proj", _ => Task.FromResult<WorkItem?>(Wi()));

    [Fact]
    public async Task GenerateAsync_HappyPath_RecordsSuccessAttempt()
    {
        var p = new FakeProvider(); var w = new FakeWriter(); var a = new FakeAttemptStore(); var ts = new FakeTemplateStore();
        var sut = Sut(p, w, a, ts);

        var result = await sut.GenerateAsync(workItemId: 1, templateId: "t1", providerId: "fake", ct: default);

        result.Status.Should().Be(AiAttemptStatus.Success);
        result.ValidationPassed.Should().BeTrue();
        a.Recorded.Should().HaveCount(1);
        a.Recorded[0].Status.Should().Be(AiAttemptStatus.Success);
    }

    [Fact]
    public async Task GenerateAsync_ProviderThrows_RecordsProviderErrorWithMessage()
    {
        var p = new FakeProvider { OnGenerate = _ => throw new HttpRequestException("boom") };
        var w = new FakeWriter(); var a = new FakeAttemptStore(); var ts = new FakeTemplateStore();
        var sut = Sut(p, w, a, ts);

        var result = await sut.GenerateAsync(1, "t1", "fake", default);

        result.Status.Should().Be(AiAttemptStatus.ProviderError);
        result.ErrorMessage.Should().Contain("boom");
        a.Recorded[0].Status.Should().Be(AiAttemptStatus.ProviderError);
    }

    [Fact]
    public async Task GenerateAsync_ValidationFails_RecordsValidationFailed()
    {
        var p = new FakeProvider { OnGenerate = _ =>
            Task.FromResult(new AiGenerateResult("## only one header\nx", "m", 0, 0, TimeSpan.Zero, null)) };
        var w = new FakeWriter(); var a = new FakeAttemptStore(); var ts = new FakeTemplateStore();
        var sut = Sut(p, w, a, ts);

        var result = await sut.GenerateAsync(1, "t1", "fake", default);

        result.Status.Should().Be(AiAttemptStatus.ValidationFailed);
        result.MissingSections.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GenerateAsync_Timeout_RecordsTimeout()
    {
        var p = new FakeProvider { OnGenerate = _ => throw new TimeoutException("over") };
        var w = new FakeWriter(); var a = new FakeAttemptStore(); var ts = new FakeTemplateStore();
        var sut = Sut(p, w, a, ts);

        var result = await sut.GenerateAsync(1, "t1", "fake", default);

        result.Status.Should().Be(AiAttemptStatus.Timeout);
    }

    [Fact]
    public async Task GenerateAsync_WritesPromptBeforeProviderCall()
    {
        int order = 0;
        int promptWriteOrder = 0, providerCallOrder = 0;
        var w = new FakeWriter();
        var p = new FakeProvider
        {
            OnGenerate = _ =>
            {
                providerCallOrder = ++order;
                return Task.FromResult(new AiGenerateResult(
                    "## Context summary\nx\n## Functional requirements\ny\n## Acceptance criteria\nz\n## Edge cases\na\n## Test plan\nb\n## Risks and dependencies\nc",
                    "m", 0, 0, TimeSpan.Zero, null));
            }
        };
        var a = new FakeAttemptStore(); var ts = new FakeTemplateStore();

        var sw = new OrderingWriter(w, () => promptWriteOrder = ++order);
        var sut = new AiPipelineService(
            new[] { (IAiProvider)p }, ts, sw, a,
            @"C:\devops", "Proj",
            _ => Task.FromResult<WorkItem?>(Wi()));

        await sut.GenerateAsync(1, "t1", "fake", default);

        promptWriteOrder.Should().BeLessThan(providerCallOrder);
    }

    private sealed class OrderingWriter : IAiSpecWriter
    {
        private readonly IAiSpecWriter _inner;
        private readonly Action _onPromptOnlyWrite;
        public OrderingWriter(IAiSpecWriter inner, Action onPromptOnlyWrite)
        { _inner = inner; _onPromptOnlyWrite = onPromptOnlyWrite; }
        public Task<AiFilePaths> WriteAsync(string root, string slug, int id, DateTimeOffset ts,
            string spec, string prompt, IReadOnlyList<AiAttempt> history, CancellationToken ct = default)
        {
            // The orchestrator writes prompt FIRST (spec empty) then later writes both (spec non-empty).
            if (string.IsNullOrEmpty(spec) && !string.IsNullOrEmpty(prompt))
                _onPromptOnlyWrite();
            return _inner.WriteAsync(root, slug, id, ts, spec, prompt, history, ct);
        }
    }
}
