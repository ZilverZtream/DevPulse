using DevPulse.Core.Models;

namespace DevPulse.App.UI;

public sealed class BoardColumnPanel : Panel
{
    private static readonly Color HeaderBg = Color.FromArgb(42, 42, 60);
    private static readonly Color ColBg = Color.FromArgb(38, 38, 56);
    private static readonly Color[] DotColors = [
        Color.FromArgb(91, 155, 213),   // Feature Request - blue
        Color.FromArgb(130, 130, 150),  // Backlog - gray
        Color.FromArgb(210, 140, 50),   // Doing - orange
        Color.FromArgb(140, 100, 200),  // In Review - purple
        Color.FromArgb(60, 160, 80)     // Done - green
    ];

    private readonly int _columnIndex;
    private readonly Panel _cardContainer;
    private readonly Label _headerLabel;

    public string ColumnName { get; }

    public BoardColumnPanel(string columnName, int columnIndex)
    {
        ColumnName = columnName;
        _columnIndex = columnIndex;
        Width = 220;
        BackColor = ColBg;
        Padding = new Padding(6);

        _headerLabel = new Label
        {
            Height = 30,
            Dock = DockStyle.Top,
            ForeColor = Color.FromArgb(210, 210, 230),
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(4, 0, 0, 0)
        };

        _cardContainer = new Panel
        {
            AutoScroll = true,
            Dock = DockStyle.Fill,
            BackColor = ColBg,
            Padding = new Padding(4)
        };

        Controls.Add(_cardContainer);
        Controls.Add(_headerLabel);
    }

    public void SetItems(IReadOnlyList<WorkItem> items, IReadOnlyList<WorkItem>? filteredItems = null)
    {
        var staleCount = items.Count(i => i.AgingLevel == Core.Enums.AgingLevel.Stale);
        _headerLabel.Text = $"  {ColumnName}  {items.Count}";
        if (staleCount > 0) _headerLabel.Text += $"  ·  {staleCount} stale";

        _cardContainer.SuspendLayout();
        _cardContainer.Controls.Clear();

        var displaySet = filteredItems ?? items;
        foreach (var item in items)
        {
            var card = new WorkItemCard(item)
            {
                Width = _cardContainer.Width - 20,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Dimmed = filteredItems != null && !filteredItems.Any(f => f.Id == item.Id)
            };
            _cardContainer.Controls.Add(card);
        }

        _cardContainer.ResumeLayout(true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var dot = _columnIndex < DotColors.Length ? DotColors[_columnIndex] : Color.Gray;
        using var br = new SolidBrush(dot);
        e.Graphics.FillEllipse(br, 6, 10, 10, 10);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        foreach (Control c in _cardContainer.Controls)
            c.Width = _cardContainer.Width - 20;
    }
}
