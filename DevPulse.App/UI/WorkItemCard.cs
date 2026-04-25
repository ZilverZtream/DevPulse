using DevPulse.Core.Enums;
using DevPulse.Core.Models;

namespace DevPulse.App.UI;

public sealed class WorkItemCard : Panel
{
    private static readonly Color BgNormal = Color.FromArgb(50, 50, 74);
    private static readonly Color BgBorder = Color.FromArgb(68, 68, 102);
    private static readonly Color BgStaleBorder = Color.FromArgb(180, 60, 60);
    private static readonly Color TextPrimary = Color.FromArgb(224, 224, 240);
    private static readonly Color TextSecondary = Color.FromArgb(144, 144, 176);
    private static readonly Color AccentBlue = Color.FromArgb(91, 155, 213);

    private static readonly Dictionary<WorkItemType, Color> TypeColors = new()
    {
        [WorkItemType.Task]     = Color.FromArgb(74, 125, 168),
        [WorkItemType.Feature]  = Color.FromArgb(90, 79, 168),
        [WorkItemType.Bug]      = Color.FromArgb(192, 82, 42),
        [WorkItemType.UserStory]= Color.FromArgb(42, 138, 106),
        [WorkItemType.Unknown]  = Color.FromArgb(100, 100, 120)
    };

    private static readonly Dictionary<string, Color> PriorityColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["1"] = Color.FromArgb(192, 48, 48),
        ["2"] = Color.FromArgb(192, 144, 48),
        ["3"] = Color.FromArgb(48, 160, 48),
        ["4"] = Color.FromArgb(100, 100, 120)
    };

    private static readonly Color[] InitialsPalette =
    [
        Color.FromArgb(80, 120, 180),
        Color.FromArgb(120, 80, 160),
        Color.FromArgb(60, 140, 100),
        Color.FromArgb(160, 80, 80)
    ];

    public WorkItem Item { get; }
    public bool Dimmed { get; set; }

    private readonly string _initials;

    public string BuildTooltipText()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append('#').Append(Item.Id).Append("  ").AppendLine(Item.Title);

        var assignee = string.IsNullOrWhiteSpace(Item.AssignedToDisplayName) ? "Unassigned" : Item.AssignedToDisplayName;
        sb.Append("Assigned to: ").AppendLine(assignee);

        // FirstSeenUtc is the closest stable "since DevPulse first observed it" timestamp;
        // DiscoveredAtUtc is the next best fallback if FirstSeen wasn't recorded.
        var createdAt = Item.FirstSeenUtc ?? Item.DiscoveredAtUtc;
        if (createdAt != default)
            sb.Append("Created ").Append(FormatRelativeAge(DateTimeOffset.UtcNow - createdAt)).Append(" ago");

        return sb.ToString().TrimEnd();
    }

    private static string FormatRelativeAge(TimeSpan span)
    {
        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours}h";
        if (span.TotalDays < 30) return $"{(int)span.TotalDays}d";
        if (span.TotalDays < 365) return $"{(int)(span.TotalDays / 30)}mo";
        return $"{(int)(span.TotalDays / 365)}y";
    }

    public WorkItemCard(WorkItem item)
    {
        Item = item;
        Height = 110;
        Margin = new Padding(0, 0, 0, 6);
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
        BackColor = BgNormal;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);

        _initials = ComputeInitials(item.AssignedToDisplayName);

        ContextMenuStrip = BuildContextMenu();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // ContextMenuStrip is owned by this card — Panel doesn't dispose it for us.
            var menu = ContextMenuStrip;
            ContextMenuStrip = null;
            menu?.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var alpha = Dimmed ? 80 : 255;
        var bg = Color.FromArgb(alpha, BgNormal);
        var borderColor = Item.AgingLevel == AgingLevel.Stale ? BgStaleBorder : BgBorder;
        var borderWidth = Item.AgingLevel == AgingLevel.Stale ? 2f : 1f;

        GdiCache.FillRoundedRect(g, bg, 0, 0, Width - 1, Height - 1, 6);
        GdiCache.DrawRoundedRect(g, borderColor, borderWidth, 0, 0, Width - 1, Height - 1, 6);

        int y = 8;

        // ID
        g.DrawString($"#{Item.Id}", GdiCache.IdFont, GdiCache.Brush(Color.FromArgb(alpha, TextSecondary)), 8, y);

        y += 16;

        // Title
        var titleRect = new RectangleF(8, y, Width - 16, 32);
        g.DrawString(Item.Title, GdiCache.TitleFont,
            GdiCache.Brush(Color.FromArgb(alpha, TextPrimary)), titleRect, GdiCache.TitleFormat);

        y += 36;

        // Badges row
        int x = 8;
        x = DrawBadge(g, Item.Type.ToString(), TypeColors.GetValueOrDefault(Item.Type, Color.Gray), x, y, alpha);
        x += 4;
        if (!string.IsNullOrEmpty(Item.Priority))
            x = DrawBadge(g, $"P{Item.Priority}", PriorityColors.GetValueOrDefault(Item.Priority, Color.Gray), x, y, alpha);

        // Assignee circle
        if (!string.IsNullOrEmpty(Item.AssignedToDisplayName))
        {
            x += 6;
            DrawInitialsCircle(g, Item.AssignedToDisplayName, _initials, x, y, alpha);
            x += 24;
        }

        // Aging badge
        if (Item.AgingLevel != AgingLevel.Fresh)
        {
            var agingColor = Item.AgingLevel == AgingLevel.Stale
                ? Color.FromArgb(192, 48, 48)
                : Color.FromArgb(192, 144, 48);
            x += 4;
            DrawBadge(g, $"{Item.DaysInCurrentState}d", agingColor, x, y, alpha);
        }

        y += 20;

        // PR link
        if (!string.IsNullOrEmpty(Item.LinkedPullRequestId))
        {
            g.DrawString($"PR #{Item.LinkedPullRequestId}", GdiCache.LinkFont,
                GdiCache.Brush(Color.FromArgb(alpha, AccentBlue)), 8, y);
        }
    }

    private static int DrawBadge(Graphics g, string text, Color color, int x, int y, int alpha)
    {
        var size = g.MeasureString(text, GdiCache.BadgeFont);
        int w = (int)size.Width + 10, h = 16;
        GdiCache.FillRoundedRect(g, Color.FromArgb(alpha, color), x, y, w, h, 4);
        g.DrawString(text, GdiCache.BadgeFont, GdiCache.Brush(Color.FromArgb(alpha, Color.White)), x + 5, y + 2);
        return x + w;
    }

    private static void DrawInitialsCircle(Graphics g, string displayName, string initials, int x, int y, int alpha)
    {
        var hash = Math.Abs(displayName.GetHashCode());
        var c = InitialsPalette[hash % InitialsPalette.Length];
        g.FillEllipse(GdiCache.Brush(Color.FromArgb(alpha, c)), x, y, 18, 18);
        g.DrawString(initials, GdiCache.InitialsFont,
            GdiCache.Brush(Color.FromArgb(alpha, Color.White)),
            new RectangleF(x, y, 18, 18), GdiCache.CenterFormat);
    }

    internal static string ComputeInitials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2
            ? $"{parts[0][0]}{parts[^1][0]}".ToUpper()
            : name.Length >= 2 ? name[..2].ToUpper() : name.ToUpper();
    }

    public event EventHandler? OnDraftRequested;
    public event EventHandler? OnViewDraftsRequested;
    public event EventHandler? OnOpenFolderRequested;

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();

        menu.Items.Add("Open in Azure DevOps", null, (_, _) =>
        {
            if (!string.IsNullOrEmpty(Item.WorkItemUrl))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Item.WorkItemUrl) { UseShellExecute = true });
        });

        menu.Items.Add(new ToolStripSeparator());

        var draftItem = new ToolStripMenuItem("Draft spec with AI…");
        draftItem.Click += (_, _) => OnDraftRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(draftItem);

        var viewItem = new ToolStripMenuItem("View AI drafts…");
        viewItem.Click += (_, _) => OnViewDraftsRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(viewItem);

        var folderItem = new ToolStripMenuItem("Open AI output folder");
        folderItem.Click += (_, _) => OnOpenFolderRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(folderItem);

        menu.Opening += (_, _) =>
        {
            draftItem.Enabled = Item.FirstSeenUtc.HasValue
                && (Item.State.Equals("New", StringComparison.OrdinalIgnoreCase)
                    || Item.State.Equals("Proposed", StringComparison.OrdinalIgnoreCase));
            draftItem.ToolTipText = draftItem.Enabled
                ? "Draft an AI spec for this New/Proposed item"
                : "AI drafts are only available for first-seen New/Proposed items";
        };

        return menu;
    }
}

// Graphics extension helpers — kept for compatibility; new code should use GdiCache directly.
internal static class GraphicsExtensions
{
    public static void FillRoundedRectangle(this Graphics g, Brush brush, float x, float y, float w, float h, float r)
    {
        using var path = GdiCache.RoundedRect(x, y, w, h, r);
        g.FillPath(brush, path);
    }

    public static void DrawRoundedRectangle(this Graphics g, Pen pen, float x, float y, float w, float h, float r)
    {
        using var path = GdiCache.RoundedRect(x, y, w, h, r);
        g.DrawPath(pen, path);
    }
}
