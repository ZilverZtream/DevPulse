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

    public WorkItem Item { get; }
    public bool Dimmed { get; set; }

    private static readonly Font TitleFont = new("Segoe UI", 9f, FontStyle.Regular);
    private static readonly Font IdFont = new("Segoe UI", 8f, FontStyle.Bold);
    private static readonly Font BadgeFont = new("Segoe UI", 7.5f, FontStyle.Bold);
    private static readonly Font InitialsFont = new("Segoe UI", 7f, FontStyle.Bold);
    private static readonly Font LinkFont = new("Segoe UI", 7.5f, FontStyle.Regular);

    public WorkItemCard(WorkItem item)
    {
        Item = item;
        Height = 110;
        Margin = new Padding(0, 0, 0, 6);
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
        BackColor = BgNormal;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);

        ContextMenuStrip = BuildContextMenu();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var alpha = Dimmed ? 80 : 255;
        var bg = Color.FromArgb(alpha, BgNormal);
        var border = Item.AgingLevel == AgingLevel.Stale ? BgStaleBorder : BgBorder;

        using (var br = new SolidBrush(bg))
            g.FillRoundedRectangle(br, 0, 0, Width - 1, Height - 1, 6);

        using (var pen = new Pen(border, Item.AgingLevel == AgingLevel.Stale ? 2 : 1))
            g.DrawRoundedRectangle(pen, 0, 0, Width - 1, Height - 1, 6);

        int y = 8;

        // ID
        using (var br = new SolidBrush(Color.FromArgb(alpha, TextSecondary)))
            g.DrawString($"#{Item.Id}", IdFont, br, 8, y);

        y += 16;

        // Title
        var titleRect = new RectangleF(8, y, Width - 16, 32);
        using (var br = new SolidBrush(Color.FromArgb(alpha, TextPrimary)))
        using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.LineLimit })
        {
            sf.LineAlignment = StringAlignment.Near;
            g.DrawString(Item.Title, TitleFont, br, titleRect, sf);
        }

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
            DrawInitialsCircle(g, Item.AssignedToDisplayName, x, y, alpha);
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
            using var br = new SolidBrush(Color.FromArgb(alpha, AccentBlue));
            g.DrawString($"PR #{Item.LinkedPullRequestId}", LinkFont, br, 8, y);
        }
    }

    private static int DrawBadge(Graphics g, string text, Color color, int x, int y, int alpha)
    {
        var size = g.MeasureString(text, BadgeFont);
        int w = (int)size.Width + 10, h = 16;
        using var br = new SolidBrush(Color.FromArgb(alpha, color));
        g.FillRoundedRectangle(br, x, y, w, h, 4);
        using var textBr = new SolidBrush(Color.FromArgb(alpha, Color.White));
        g.DrawString(text, BadgeFont, textBr, x + 5, y + 2);
        return x + w;
    }

    private static void DrawInitialsCircle(Graphics g, string displayName, int x, int y, int alpha)
    {
        var initials = GetInitials(displayName);
        var hash = Math.Abs(displayName.GetHashCode());
        var colors = new[] {
            Color.FromArgb(80, 120, 180), Color.FromArgb(120, 80, 160),
            Color.FromArgb(60, 140, 100), Color.FromArgb(160, 80, 80)
        };
        var c = colors[hash % colors.Length];
        using var br = new SolidBrush(Color.FromArgb(alpha, c));
        g.FillEllipse(br, x, y, 18, 18);
        using var textBr = new SolidBrush(Color.FromArgb(alpha, Color.White));
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(initials, InitialsFont, textBr, new RectangleF(x, y, 18, 18), sf);
    }

    private static string GetInitials(string name)
    {
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

// Graphics extension helpers
internal static class GraphicsExtensions
{
    public static void FillRoundedRectangle(this Graphics g, Brush brush, float x, float y, float w, float h, float r)
    {
        using var path = RoundedRect(x, y, w, h, r);
        g.FillPath(brush, path);
    }

    public static void DrawRoundedRectangle(this Graphics g, Pen pen, float x, float y, float w, float h, float r)
    {
        using var path = RoundedRect(x, y, w, h, r);
        g.DrawPath(pen, path);
    }

    private static System.Drawing.Drawing2D.GraphicsPath RoundedRect(float x, float y, float w, float h, float r)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddArc(x, y, r * 2, r * 2, 180, 90);
        path.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
        path.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
        path.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
        path.CloseFigure();
        return path;
    }
}
