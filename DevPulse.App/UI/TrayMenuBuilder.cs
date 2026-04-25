using DevPulse.Core.Models;

namespace DevPulse.App.UI;

public sealed class TrayMenuBuilder
{
    // Tag prefix used to mark per-inbox menu items so UpdateUnreadCounts can find them in-place.
    private const string InboxItemTagPrefix = "inbox:";

    public static void UpdateUnreadCounts(ContextMenuStrip? menu, Dictionary<string, int> unreadCounts)
    {
        if (menu == null) return;
        foreach (ToolStripItem item in menu.Items)
        {
            if (item is not ToolStripMenuItem top) continue;
            foreach (ToolStripItem child in top.DropDownItems)
            {
                if (child is not ToolStripMenuItem mi) continue;
                if (mi.Tag is not string tag || !tag.StartsWith(InboxItemTagPrefix, StringComparison.Ordinal)) continue;
                var inboxName = tag[InboxItemTagPrefix.Length..];
                var count = unreadCounts.GetValueOrDefault(inboxName, 0);
                mi.Text = count > 0 ? $"{inboxName}  ({count})" : inboxName;
            }
        }
    }

    public ContextMenuStrip Build(
        IReadOnlyList<InboxDefinition> inboxes,
        Dictionary<string, int> unreadCounts,
        Action refreshPrs,
        Action refreshBoard,
        Action<string> openInbox,
        Action openBoard,
        Action openMuted,
        Action openSettings,
        Action openDebug,
        string orgUrl,
        Action exit)
    {
        var menu = new ContextMenuStrip();
        menu.BackColor = Color.FromArgb(36, 36, 52);
        menu.ForeColor = Color.FromArgb(220, 220, 235);
        menu.Font = SystemFonts.MenuFont;

        // Refresh submenu
        var refreshMenu = new ToolStripMenuItem("Refresh now");
        refreshMenu.DropDownItems.Add("Refresh PRs", null, (_, _) => refreshPrs());
        refreshMenu.DropDownItems.Add("Refresh board", null, (_, _) => refreshBoard());
        menu.Items.Add(refreshMenu);

        menu.Items.Add(new ToolStripSeparator());

        // View latest submenu
        var viewMenu = new ToolStripMenuItem("View latest");
        foreach (var inbox in inboxes.OrderBy(i => i.Order))
        {
            var count = unreadCounts.GetValueOrDefault(inbox.Name, 0);
            var label = count > 0 ? $"{inbox.Name}  ({count})" : inbox.Name;
            var inboxName = inbox.Name;
            var item = new ToolStripMenuItem(label) { Tag = InboxItemTagPrefix + inboxName };
            item.Click += (_, _) => openInbox(inboxName);
            viewMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(viewMenu);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open board", null, (_, _) => openBoard());
        menu.Items.Add("Muted PRs", null, (_, _) => openMuted());
        menu.Items.Add("Open Azure DevOps", null, (_, _) =>
        {
            if (!string.IsNullOrEmpty(orgUrl))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(orgUrl) { UseShellExecute = true });
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Debug window", null, (_, _) => openDebug());
        menu.Items.Add("Settings", null, (_, _) => openSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => exit());

        return menu;
    }
}
