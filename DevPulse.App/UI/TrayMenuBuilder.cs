using DevPulse.Core.Models;

namespace DevPulse.App.UI;

public sealed class TrayMenuBuilder
{
    private static readonly Font MenuFont = new("Segoe UI", 9f);

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
        menu.Font = MenuFont;

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
            viewMenu.DropDownItems.Add(label, null, (_, _) => openInbox(inboxName));
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
