using HyperTerm.Core.Models;

namespace HyperTerm.Core.Tests;

public sealed class PaneTreeTests
{
    [Fact]
    public void FirstPaneIsRootAndActive()
    {
        Guid pane = Guid.NewGuid();
        var tree = new PaneTree(pane);

        Assert.Equal(pane, tree.ActivePaneId);
        Assert.Equal([pane], tree.PaneIds);
        Assert.Equal(new TerminalPaneNode(pane), tree.Root);
    }

    [Theory]
    [InlineData(SplitOrientation.Horizontal)]
    [InlineData(SplitOrientation.Vertical)]
    public void SplitReplacesLeafAndActivatesNewPane(SplitOrientation orientation)
    {
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        var tree = new PaneTree(first);

        Assert.True(tree.Split(first, second, orientation));

        var split = Assert.IsType<SplitPaneNode>(tree.Root);
        Assert.Equal(orientation, split.Orientation);
        Assert.Equal(new TerminalPaneNode(first), split.First);
        Assert.Equal(new TerminalPaneNode(second), split.Second);
        Assert.Equal(second, tree.ActivePaneId);
    }

    [Fact]
    public void NestedSplitPreservesSiblings()
    {
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        Guid third = Guid.NewGuid();
        var tree = new PaneTree(first);
        tree.Split(first, second, SplitOrientation.Vertical);

        Assert.True(tree.Split(second, third, SplitOrientation.Horizontal));

        var root = Assert.IsType<SplitPaneNode>(tree.Root);
        Assert.Equal(new TerminalPaneNode(first), root.First);
        var nested = Assert.IsType<SplitPaneNode>(root.Second);
        Assert.Equal([first, second, third], tree.PaneIds);
        Assert.Equal(SplitOrientation.Horizontal, nested.Orientation);
    }

    [Fact]
    public void RemoveNestedLeafCollapsesParentAndSelectsAdjacentPane()
    {
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        Guid third = Guid.NewGuid();
        var tree = new PaneTree(first);
        tree.Split(first, second, SplitOrientation.Vertical);
        tree.Split(second, third, SplitOrientation.Horizontal);

        Assert.True(tree.Remove(third, out bool last));

        Assert.False(last);
        Assert.Equal(second, tree.ActivePaneId);
        var root = Assert.IsType<SplitPaneNode>(tree.Root);
        Assert.Equal(new TerminalPaneNode(second), root.Second);
    }

    [Fact]
    public void RemoveLastPaneEmptiesTree()
    {
        Guid pane = Guid.NewGuid();
        var tree = new PaneTree(pane);

        Assert.True(tree.Remove(pane, out bool last));

        Assert.True(last);
        Assert.Null(tree.Root);
        Assert.Null(tree.ActivePaneId);
    }

    [Fact]
    public void FocusWrapsInLayoutOrder()
    {
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        var tree = new PaneTree(first);
        tree.Split(first, second, SplitOrientation.Vertical);

        Assert.True(tree.FocusNext());
        Assert.Equal(first, tree.ActivePaneId);
        Assert.True(tree.FocusPrevious());
        Assert.Equal(second, tree.ActivePaneId);
    }

    [Fact]
    public void HorizontalFocusOnlyMovesToRequestedSide()
    {
        Guid left = Guid.NewGuid();
        Guid topRight = Guid.NewGuid();
        Guid bottomRight = Guid.NewGuid();
        var tree = new PaneTree(left);
        tree.Split(left, topRight, SplitOrientation.Vertical);
        tree.Split(topRight, bottomRight, SplitOrientation.Horizontal);

        Assert.True(tree.FocusLeft());
        Assert.Equal(left, tree.ActivePaneId);
        Assert.False(tree.FocusLeft());
        Assert.Equal(left, tree.ActivePaneId);
        Assert.True(tree.FocusRight());
        Assert.Equal(topRight, tree.ActivePaneId);
        Assert.False(tree.FocusRight());
        Assert.Equal(topRight, tree.ActivePaneId);
    }

    [Fact]
    public void VerticalFocusStaysInColumnInTwoByTwoLayout()
    {
        Guid topLeft = Guid.NewGuid();
        Guid topRight = Guid.NewGuid();
        Guid bottomLeft = Guid.NewGuid();
        Guid bottomRight = Guid.NewGuid();
        var tree = new PaneTree(topLeft);
        tree.Split(topLeft, topRight, SplitOrientation.Vertical);
        tree.Split(topLeft, bottomLeft, SplitOrientation.Horizontal);
        tree.Split(topRight, bottomRight, SplitOrientation.Horizontal);

        Assert.True(tree.SetActive(topLeft));
        Assert.True(tree.FocusDown());
        Assert.Equal(bottomLeft, tree.ActivePaneId);
        Assert.False(tree.FocusDown());
        Assert.True(tree.FocusUp());
        Assert.Equal(topLeft, tree.ActivePaneId);

        Assert.True(tree.SetActive(topRight));
        Assert.True(tree.FocusDown());
        Assert.Equal(bottomRight, tree.ActivePaneId);
        Assert.True(tree.FocusUp());
        Assert.Equal(topRight, tree.ActivePaneId);
    }

    [Fact]
    public void RatioValidatesBoundsAndUpdatesOwningSplit()
    {
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        var tree = new PaneTree(first);
        tree.Split(first, second, SplitOrientation.Horizontal);

        Assert.False(tree.SetRatio(first, 0.01));
        Assert.True(tree.SetRatio(first, 0.7));
        Assert.Equal(0.7, Assert.IsType<SplitPaneNode>(tree.Root).Ratio);
    }

    [Fact]
    public void InvalidOperationsPreserveTree()
    {
        Guid pane = Guid.NewGuid();
        var tree = new PaneTree(pane);
        PaneNode root = tree.Root!;

        Assert.False(tree.Split(Guid.NewGuid(), Guid.NewGuid(), SplitOrientation.Vertical));
        Assert.False(tree.Split(pane, pane, SplitOrientation.Vertical));
        Assert.False(tree.SetActive(Guid.NewGuid()));
        Assert.False(tree.Remove(Guid.NewGuid(), out _));
        Assert.Same(root, tree.Root);
    }
}
