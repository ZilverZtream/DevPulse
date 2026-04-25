using DevPulse.Core.Enums;
using DevPulse.Core.Models;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Serilog;

namespace DevPulse.Core.Services;

public sealed class RuleEngine
{
    public (string InboxName, string? RuleDescription) AssignInbox(
        DevOpsEvent evt,
        IReadOnlyList<Watcher> watchers,
        IReadOnlyList<InboxDefinition> inboxes,
        IReadOnlyList<KeywordPack> keywordPacks,
        AppSettings settings)
    {
        foreach (var watcher in watchers)
        {
            if (MatchesWatcher(evt, watcher))
                return (watcher.TargetInbox, $"Watcher:{watcher.Type}={watcher.MatchValue}");
        }

        var systemInbox = inboxes.FirstOrDefault(i => i.IsSystemInbox);
        if (systemInbox != null)
        {
            if (MatchesNeedsMyAttention(evt, settings, keywordPacks))
                return (systemInbox.Name, "NeedsMyAttention:SystemRule");
        }
        foreach (var inbox in inboxes.Where(i => !i.IsSystemInbox && i.IsEnabled).OrderBy(i => i.Order))
        {
            foreach (var rule in inbox.Rules.Where(r => r.Enabled))
            {
                if (MatchesRule(evt, rule, keywordPacks))
                    return (inbox.Name, BuildRuleDescription(rule));
            }
        }

        var fallback = inboxes
            .Where(i => !i.IsSystemInbox && i.IsEnabled)
            .OrderBy(i => i.Order)
            .LastOrDefault();

        if (fallback != null)
            return (fallback.Name, "FallbackInbox");

        var systemFallback = inboxes.FirstOrDefault(i => i.IsSystemInbox && i.IsEnabled);
        if (systemFallback != null)
            return (systemFallback.Name, "SystemFallback");

        // Last resort: route to first enabled inbox (any) to avoid orphaned "Unassigned" rows
        var anyEnabled = inboxes.FirstOrDefault(i => i.IsEnabled);
        return anyEnabled != null
            ? (anyEnabled.Name, "LastResortFallback")
            : ("Unassigned", null);
    }

    private bool MatchesNeedsMyAttention(DevOpsEvent evt, AppSettings settings, IReadOnlyList<KeywordPack> packs)
    {
        if (!evt.IsCurrentUserReviewer) return false;
        if (evt.EventSource == PrEventSource.Bot) return false;
        var msg = evt.MessageText ?? string.Empty;

        if (evt.EventMeaning == EventMeaning.Blocked) return true;
        if (evt.EventMeaning == EventMeaning.Mention) return true;

        if (evt.EventSource == PrEventSource.Human &&
            evt.EventMeaning == EventMeaning.Comment &&
            settings.PoQaGroupCanonicalKeys.Contains(evt.AuthorCanonicalKey, StringComparer.OrdinalIgnoreCase))
            return true;

        var attentionPack = packs.FirstOrDefault(p => p.Name.Equals(settings.NeedsAttentionKeywordPackName, StringComparison.OrdinalIgnoreCase));
        if (attentionPack != null)
        {
            var keywords = ExpandKeywords(attentionPack.Keywords, packs).ToList();
            if (keywords.Any(k => msg.Contains(k, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }

    private static readonly HashSet<WatcherType> _warnedUnimplemented = [];

    private static bool MatchesWatcher(DevOpsEvent evt, Watcher watcher)
    {
        // Guard empty MatchValue — Contains("") is always true, which would route every event to this watcher.
        if (string.IsNullOrWhiteSpace(watcher.MatchValue)) return false;

        return watcher.Type switch
        {
            // Author uses Equals (strict) for consistency with Repository — prevents false matches
            // like "evil-alice@corp.com" triggering an "alice@corp.com" watcher. Users who want
            // substring matching at the rule level have InboxRule.AuthorContains.
            WatcherType.Author => evt.AuthorCanonicalKey.Equals(watcher.MatchValue, StringComparison.OrdinalIgnoreCase),
            WatcherType.Repository => evt.Repository.Equals(watcher.MatchValue, StringComparison.OrdinalIgnoreCase),
            WatcherType.PrTitlePattern => MatchesGlob(evt.PullRequestTitle, watcher.MatchValue),
            WatcherType.ByWorkItemType or WatcherType.ByWorkItemState => LogUnimplementedWatcher(watcher.Type),
            _ => false
        };
    }

    private static bool LogUnimplementedWatcher(WatcherType type)
    {
        lock (_warnedUnimplemented)
        {
            if (_warnedUnimplemented.Add(type))
                Log.Warning("RuleEngine: WatcherType.{Type} is not implemented — watchers of this type will never match. Remove or reconfigure.", type);
        }
        return false;
    }

    private static bool MatchesRule(DevOpsEvent evt, InboxRule rule, IReadOnlyList<KeywordPack> packs)
    {
        var msg = evt.MessageText ?? string.Empty;
        // Excludes first — any match disqualifies
        if (!string.IsNullOrEmpty(rule.ExcludeAuthorContains) &&
            evt.AuthorCanonicalKey.Contains(rule.ExcludeAuthorContains, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrEmpty(rule.ExcludeMessageContains) &&
            msg.Contains(rule.ExcludeMessageContains, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrEmpty(rule.ExcludeRepositoryEquals) &&
            evt.Repository.Equals(rule.ExcludeRepositoryEquals, StringComparison.OrdinalIgnoreCase))
            return false;

        // Includes — all must match
        if (rule.EventSourceEquals.HasValue && evt.EventSource != rule.EventSourceEquals.Value) return false;
        if (rule.EventMeaningEquals.HasValue && evt.EventMeaning != rule.EventMeaningEquals.Value) return false;
        if (!string.IsNullOrEmpty(rule.AuthorEquals) &&
            !evt.AuthorCanonicalKey.Equals(rule.AuthorEquals, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrEmpty(rule.AuthorContains) &&
            !evt.AuthorCanonicalKey.Contains(rule.AuthorContains, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrEmpty(rule.RepositoryEquals) &&
            !evt.Repository.Equals(rule.RepositoryEquals, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrEmpty(rule.ProjectEquals) &&
            !evt.Project.Equals(rule.ProjectEquals, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrEmpty(rule.StatusEquals) &&
            !evt.Status.Equals(rule.StatusEquals, StringComparison.OrdinalIgnoreCase)) return false;

        if (rule.MessageContainsAny?.Count > 0)
        {
            var keywords = ExpandKeywords(rule.MessageContainsAny, packs).ToList();
            // If expansion yielded no keywords (all whitespace or empty pack refs), skip the clause
            // rather than treating it as "always reject".
            if (keywords.Count > 0 && !keywords.Any(k => msg.Contains(k, StringComparison.OrdinalIgnoreCase)))
                return false;
        }

        if (rule.MessageContainsAll?.Count > 0)
        {
            var keywords = ExpandKeywords(rule.MessageContainsAll, packs).ToList();
            // Mirror MessageContainsAny: empty post-expansion skips the clause rather than
            // treating it as an unsatisfiable filter (e.g., all entries were deleted pack refs).
            if (keywords.Count > 0 && !keywords.All(k => msg.Contains(k, StringComparison.OrdinalIgnoreCase)))
                return false;
        }

        return true;
    }

    private static IEnumerable<string> ExpandKeywords(IEnumerable<string> items, IReadOnlyList<KeywordPack> packs, bool expandPackRefs = true)
    {
        foreach (var item in items)
        {
            if (!expandPackRefs)
            {
                if (!string.IsNullOrWhiteSpace(item)) yield return item;
                continue;
            }
            var pack = packs.FirstOrDefault(p => p.Name.Equals(item, StringComparison.OrdinalIgnoreCase));
            if (pack != null)
            {
                foreach (var kw in pack.Keywords)
                    if (!string.IsNullOrWhiteSpace(kw)) yield return kw;
            }
            else if (!string.IsNullOrWhiteSpace(item))
            {
                yield return item;
            }
        }
    }

    private const int MaxGlobCacheSize = 256;
    private static readonly ConcurrentDictionary<string, Regex> GlobCache = new();

    private static Regex CompileGlob(string pattern) =>
        new("^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    private static bool MatchesGlob(string text, string pattern)
    {
        if (GlobCache.TryGetValue(pattern, out var cached))
            return cached.IsMatch(text);

        var regex = CompileGlob(pattern);
        if (GlobCache.Count < MaxGlobCacheSize)
            GlobCache.TryAdd(pattern, regex);
        return regex.IsMatch(text);
    }

    private static string BuildRuleDescription(InboxRule rule)
    {
        var parts = new List<string>();
        if (rule.EventSourceEquals.HasValue) parts.Add($"EventSourceEquals={rule.EventSourceEquals}");
        if (rule.EventMeaningEquals.HasValue) parts.Add($"EventMeaningEquals={rule.EventMeaningEquals}");
        if (!string.IsNullOrEmpty(rule.AuthorEquals)) parts.Add($"AuthorEquals={rule.AuthorEquals}");
        if (!string.IsNullOrEmpty(rule.AuthorContains)) parts.Add($"AuthorContains={rule.AuthorContains}");
        if (!string.IsNullOrEmpty(rule.RepositoryEquals)) parts.Add($"RepositoryEquals={rule.RepositoryEquals}");
        if (!string.IsNullOrEmpty(rule.ProjectEquals)) parts.Add($"ProjectEquals={rule.ProjectEquals}");
        if (!string.IsNullOrEmpty(rule.StatusEquals)) parts.Add($"StatusEquals={rule.StatusEquals}");
        if (!string.IsNullOrEmpty(rule.ExcludeAuthorContains)) parts.Add($"ExcludeAuthorContains={rule.ExcludeAuthorContains}");
        if (!string.IsNullOrEmpty(rule.ExcludeMessageContains)) parts.Add($"ExcludeMessageContains={rule.ExcludeMessageContains}");
        if (!string.IsNullOrEmpty(rule.ExcludeRepositoryEquals)) parts.Add($"ExcludeRepositoryEquals={rule.ExcludeRepositoryEquals}");
        if (rule.MessageContainsAny?.Count > 0) parts.Add($"MessageContainsAny=[{string.Join(",", rule.MessageContainsAny)}]");
        if (rule.MessageContainsAll?.Count > 0) parts.Add($"MessageContainsAll=[{string.Join(",", rule.MessageContainsAll)}]");
        return string.Join(";", parts) is { Length: > 0 } s ? s : "NoConditions";
    }
}
