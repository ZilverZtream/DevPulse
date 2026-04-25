namespace DevPulse.App.UI;

public enum ColumnAlignment { Left, Right, Center }

public enum SortDirection { Ascending, Descending }

/// <summary>
/// Declarative column definition for <see cref="SortableListView{T}"/>.
/// </summary>
public sealed class SortableListColumn<T>
{
    /// <summary>Header text displayed at the top of the column.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Default pixel width. May change at runtime if the user resizes the column.</summary>
    public int Width { get; set; } = 120;

    /// <summary>Selector returning the value to sort/compare on.</summary>
    public Func<T, object?> ValueSelector { get; init; } = _ => null;

    /// <summary>Selector returning the rendered cell text. Defaults to ValueSelector.ToString().</summary>
    public Func<T, string>? DisplaySelector { get; init; }

    /// <summary>Horizontal text alignment within the cell.</summary>
    public ColumnAlignment Alignment { get; init; } = ColumnAlignment.Left;

    /// <summary>
    /// When true and this is the last column, it stretches to fill remaining width
    /// regardless of <see cref="Width"/>.
    /// </summary>
    public bool IsStretch { get; init; }

    /// <summary>Returns the cell text for an item, falling back to ValueSelector.ToString().</summary>
    public string GetDisplay(T item)
    {
        if (DisplaySelector != null) return DisplaySelector(item) ?? string.Empty;
        var v = ValueSelector(item);
        return v?.ToString() ?? string.Empty;
    }
}
