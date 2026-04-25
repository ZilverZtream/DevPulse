using System.Text.Json;
using DevPulse.Core.Interfaces;
using DevPulse.Core.Models;
using DevPulse.Core.Services;

namespace DevPulse.Infrastructure.Ai;

public sealed class FilesystemSpecWriter : IAiSpecWriter
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public async Task<AiFilePaths> WriteAsync(
        string outputRoot,
        string projectSlug,
        int workItemId,
        DateTimeOffset timestampUtc,
        string specMarkdown,
        string promptMarkdown,
        IReadOnlyList<AiAttempt> attemptHistory,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(outputRoot))
            throw new ArgumentException("Output root is required", nameof(outputRoot));
        if (workItemId <= 0)
            throw new ArgumentException("Work item id must be positive", nameof(workItemId));
        if (projectSlug.Contains(".."))
            throw new ArgumentException("Project slug cannot contain '..' path segments", nameof(projectSlug));

        var slug = Slugify.Project(projectSlug);
        if (string.IsNullOrEmpty(slug))
            throw new ArgumentException("Project slug cannot be empty after slugify", nameof(projectSlug));

        var rootFull = Path.GetFullPath(outputRoot);
        var candidate = Path.GetFullPath(Path.Combine(rootFull, slug, workItemId.ToString()));
        if (!candidate.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Resolved path escapes output root", nameof(projectSlug));

        Directory.CreateDirectory(candidate);

        // Defense against symlink/junction smuggling: even if `candidate` lexically lives under
        // `rootFull`, an attacker (or a careless user) could replace any directory in the chain
        // with a junction pointing elsewhere. Walk every segment from rootFull leaf-ward and
        // refuse if any of them is a reparse point.
        RejectReparsePointsInChain(rootFull, candidate);

        var tsStamp = timestampUtc.ToUniversalTime().ToString("yyyyMMddTHHmmssZ");
        var specPath = Path.Combine(candidate, $"spec-{tsStamp}.md");
        var promptPath = Path.Combine(candidate, $"prompt-{tsStamp}.md");
        var metaPath = Path.Combine(candidate, "meta.json");

        await File.WriteAllTextAsync(specPath, specMarkdown, ct);
        await File.WriteAllTextAsync(promptPath, promptMarkdown, ct);
        var metaJson = JsonSerializer.Serialize(attemptHistory, JsonOpts);
        await File.WriteAllTextAsync(metaPath, metaJson, ct);

        return new AiFilePaths(specPath, promptPath, metaPath);
    }

    private static void RejectReparsePointsInChain(string rootFull, string candidate)
    {
        // Walk from candidate up to (and including) rootFull; throw if any directory on the
        // chain has the ReparsePoint attribute. We only check directories that already exist
        // — Directory.CreateDirectory above ensures the candidate path itself exists.
        var rootNorm = Path.TrimEndingDirectorySeparator(rootFull);
        var current = Path.TrimEndingDirectorySeparator(candidate);

        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(current))
            {
                var attrs = File.GetAttributes(current);
                if ((attrs & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"Refusing to write through reparse point: {current}");
            }

            if (string.Equals(current, rootNorm, StringComparison.OrdinalIgnoreCase))
                break;

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                break;
            current = parent;
        }
    }
}
