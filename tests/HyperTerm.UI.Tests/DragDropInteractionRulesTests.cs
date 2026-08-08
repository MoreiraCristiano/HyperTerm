using HyperTerm.UI.Views;

namespace HyperTerm.UI.Tests;

public sealed class DragDropInteractionRulesTests
{
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
}
