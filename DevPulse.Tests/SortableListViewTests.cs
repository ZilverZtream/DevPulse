using DevPulse.App.UI;
using FluentAssertions;

namespace DevPulse.Tests;

public class SortableListViewTests
{
    private sealed record Row(int Id, string Name, DateTime CreatedUtc);

    private static SortableListView<Row> NewListWithThreeColumns()
    {
        var lv = new SortableListView<Row>();
        lv.AddColumn(new SortableListColumn<Row>
        {
            Name = "Id",
            Width = 60,
            ValueSelector = r => r.Id,
            Alignment = ColumnAlignment.Right,
        });
        lv.AddColumn(new SortableListColumn<Row>
        {
            Name = "Name",
            Width = 200,
            ValueSelector = r => r.Name,
        });
        lv.AddColumn(new SortableListColumn<Row>
        {
            Name = "Created",
            Width = 140,
            ValueSelector = r => r.CreatedUtc,
            DisplaySelector = r => r.CreatedUtc.ToString("u"),
            IsStretch = true,
        });
        return lv;
    }

    [Fact]
    public void SetItems_PopulatesItemsAndVisibleItems()
    {
        var lv = NewListWithThreeColumns();
        var data = new[]
        {
            new Row(3, "alpha", new DateTime(2024,1,1)),
            new Row(1, "Bravo", new DateTime(2023,6,1)),
            new Row(2, "charlie", new DateTime(2025,3,1)),
        };

        lv.SetItems(data);

        lv.Items.Should().HaveCount(3);
        lv.VisibleItems.Should().HaveCount(3);
    }

    [Fact]
    public void SetFilter_LimitsVisibleItemsButLeavesItemsAlone()
    {
        var lv = NewListWithThreeColumns();
        lv.SetItems(new[]
        {
            new Row(1, "apple", DateTime.UtcNow),
            new Row(2, "banana", DateTime.UtcNow),
            new Row(3, "avocado", DateTime.UtcNow),
        });

        lv.SetFilter(r => r.Name.StartsWith("a", StringComparison.OrdinalIgnoreCase));

        lv.Items.Should().HaveCount(3);
        lv.VisibleItems.Should().HaveCount(2);
        lv.VisibleItems.Select(r => r.Name).Should().BeEquivalentTo(new[] { "apple", "avocado" });
    }

    [Fact]
    public void SetFilter_Null_RestoresAllItems()
    {
        var lv = NewListWithThreeColumns();
        lv.SetItems(new[]
        {
            new Row(1, "apple", DateTime.UtcNow),
            new Row(2, "banana", DateTime.UtcNow),
        });
        lv.SetFilter(r => r.Id == 1);
        lv.VisibleItems.Should().HaveCount(1);

        lv.SetFilter(null);

        lv.VisibleItems.Should().HaveCount(2);
    }

    [Fact]
    public void Sort_ByInt_ReturnsAscendingThenDescending()
    {
        var lv = NewListWithThreeColumns();
        lv.SetItems(new[]
        {
            new Row(3, "a", DateTime.UtcNow),
            new Row(1, "b", DateTime.UtcNow),
            new Row(2, "c", DateTime.UtcNow),
        });

        lv.Sort(0);
        lv.VisibleItems.Select(r => r.Id).Should().Equal(1, 2, 3);

        lv.Sort(0); // toggles
        lv.VisibleItems.Select(r => r.Id).Should().Equal(3, 2, 1);

        lv.Sort(0); // toggles back
        lv.VisibleItems.Select(r => r.Id).Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Sort_ByString_IsCaseInsensitive()
    {
        var lv = NewListWithThreeColumns();
        lv.SetItems(new[]
        {
            new Row(1, "banana", DateTime.UtcNow),
            new Row(2, "Apple", DateTime.UtcNow),
            new Row(3, "cherry", DateTime.UtcNow),
        });

        lv.Sort(1);

        lv.VisibleItems.Select(r => r.Name).Should().Equal("Apple", "banana", "cherry");
    }

    [Fact]
    public void Sort_ByDateTime_OrdersChronologically()
    {
        var lv = NewListWithThreeColumns();
        var d1 = new DateTime(2024, 1, 1);
        var d2 = new DateTime(2025, 6, 1);
        var d3 = new DateTime(2023, 12, 1);
        lv.SetItems(new[]
        {
            new Row(1, "x", d1),
            new Row(2, "y", d2),
            new Row(3, "z", d3),
        });

        lv.Sort(2);
        lv.VisibleItems.Select(r => r.CreatedUtc).Should().Equal(d3, d1, d2);

        lv.Sort(2, SortDirection.Descending);
        lv.VisibleItems.Select(r => r.CreatedUtc).Should().Equal(d2, d1, d3);
    }

    [Fact]
    public void Sort_ExplicitDirection_DoesNotToggle()
    {
        var lv = NewListWithThreeColumns();
        lv.SetItems(new[]
        {
            new Row(3, "a", DateTime.UtcNow),
            new Row(1, "b", DateTime.UtcNow),
            new Row(2, "c", DateTime.UtcNow),
        });

        lv.Sort(0, SortDirection.Descending);
        lv.VisibleItems.Select(r => r.Id).Should().Equal(3, 2, 1);

        lv.Sort(0, SortDirection.Descending); // explicit again — should NOT flip
        lv.VisibleItems.Select(r => r.Id).Should().Equal(3, 2, 1);
    }

    [Fact]
    public void ActivateSelected_FiresItemActivatedWithFocusedRow()
    {
        var lv = NewListWithThreeColumns();
        var rows = new[]
        {
            new Row(1, "a", DateTime.UtcNow),
            new Row(2, "b", DateTime.UtcNow),
            new Row(3, "c", DateTime.UtcNow),
        };
        lv.SetItems(rows);
        lv.SelectedItem = rows[1];

        Row? activated = null;
        lv.ItemActivated += (_, r) => activated = r;

        lv.ActivateSelected();

        activated.Should().NotBeNull();
        activated!.Id.Should().Be(2);
    }

    [Fact]
    public void ActivateSelected_NoSelection_DoesNotThrowAndDoesNotFire()
    {
        var lv = NewListWithThreeColumns();
        lv.SetItems(new[] { new Row(1, "a", DateTime.UtcNow) });
        bool fired = false;
        lv.ItemActivated += (_, _) => fired = true;

        lv.ActivateSelected();

        fired.Should().BeFalse();
    }

    [Fact]
    public void SelectionChanged_FiresOnSelectedItemSetter()
    {
        var lv = NewListWithThreeColumns();
        var rows = new[]
        {
            new Row(1, "a", DateTime.UtcNow),
            new Row(2, "b", DateTime.UtcNow),
        };
        lv.SetItems(rows);

        Row? changed = null;
        int count = 0;
        lv.SelectionChanged += (_, r) => { changed = r; count++; };

        lv.SelectedItem = rows[1];

        changed.Should().NotBeNull();
        changed!.Id.Should().Be(2);
        count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void BuildCopyText_SingleSelection_TabSeparatedColumns()
    {
        var lv = NewListWithThreeColumns();
        var rows = new[]
        {
            new Row(42, "Hello", new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
        };
        lv.SetItems(rows);
        lv.SelectedItem = rows[0];

        var text = lv.BuildCopyText();

        text.Should().Be($"42\tHello\t{rows[0].CreatedUtc:u}");
    }

    [Fact]
    public void BuildCopyText_NoSelection_ReturnsEmpty()
    {
        var lv = NewListWithThreeColumns();
        lv.SetItems(new[] { new Row(1, "a", DateTime.UtcNow) });

        lv.BuildCopyText().Should().BeEmpty();
    }

    [Fact]
    public void BuildCopyText_MultiSelection_NewlineBetweenRows()
    {
        var lv = NewListWithThreeColumns();
        var rows = new[]
        {
            new Row(1, "alpha", new DateTime(2024,1,1, 0, 0, 0, DateTimeKind.Utc)),
            new Row(2, "beta",  new DateTime(2024,2,1, 0, 0, 0, DateTimeKind.Utc)),
            new Row(3, "gamma", new DateTime(2024,3,1, 0, 0, 0, DateTimeKind.Utc)),
        };
        lv.SetItems(rows);

        // Drive multi-select via internals helper: select rows 0 and 2 by index.
        SelectIndicesViaPublicApi(lv, rows, new[] { 0, 2 });

        var text = lv.BuildCopyText();

        var lines = text.Split('\n');
        lines.Should().HaveCount(2);
        lines[0].Should().Be($"1\talpha\t{rows[0].CreatedUtc:u}");
        lines[1].Should().Be($"3\tgamma\t{rows[2].CreatedUtc:u}");
    }

    [Fact]
    public void CompareValues_HandlesNullsAndMixedTypes()
    {
        SortableListView<Row>.CompareValues(null, null).Should().Be(0);
        SortableListView<Row>.CompareValues(null, "x").Should().BeLessThan(0);
        SortableListView<Row>.CompareValues("x", null).Should().BeGreaterThan(0);
        SortableListView<Row>.CompareValues("apple", "Banana").Should().BeLessThan(0); // case-insensitive
        SortableListView<Row>.CompareValues(2, 10).Should().BeLessThan(0);
        SortableListView<Row>.CompareValues(new DateTime(2024, 1, 1), new DateTime(2025, 1, 1)).Should().BeLessThan(0);
    }

    [Fact]
    public void EmptyStateText_DefaultIsNoItems_AndIsConfigurable()
    {
        var lv = NewListWithThreeColumns();
        lv.EmptyStateText.Should().Be("(no items)");

        lv.EmptyStateText = "nothing here";
        lv.EmptyStateText.Should().Be("nothing here");
    }

    /// <summary>
    /// Multi-select isn't exposed publicly, so we drive the internal HashSet via reflection
    /// to validate the multi-row format of BuildCopyText. The setter API is single-select only.
    /// </summary>
    private static void SelectIndicesViaPublicApi(SortableListView<Row> lv, Row[] rows, int[] indices)
    {
        lv.SelectedItem = rows[indices[0]];
        var field = typeof(SortableListView<Row>).GetField("_selectedIndices",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var set = (HashSet<int>)field!.GetValue(lv)!;
        set.Clear();
        foreach (var i in indices) set.Add(i);
    }
}
