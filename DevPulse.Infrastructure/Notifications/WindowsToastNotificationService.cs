using DevPulse.Core.Interfaces;
using DevPulse.Core.Models;
using DevPulse.Core.Enums;
using Microsoft.Toolkit.Uwp.Notifications;
using Serilog;

namespace DevPulse.Infrastructure.Notifications;

public sealed class WindowsToastNotificationService : INotificationService
{
    public Task ShowAsync(DevOpsEvent evt, CancellationToken ct = default)
    {
        try
        {
            new ToastContentBuilder()
                .AddText(BuildTitle(evt))
                .AddText(BuildBody(evt))
                .Show();
        }
        catch (InvalidOperationException ex)
        {
            Log.Warning(ex, "Toast notification failed (notification platform unavailable)");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Toast notification failed unexpectedly for PR #{PrId}", evt.PullRequestId);
        }
        return Task.CompletedTask;
    }

    private static string BuildTitle(DevOpsEvent evt) => evt.EventMeaning switch
    {
        EventMeaning.Merged => "PR merged",
        EventMeaning.Abandoned => "PR abandoned",
        EventMeaning.Blocked => "Reviewer blocked PR",
        EventMeaning.VoteChanged => "Vote changed",
        EventMeaning.ReviewerAdded => "Added as reviewer",
        EventMeaning.Mention => "You were mentioned",
        _ => "PR activity"
    };

    private static string BuildBody(DevOpsEvent evt)
    {
        if (evt.IsCollapsed)
            return $"{evt.AuthorDisplayName} added {evt.CollapsedCount} comments on PR #{evt.PullRequestId}";

        var msg = evt.MessageText.Length > 80 ? evt.MessageText[..80] + "…" : evt.MessageText;
        return string.IsNullOrWhiteSpace(msg)
            ? $"PR #{evt.PullRequestId}: {evt.PullRequestTitle}"
            : $"{evt.AuthorDisplayName}: {msg}";
    }
}
