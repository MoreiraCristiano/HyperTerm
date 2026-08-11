using HyperTerm.UI.Views;

namespace HyperTerm.UI.Tests;

public sealed class DragDropInteractionRulesTests
{
    [Theory]
    [InlineData(800, 2, 190)]
    [InlineData(800, 5, 160)]
    [InlineData(800, 20, 96)]
    [InlineData(800, 0, 190)]
    public void Tab_width_shrinks_between_maximum_and_minimum(
        double viewportWidth,
        int tabCount,
        double expected) =>
        Assert.Equal(
            expected,
            DragDropInteractionRules.GetAdaptiveTabWidth(viewportWidth, tabCount));

    [Theory]
    [InlineData("Servers", "", true)]
    [InlineData("Servers", "Archive", true)]
    [InlineData("Servers", "servers", false)]
    [InlineData("Servers", "SERVERS/Production", false)]
    [InlineData("", "Archive", false)]
    public void Folder_move_rejects_self_and_descendants(
        string source,
        string destination,
        bool expected) =>
        Assert.Equal(expected, DragDropInteractionRules.CanMoveFolder(source, destination));

    [Theory]
    [InlineData(49, 100, false)]
    [InlineData(50, 100, true)]
    [InlineData(51, 100, true)]
    public void Tab_drop_uses_target_midpoint(
        double pointerX,
        double width,
        bool expected) =>
        Assert.Equal(expected, DragDropInteractionRules.InsertTabAfter(pointerX, width));

    [Theory]
    [InlineData(0, 200, -1)]
    [InlineData(31, 200, -1)]
    [InlineData(32, 200, 0)]
    [InlineData(168, 200, 0)]
    [InlineData(169, 200, 1)]
    public void Tab_auto_scroll_uses_fixed_edge_zones(
        double pointerX,
        double width,
        double expected) =>
        Assert.Equal(
            expected,
            DragDropInteractionRules.GetTabAutoScrollDirection(pointerX, width));

    [Theory]
    [InlineData(100, 300, 0, -1, 148)]
    [InlineData(100, 300, 0, 1, 52)]
    [InlineData(100, 300, -1, 1, 148)]
    [InlineData(10, 300, 0, 1, 0)]
    [InlineData(290, 300, 0, -1, 300)]
    [InlineData(100, 0, 0, -1, 0)]
    public void Tab_wheel_scrolls_horizontally_within_bounds(
        double currentOffset,
        double maximumOffset,
        double horizontalDelta,
        double verticalDelta,
        double expected) =>
        Assert.Equal(
            expected,
            DragDropInteractionRules.GetTabWheelScrollOffset(
                currentOffset,
                maximumOffset,
                horizontalDelta,
                verticalDelta));
}
