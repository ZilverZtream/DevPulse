using DevPulse.Core.Enums;
using DevPulse.Core.Models;
using System.Text.RegularExpressions;

namespace DevPulse.Core.Services;

public sealed class RuleEngine
{
    public string AssignInbox(
        DevOpsEvent evt,
        IReadOnlyList<Watcher> watchers,
        IReadOnlyList<InboxDefinition> inboxes,
        IReadOnlyList<KeywordPack> keywordPacks,
        AppSettings settings)
    {
        // 1. Watchers short-circuit rule evaluation
        foreach (var watcher in watchers)
        {
            if (MatchesWatcher(evt, watcher))
            {
                evt.MatchedRuleDescription = $"Watcher:{watcher.Type}={watcher.Pattern}";
                return watcher.TargetInbox;
            }
        }

        // 2. System inbox (Needs My Attention) — always evaluated first
        var systemInbox = inboxes.FirstOrDefault(i => i.IsSystemInbox);
        if (systemInbox != null && MatchesNeedsMyAttention(evt, settings, keywordPacks))
        {
            evt.MatchedRuleDescription = "NeedsMyAttention:SystemRule";
            return systemInbox.Name;
        }

        // 3. User inboxes in order; first match wins
        foreach (var inbox in inboxes.Where(i => !i.IsSystemInbox && i.IsEnabled).OrderBy(i => i.Order))
        {
            foreach (var rule in inbox.Rules.Where(r => r.Enabled))
            {
                if (MatchesRule(evt, rule, keywordPacks))
                {
                    evt.MatchedRuleDescription = BuildRuleDescription(rule);
                    return inbox.Name;
                }
            }
        }

        // 4. Fallback inbox (last enabled non-system inbox, expected to have no conditions)
        var fallback = inboxes
            .Where(i => !i.IsSystemInbox && i.IsEnabled)
            .OrderBy(i => i.Order)
            .LastOrDefault();

        if (fallback != null)
        {
            evt.MatchedRuleDescription = "FallbackInbox";
            return fallback.Name;
        }

        return "Unassigned";
    }

    private bool MatchesNeedsMyAttention(DevOpsEvent evt, AppSettings settings, IReadOnlyList<KeywordPack> packs)
    {
        if (!evt.IsCurrentUserReviewer) return false;
        if (evt.EventSource == EventSource.Bot || evt.EventSource == EventSource.System) return false;

        if (evt.EventMeaning == EventMeaning.Blocked) return true;
        if (evt.EventMeaning == EventMeaning.Mention) return true;

        if (evt.EventSource == EventSource.Human &&
            evt.EventMeaning == EventMeaning.Comment &&
            settings.PoQaGroupCanonicalKeys.Contains(evt.AuthorCanonicalKey, StringComparer.OrdinalIgnoreCase))
            return true;

        var attentionPack = packs.FirstOrDefault(p => p.Name.Equals(settings.NeedsAttentionKeywordPackName, StringComparison.OrdinalIgnoreCase));
        if (attentionPack != null &&
            attentionPack.Keywords.Any(k => evt.MessageText.Contains(k, StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }

    private static bool MatchesWatcher(DevOpsEvent evt, Watcher watcher) => watcher.Type switch
    {
        WatcherType.Author => evt.AuthorCanonicalKey.Contains(watcher.Pattern, StringComparison.OrdinalIgnoreCase),
        WatcherType.Repository => evt.Repository.Equals(watcher.Pattern, StringComparison.OrdinalIgnoreCase),
        WatcherType.PrTitlePattern => MatchesGlob(evt.PullRequestTitle, watcher.Pattern),
        _ => false
    };

    private static bool MatchesRule(DevOpsEvent evt, InboxRule rule, IReadOnlyList<KeywordPack> packs)
    {
        // Excludes first — any match disqualifies
        if (!string.IsNullOrEmpty(rule.ExcludeAuthorContains) &&
            evt.AuthorCanonicalKey.Contains(rule.ExcludeAuthorContains, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrEmpty(rule.ExcludeMessageContains) &&
            evt.MessageText.Contains(rule.ExcludeMessageContains, StringComparison.OrdinalIgnoreCase))
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
            var keywords = ExpandKeywords(rule.MessageContainsAny, packs);
            if (!keywords.Any(k => evt.MessageText.Contains(k, StringComparison.OrdinalIgnoreCase)))
                return false;
        }

        if (rule.MessageContainsAll?.Count > 0)
        {
            var keywords = ExpandKeywords(rule.MessageContainsAll, packs);
            if (!keywords.All(k => evt.MessageText.Contains(k, StringComparison.OrdinalIgnoreCase)))
                return false;
        }

        return true;
    }

    private static IEnumerable<string> ExpandKeywords(IEnumerable<string> items, IReadOnlyList<KeywordPack> packs)
    {
        foreach (var item in items)
        {
            var pack = packs.FirstOrDefault(p => p.Name.Equals(item, StringComparison.OrdinalIgnoreCase));
            if (pack != null)
                foreach (var kw in pack.Keywords) yield return kw;
            else
                yield return item;
        }
    }

    private static bool MatchesGlob(string text, string pattern)
    {
        var regex = "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
        return Regex.IsMatch(text, regex, RegexOptions.IgnoreCase);
    }

    private static string BuildRuleDescription(InboxRule rule)
    {
        var parts = new List<string>();
        if (rule.EventSourceEquals.HasValue) parts.Add($"EventSourceEquals={rule.EventSourceEquals}");
        if (rule.EventMeaningEquals.HasValue) parts.Add($"EventMeaningEquals={rule.EventMeaningEquals}");
        if (!string.IsNullOrEmpty(rule.AuthorEquals)) parts.Add($"AuthorEquals={rule.AuthorEquals}");
        if (!string.IsNullOrEmpty(rule.AuthorContains)) parts.Add($"AuthorContains={rule.AuthorContains}");
        if (!string.IsNullOrEmpty(rule.ExcludeAuthorContains)) parts.Add($"ExcludeAuthorContains={rule.ExcludeAuthorContains}");
        if (rule.MessageContainsAny?.Count > 0) parts.Add($"MessageContainsAny=[{string.Join(",", rule.MessageContainsAny)}]");
        return string.Join(";", parts) is { Length: > 0 } s ? s : "NoConditions";
    }
}
