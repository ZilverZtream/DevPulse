using System.Collections;
using System.Drawing.Drawing2D;
using System.Text;

namespace DevPulse.App.UI;

/// <summary>
/// Owner-drawn dark-themed sortable, filterable list. Designed to replace the default
/// WinForms <see cref="ListView"/>/<see cref="DataGridView"/> across DevPulse.
///
/// Logic only — visuals match the BoardForm palette.
/// </summary>
public class SortableListView<T> : Control
{
    private static readonly Color BgColor = Color.FromArgb(30, 30, 46);
    private static readonly Color HeaderBg = Color.FromArgb(36, 36, 52);
    private static readonly Color HeaderHoverBg = Color.FromArgb(46, 46, 66);
    private static readonly Color RowAltBg = Color.FromArgb(34, 34, 58);
    private static readonly Color SeparatorColor = Color.FromArgb(58, 58, 82);
    private static readonly Color SelectionBg = Color.FromArgb(58, 58, 94);
    private static readonly Color TextNormal = Color.FromArgb(220, 220, 224);
    private static readonly Color TextSelected = Color.FromArgb(255, 255, 255);
    private static readonly Color TextMuted = Color.FromArgb(136, 136, 164);
    private static readonly Color TextHeader = Color.FromArgb(210, 210, 230);
    private static readonly Color SortArrowColor = Color.FromArgb(120, 170, 230);

    private const int HeaderHeight = 26;
    private const int RowHeight = 22;
    private const int CellPadX = 8;
    private const int ResizeGripWidth = 6;

    private readonly List<SortableListColumn<T>> _columns = new();
    private readonly List<T> _allItems = new();
    private readonly List<T> _visibleItems = new();
    private readonly HashSet<int> _selectedIndices = new();

    private Func<T, bool>? _filter;
    private int _sortColumn = -1;
    private SortDirection _sortDirection = SortDirection.Ascending;
    private int _focusedIndex = -1;
    private int _anchorIndex = -1;
    private int _hoverHeader = -1;

    private readonly VScrollBar _vScroll;
    private int _scrollOffset;

    // Column resize state.
    private int _resizingColumn = -1;
    private int _resizeStartX;
    private int _resizeStartWidth;

    public SortableListView()
    {
        SetStyle(
            ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.UserPaint
            | ControlStyles.ResizeRedraw
            | ControlStyles.Selectable,
            true);
        DoubleBuffered = true;
        BackColor = BgColor;
        TabStop = true;

        AccessibleName = "Sortable list";
        AccessibleRole = AccessibleRole.List;

        _vScroll = new VScrollBar { Dock = DockStyle.Right, Visible = false, SmallChange = RowHeight, LargeChange = RowHeight * 4 };
        _vScroll.Scroll += (_, e) => { _scrollOffset = e.NewValue; Invalidate(); };
        Controls.Add(_vScroll);
    }

    // ----- Public API -----

    public IReadOnlyList<SortableListColumn<T>> Columns => _columns;

    public void AddColumn(SortableListColumn<T> column)
    {
        _columns.Add(column);
        Invalidate();
    }

    public void SetColumns(IEnumerable<SortableListColumn<T>> columns)
    {
        _columns.Clear();
        _columns.AddRange(columns);
        Invalidate();
    }

    public IReadOnlyList<T> Items => _allItems;

    public IReadOnlyList<T> VisibleItems => _visibleItems;

    public T? SelectedItem
    {
        get
        {
            if (_focusedIndex < 0 || _focusedIndex >= _visibleItems.Count) return default;
            return _visibleItems[_focusedIndex];
        }
        set
        {
            if (value is null)
            {
                _focusedIndex = -1;
                _selectedIndices.Clear();
                Invalidate();
                return;
            }
            for (int i = 0; i < _visibleItems.Count; i++)
            {
                if (EqualityComparer<T>.Default.Equals(_visibleItems[i], value))
                {
                    _focusedIndex = i;
                    _anchorIndex = i;
                    _selectedIndices.Clear();
                    _selectedIndices.Add(i);
                    EnsureVisible(i);
                    OnSelectionChanged();
                    Invalidate();
                    return;
                }
            }
        }
    }

    public IEnumerable<T> SelectedItems
        => _selectedIndices.OrderBy(i => i)
            .Where(i => i >= 0 && i < _visibleItems.Count)
            .Select(i => _visibleItems[i]);

    /// <summary>Text shown centered when there are no visible items. Default "(no items)".</summary>
    public string EmptyStateText { get; set; } = "(no items)";

    public event EventHandler<T?>? SelectionChanged;
    public event EventHandler<T?>? ItemActivated;
    public event EventHandler? Refreshed;

    public void SetItems(IEnumerable<T> items)
    {
        _allItems.Clear();
        _allItems.AddRange(items);
        RebuildVisible(preserveSelection: false);
    }

    public void SetFilter(Func<T, bool>? predicate)
    {
        _filter = predicate;
        RebuildVisible(preserveSelection: true);
    }

    /// <summary>
    /// Sort by the given column. If <paramref name="dir"/> is null, toggles asc/desc on the same column,
    /// or starts ascending when sorting a different column.
    /// </summary>
    public void Sort(int columnIndex, SortDirection? dir = null)
    {
        if (columnIndex < 0 || columnIndex >= _columns.Count) return;

        if (dir is null)
        {
            if (_sortColumn == columnIndex)
                _sortDirection = _sortDirection == SortDirection.Ascending
                    ? SortDirection.Descending
                    : SortDirection.Ascending;
            else
                _sortDirection = SortDirection.Ascending;
        }
        else
        {
            _sortDirection = dir.Value;
        }

        _sortColumn = columnIndex;
        ApplySort();
        Invalidate();
    }

    /// <summary>Raises ItemActivated for the currently focused row. Wired to Enter / double-click.</summary>
    public void ActivateSelected()
    {
        if (_focusedIndex >= 0 && _focusedIndex < _visibleItems.Count)
            ItemActivated?.Invoke(this, _visibleItems[_focusedIndex]);
    }

    /// <summary>Copies all selected rows as TSV (tab-separated columns, newline-separated rows).</summary>
    public void CopySelectedToClipboard()
    {
        var text = BuildCopyText();
        if (string.IsNullOrEmpty(text)) return;
        try { Clipboard.SetText(text); }
        catch { /* Clipboard can throw on some sessions; swallow to avoid crashing the UI. */ }
    }

    /// <summary>
    /// Returns the TSV representation of the current selection. Public so unit tests can
    /// verify the format without touching the system clipboard.
    /// </summary>
    public string BuildCopyText()
    {
        if (_columns.Count == 0) return string.Empty;
        var indices = _selectedIndices.Count > 0
            ? _selectedIndices.OrderBy(i => i).ToList()
            : (_focusedIndex >= 0 ? new List<int> { _focusedIndex } : new List<int>());

        var sb = new StringBuilder();
        bool first = true;
        foreach (var idx in indices)
        {
            if (idx < 0 || idx >= _visibleItems.Count) continue;
            if (!first) sb.Append('\n');
            first = false;
            var item = _visibleItems[idx];
            for (int c = 0; c < _columns.Count; c++)
            {
                if (c > 0) sb.Append('\t');
                sb.Append(_columns[c].GetDisplay(item));
            }
        }
        return sb.ToString();
    }

    // ----- Internal logic -----

    private void RebuildVisible(bool preserveSelection)
    {
        T? focused = preserveSelection && _focusedIndex >= 0 && _focusedIndex < _visibleItems.Count
            ? _visibleItems[_focusedIndex]
            : default;

        _visibleItems.Clear();
        if (_filter is null)
            _visibleItems.AddRange(_allItems);
        else
            foreach (var i in _allItems) if (_filter(i)) _visibleItems.Add(i);

        if (_sortColumn >= 0) ApplySort();

        _selectedIndices.Clear();
        _focusedIndex = -1;
        _anchorIndex = -1;

        if (focused is not null)
        {
            for (int i = 0; i < _visibleItems.Count; i++)
            {
                if (EqualityComparer<T>.Default.Equals(_visibleItems[i], focused))
                {
                    _focusedIndex = i;
                    _anchorIndex = i;
                    _selectedIndices.Add(i);
                    break;
                }
            }
        }

        UpdateScrollBar();
        Invalidate();
        OnSelectionChanged();
    }

    private void ApplySort()
    {
        if (_sortColumn < 0 || _sortColumn >= _columns.Count) return;
        var col = _columns[_sortColumn];
        var cmp = Comparer<object?>.Create(CompareValues);
        if (_sortDirection == SortDirection.Ascending)
            _visibleItems.Sort((a, b) => cmp.Compare(col.ValueSelector(a), col.ValueSelector(b)));
        else
            _visibleItems.Sort((a, b) => cmp.Compare(col.ValueSelector(b), col.ValueSelector(a)));
    }

    /// <summary>
    /// Generic comparer that handles nulls, strings (case-insensitive), and IComparable.
    /// Falls back to ToString() when types disagree.
    /// </summary>
    public static int CompareValues(object? a, object? b)
    {
        if (a is null && b is null) return 0;
        if (a is null) return -1;
        if (b is null) return 1;

        if (a is string sa && b is string sb)
            return string.Compare(sa, sb, StringComparison.OrdinalIgnoreCase);

        if (a.GetType() == b.GetType() && a is IComparable cmp)
            return cmp.CompareTo(b);

        return string.Compare(a.ToString(), b.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private void OnSelectionChanged()
        => SelectionChanged?.Invoke(this, _focusedIndex >= 0 && _focusedIndex < _visibleItems.Count ? _visibleItems[_focusedIndex] : default);

    // ----- Layout helpers -----

    private int ContentHeight => _visibleItems.Count * RowHeight;
    private int ViewportHeight => Math.Max(0, Height - HeaderHeight);
    private int RowsAreaTop => HeaderHeight;
    private int RowsAreaWidth => Width - (_vScroll.Visible ? _vScroll.Width : 0);

    private void UpdateScrollBar()
    {
        if (ContentHeight > ViewportHeight)
        {
            _vScroll.Visible = true;
            _vScroll.Minimum = 0;
            _vScroll.Maximum = Math.Max(0, ContentHeight - 1);
            _vScroll.LargeChange = Math.Max(RowHeight, ViewportHeight);
            _vScroll.SmallChange = RowHeight;
            _scrollOffset = Math.Min(_scrollOffset, Math.Max(0, ContentHeight - ViewportHeight));
            _vScroll.Value = Math.Max(0, Math.Min(_scrollOffset, _vScroll.Maximum - _vScroll.LargeChange + 1));
        }
        else
        {
            _vScroll.Visible = false;
            _scrollOffset = 0;
        }
    }

    private void EnsureVisible(int index)
    {
        if (index < 0) return;
        var top = index * RowHeight;
        var bottom = top + RowHeight;
        if (top < _scrollOffset) _scrollOffset = top;
        else if (bottom > _scrollOffset + ViewportHeight) _scrollOffset = bottom - ViewportHeight;
        if (_vScroll.Visible)
        {
            var max = Math.Max(0, _vScroll.Maximum - _vScroll.LargeChange + 1);
            _vScroll.Value = Math.Max(0, Math.Min(_scrollOffset, max));
        }
    }

    /// <summary>
    /// Returns the rendered width for column <paramref name="i"/>, expanding the last IsStretch
    /// column to fill remaining viewport width.
    /// </summary>
    private int GetColumnRenderWidth(int i)
    {
        if (i < 0 || i >= _columns.Count) return 0;
        var col = _columns[i];
        if (i == _columns.Count - 1 && col.IsStretch)
        {
            int used = 0;
            for (int k = 0; k < _columns.Count - 1; k++) used += _columns[k].Width;
            return Math.Max(col.Width, RowsAreaWidth - used);
        }
        return col.Width;
    }

    private int GetColumnLeft(int i)
    {
        int x = 0;
        for (int k = 0; k < i; k++) x += GetColumnRenderWidth(k);
        return x;
    }

    private int HitTestColumnHeader(int x)
    {
        if (x < 0) return -1;
        int xa = 0;
        for (int i = 0; i < _columns.Count; i++)
        {
            int w = GetColumnRenderWidth(i);
            if (x >= xa && x < xa + w) return i;
            xa += w;
        }
        return -1;
    }

    private int HitTestColumnResizeGrip(int x)
    {
        // The grip is centered on the right edge of each column (except the last stretch column).
        int xa = 0;
        for (int i = 0; i < _columns.Count; i++)
        {
            int w = GetColumnRenderWidth(i);
            int edge = xa + w;
            bool isLastStretch = (i == _columns.Count - 1) && _columns[i].IsStretch;
            if (!isLastStretch && Math.Abs(x - edge) <= ResizeGripWidth / 2) return i;
            xa += w;
        }
        return -1;
    }

    private int HitTestRow(int y)
    {
        if (y < HeaderHeight) return -1;
        int rel = (y - HeaderHeight) + _scrollOffset;
        int idx = rel / RowHeight;
        if (idx < 0 || idx >= _visibleItems.Count) return -1;
        return idx;
    }

    // ----- Painting -----

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        // Background.
        g.FillRectangle(GdiCache.Brush(BgColor), 0, 0, Width, Height);

        // Rows (clip to viewport so partially-visible top/bottom rows don't bleed into header).
        var rowsClip = new Rectangle(0, RowsAreaTop, RowsAreaWidth, ViewportHeight);
        var oldClip = g.Clip;
        g.SetClip(rowsClip);
        PaintRows(g);
        g.Clip = oldClip;

        // Header on top so it always wins over scrolled rows.
        PaintHeader(g);

        // Empty state.
        if (_visibleItems.Count == 0)
        {
            var rect = new RectangleF(0, HeaderHeight, RowsAreaWidth, ViewportHeight);
            g.DrawString(EmptyStateText, GdiCache.ListEmptyFont, GdiCache.Brush(TextMuted), rect, GdiCache.CenterFormat);
        }

        // Focus dotted rectangle (around the whole control) when this control has focus and there is no row focus visible.
        if (Focused && _focusedIndex < 0)
        {
            ControlPaint.DrawFocusRectangle(g, new Rectangle(0, 0, Width - 1, Height - 1));
        }
    }

    private void PaintHeader(Graphics g)
    {
        g.FillRectangle(GdiCache.Brush(HeaderBg), 0, 0, Width, HeaderHeight);

        int x = 0;
        for (int i = 0; i < _columns.Count; i++)
        {
            int w = GetColumnRenderWidth(i);
            var rect = new Rectangle(x, 0, w, HeaderHeight);

            if (i == _hoverHeader)
                g.FillRectangle(GdiCache.Brush(HeaderHoverBg), rect);

            // Vertical separator on the right edge.
            g.DrawLine(GdiCache.Pen(SeparatorColor, 1f), x + w - 1, 4, x + w - 1, HeaderHeight - 4);

            var col = _columns[i];
            var fmt = AlignmentToFormat(col.Alignment);
            int arrowSpace = (i == _sortColumn) ? 14 : 0;
            var textRect = new RectangleF(x + CellPadX, 4, Math.Max(0, w - CellPadX * 2 - arrowSpace), HeaderHeight - 8);
            g.DrawString(col.Name, GdiCache.ListHeaderFont, GdiCache.Brush(TextHeader), textRect, fmt);

            if (i == _sortColumn)
            {
                var arrow = _sortDirection == SortDirection.Ascending ? "▲" : "▼";
                var arrowRect = new RectangleF(x + w - CellPadX - 12, 4, 12, HeaderHeight - 8);
                g.DrawString(arrow, GdiCache.ListArrowFont, GdiCache.Brush(SortArrowColor), arrowRect, GdiCache.CenterFormat);
            }

            x += w;
        }

        // Bottom separator under the header.
        g.DrawLine(GdiCache.Pen(SeparatorColor, 1f), 0, HeaderHeight - 1, Width, HeaderHeight - 1);
    }

    private void PaintRows(Graphics g)
    {
        int firstVisible = _scrollOffset / RowHeight;
        int yOffset = HeaderHeight - (_scrollOffset % RowHeight);

        for (int i = firstVisible; i < _visibleItems.Count; i++)
        {
            int y = yOffset + (i - firstVisible) * RowHeight;
            if (y >= Height) break;

            var rowRect = new Rectangle(0, y, RowsAreaWidth, RowHeight);
            bool selected = _selectedIndices.Contains(i);

            Color rowBg;
            if (selected) rowBg = SelectionBg;
            else if ((i & 1) == 1) rowBg = RowAltBg;
            else rowBg = BgColor;

            g.FillRectangle(GdiCache.Brush(rowBg), rowRect);

            // Focused row gets a subtle left accent strip to show keyboard focus distinct from selection.
            if (i == _focusedIndex)
                g.FillRectangle(GdiCache.Brush(SortArrowColor), 0, y, 2, RowHeight);

            var textColor = selected ? TextSelected : TextNormal;

            int x = 0;
            for (int c = 0; c < _columns.Count; c++)
            {
                int w = GetColumnRenderWidth(c);
                var col = _columns[c];
                var fmt = AlignmentToFormat(col.Alignment);
                var textRect = new RectangleF(x + CellPadX, y + 2, Math.Max(0, w - CellPadX * 2), RowHeight - 4);
                var text = col.GetDisplay(_visibleItems[i]);
                g.DrawString(text, GdiCache.ListRowFont, GdiCache.Brush(textColor), textRect, fmt);
                x += w;
            }
        }
    }

    private static StringFormat AlignmentToFormat(ColumnAlignment a) => a switch
    {
        ColumnAlignment.Right => _rightFormat,
        ColumnAlignment.Center => GdiCache.CenterFormat,
        _ => _leftMiddleFormat,
    };

    // List rows want vertically-centered single-line text with ellipsis — distinct from GdiCache.LeftFormat
    // which is top-aligned for cards. Static so paint allocates nothing.
    private static readonly StringFormat _leftMiddleFormat = new()
    {
        Alignment = StringAlignment.Near,
        LineAlignment = StringAlignment.Center,
        Trimming = StringTrimming.EllipsisCharacter,
        FormatFlags = StringFormatFlags.NoWrap,
    };

    private static readonly StringFormat _rightFormat = new()
    {
        Alignment = StringAlignment.Far,
        LineAlignment = StringAlignment.Center,
        Trimming = StringTrimming.EllipsisCharacter,
        FormatFlags = StringFormatFlags.NoWrap,
    };

    // ----- Mouse / keyboard -----

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_resizingColumn >= 0)
        {
            int dx = e.X - _resizeStartX;
            var col = _columns[_resizingColumn];
            col.Width = Math.Max(40, _resizeStartWidth + dx);
            Invalidate();
            return;
        }

        // Header hover + cursor for resize grip.
        if (e.Y < HeaderHeight)
        {
            int grip = HitTestColumnResizeGrip(e.X);
            Cursor = grip >= 0 ? Cursors.VSplit : Cursors.Default;
            int newHover = HitTestColumnHeader(e.X);
            if (newHover != _hoverHeader) { _hoverHeader = newHover; Invalidate(new Rectangle(0, 0, Width, HeaderHeight)); }
        }
        else
        {
            Cursor = Cursors.Default;
            if (_hoverHeader != -1) { _hoverHeader = -1; Invalidate(new Rectangle(0, 0, Width, HeaderHeight)); }
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverHeader != -1) { _hoverHeader = -1; Invalidate(new Rectangle(0, 0, Width, HeaderHeight)); }
        Cursor = Cursors.Default;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();

        if (e.Button != MouseButtons.Left) return;

        if (e.Y < HeaderHeight)
        {
            int grip = HitTestColumnResizeGrip(e.X);
            if (grip >= 0)
            {
                _resizingColumn = grip;
                _resizeStartX = e.X;
                _resizeStartWidth = _columns[grip].Width;
                Capture = true;
                return;
            }
            int col = HitTestColumnHeader(e.X);
            if (col >= 0) Sort(col);
            return;
        }

        int row = HitTestRow(e.Y);
        if (row < 0) return;

        bool ctrl = (ModifierKeys & Keys.Control) == Keys.Control;
        bool shift = (ModifierKeys & Keys.Shift) == Keys.Shift;

        if (shift && _anchorIndex >= 0)
        {
            _selectedIndices.Clear();
            int from = Math.Min(_anchorIndex, row);
            int to = Math.Max(_anchorIndex, row);
            for (int i = from; i <= to; i++) _selectedIndices.Add(i);
            _focusedIndex = row;
        }
        else if (ctrl)
        {
            if (!_selectedIndices.Add(row)) _selectedIndices.Remove(row);
            _focusedIndex = row;
            _anchorIndex = row;
        }
        else
        {
            _selectedIndices.Clear();
            _selectedIndices.Add(row);
            _focusedIndex = row;
            _anchorIndex = row;
        }

        EnsureVisible(_focusedIndex);
        Invalidate();
        OnSelectionChanged();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_resizingColumn >= 0)
        {
            _resizingColumn = -1;
            Capture = false;
            Invalidate();
        }
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Y >= HeaderHeight && _focusedIndex >= 0) ActivateSelected();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!_vScroll.Visible) return;
        int delta = -Math.Sign(e.Delta) * RowHeight * 3;
        var max = Math.Max(0, _vScroll.Maximum - _vScroll.LargeChange + 1);
        _scrollOffset = Math.Max(0, Math.Min(_scrollOffset + delta, max));
        _vScroll.Value = _scrollOffset;
        Invalidate();
    }

    protected override bool IsInputKey(Keys keyData)
    {
        switch (keyData & Keys.KeyCode)
        {
            case Keys.Up:
            case Keys.Down:
            case Keys.Left:
            case Keys.Right:
            case Keys.PageUp:
            case Keys.PageDown:
            case Keys.Home:
            case Keys.End:
            case Keys.Enter:
            case Keys.F5:
                return true;
        }
        return base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_visibleItems.Count == 0)
        {
            if (e.KeyCode == Keys.F5) { Refreshed?.Invoke(this, EventArgs.Empty); e.Handled = true; }
            return;
        }

        bool shift = e.Shift;
        int newIndex = _focusedIndex < 0 ? 0 : _focusedIndex;
        int pageRows = Math.Max(1, ViewportHeight / RowHeight);

        switch (e.KeyCode)
        {
            case Keys.Up: newIndex = Math.Max(0, _focusedIndex - 1); break;
            case Keys.Down: newIndex = Math.Min(_visibleItems.Count - 1, _focusedIndex + 1); break;
            case Keys.PageUp: newIndex = Math.Max(0, (_focusedIndex < 0 ? 0 : _focusedIndex) - pageRows); break;
            case Keys.PageDown: newIndex = Math.Min(_visibleItems.Count - 1, (_focusedIndex < 0 ? 0 : _focusedIndex) + pageRows); break;
            case Keys.Home: newIndex = 0; break;
            case Keys.End: newIndex = _visibleItems.Count - 1; break;
            case Keys.Enter: ActivateSelected(); e.Handled = true; return;
            case Keys.F5: Refreshed?.Invoke(this, EventArgs.Empty); e.Handled = true; return;
            case Keys.A when e.Control:
                _selectedIndices.Clear();
                for (int i = 0; i < _visibleItems.Count; i++) _selectedIndices.Add(i);
                Invalidate();
                e.Handled = true;
                return;
            case Keys.C when e.Control:
                CopySelectedToClipboard();
                e.Handled = true;
                return;
            default: return;
        }

        if (shift && _anchorIndex >= 0)
        {
            _selectedIndices.Clear();
            int from = Math.Min(_anchorIndex, newIndex);
            int to = Math.Max(_anchorIndex, newIndex);
            for (int i = from; i <= to; i++) _selectedIndices.Add(i);
        }
        else
        {
            _selectedIndices.Clear();
            _selectedIndices.Add(newIndex);
            _anchorIndex = newIndex;
        }
        _focusedIndex = newIndex;
        EnsureVisible(_focusedIndex);
        OnSelectionChanged();
        Invalidate();
        e.Handled = true;
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateScrollBar();
        Invalidate();
    }
}
