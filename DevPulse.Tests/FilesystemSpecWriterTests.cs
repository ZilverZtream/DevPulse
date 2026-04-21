using System.Text.Json;
using DevPulse.Core.Enums;
using DevPulse.Core.Models;
using DevPulse.Infrastructure.Ai;
using FluentAssertions;

namespace DevPulse.Tests;

public class FilesystemSpecWriterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"devpulse-fs-{Guid.NewGuid():N}");
    private readonly FilesystemSpecWriter _sut = new();

    [Fact]
    public async Task WriteAsync_CreatesVersionedFiles()
    {
        var ts = new DateTimeOffset(2026, 4, 21, 14, 30, 22, TimeSpan.Zero);
        var paths = await _sut.WriteAsync(_root, "MyProject", 42, ts,
            "## Spec\nbody", "prompt body", []);

        File.Exists(paths.SpecPath).Should().BeTrue();
        File.Exists(paths.PromptPath).Should().BeTrue();
        File.Exists(paths.MetaPath).Should().BeTrue();
        paths.SpecPath.Should().EndWith("spec-20260421T143022Z.md");
        paths.PromptPath.Should().EndWith("prompt-20260421T143022Z.md");
        (await File.ReadAllTextAsync(paths.SpecPath)).Should().Contain("## Spec");
    }

    [Fact]
    public async Task WriteAsync_CreatesDirectoryIfMissing()
    {
        var target = Path.Combine(_root, "NewProj", "99");
        Directory.Exists(target).Should().BeFalse();
        await _sut.WriteAsync(_root, "NewProj", 99, DateTimeOffset.UtcNow,
            "## s\nb", "p", []);
        Directory.Exists(target).Should().BeTrue();
    }

    [Fact]
    public async Task WriteAsync_SlugifiesProjectName()
    {
        await _sut.WriteAsync(_root, "My Project: With/Bad Chars", 1,
            DateTimeOffset.UtcNow, "## s\nb", "p", []);
        Directory.GetDirectories(_root).Should().ContainSingle()
            .Which.Should().EndWith("My_Project__With_Bad_Chars");
    }

    [Fact]
    public async Task WriteAsync_RejectsEmptySlug()
    {
        var act = () => _sut.WriteAsync(_root, "", 1,
            DateTimeOffset.UtcNow, "## s\nb", "p", []);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task WriteAsync_RejectsPathTraversalInSlug()
    {
        var act = () => _sut.WriteAsync(_root, @"..\..\windows", 1,
            DateTimeOffset.UtcNow, "## s\nb", "p", []);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task WriteAsync_RejectsNonPositiveWorkItemId()
    {
        var act = () => _sut.WriteAsync(_root, "Proj", 0,
            DateTimeOffset.UtcNow, "## s\nb", "p", []);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task WriteAsync_MetaJsonIsValidJsonArray()
    {
        var attempts = new List<AiAttempt>
        {
            new() { Id = "a1", WorkItemId = 1, Status = AiAttemptStatus.Success,
                    CreatedAtUtc = DateTimeOffset.UtcNow }
        };
        var paths = await _sut.WriteAsync(_root, "P", 1, DateTimeOffset.UtcNow,
            "## s\nb", "p", attempts);
        var json = await File.ReadAllTextAsync(paths.MetaPath);
        var doc = JsonDocument.Parse(json);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().Should().Be(1);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }
}
