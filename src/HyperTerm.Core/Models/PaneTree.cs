namespace HyperTerm.Core.Models;

public enum SplitOrientation
{
    Horizontal,
    Vertical,
}

public abstract record PaneNode;

public sealed record TerminalPaneNode(Guid PaneId) : PaneNode;

public sealed record SplitPaneNode(
    SplitOrientation Orientation,
    PaneNode First,
    PaneNode Second,
    double Ratio) : PaneNode
{
    public const double MinimumRatio = 0.1;
    public const double MaximumRatio = 0.9;

    public SplitPaneNode(
        SplitOrientation orientation,
        PaneNode first,
        PaneNode second)
        : this(orientation, first, second, 0.5)
    {
    }
}

public sealed class PaneTree
{
    public PaneTree(Guid firstPaneId)
    {
        if (firstPaneId == Guid.Empty)
        {
            throw new ArgumentException("Pane identifier cannot be empty.", nameof(firstPaneId));
        }

        Root = new TerminalPaneNode(firstPaneId);
        ActivePaneId = firstPaneId;
    }

    public PaneNode? Root { get; private set; }

    public Guid? ActivePaneId { get; private set; }

    public IReadOnlyList<Guid> PaneIds => Enumerate(Root).ToArray();

    public bool SetActive(Guid paneId)
    {
        if (!Contains(paneId))
        {
            return false;
        }

        ActivePaneId = paneId;
        return true;
    }

    public bool Split(
        Guid paneId,
        Guid newPaneId,
        SplitOrientation orientation,
        double ratio = 0.5)
    {
        if (newPaneId == Guid.Empty || Contains(newPaneId) ||
            ratio is < SplitPaneNode.MinimumRatio or > SplitPaneNode.MaximumRatio)
        {
            return false;
        }

        bool replaced = false;
        Root = Replace(Root, paneId, node =>
        {
            replaced = true;
            return new SplitPaneNode(
                orientation,
                node,
                new TerminalPaneNode(newPaneId),
                ratio);
        });
        if (replaced)
        {
            ActivePaneId = newPaneId;
        }

        return replaced;
    }

    public bool SetRatio(Guid firstDescendantPaneId, double ratio)
    {
        if (ratio is < SplitPaneNode.MinimumRatio or > SplitPaneNode.MaximumRatio)
        {
            return false;
        }

        bool changed = false;
        Root = UpdateSplit(Root, firstDescendantPaneId, split =>
        {
            changed = true;
            return split with { Ratio = ratio };
        });
        return changed;
    }

    public bool Remove(Guid paneId, out bool lastPaneRemoved)
    {
        lastPaneRemoved = false;
        if (!Contains(paneId))
        {
            return false;
        }

        IReadOnlyList<Guid> before = PaneIds;
        if (before.Count == 1)
        {
            Root = null;
            ActivePaneId = null;
            lastPaneRemoved = true;
            return true;
        }

        int removedIndex = before.IndexOf(paneId);
        Root = RemoveNode(Root!, paneId, out bool removed);
        if (!removed)
        {
            return false;
        }

        IReadOnlyList<Guid> remaining = PaneIds;
        if (ActivePaneId == paneId)
        {
            ActivePaneId = remaining[Math.Min(removedIndex, remaining.Count - 1)];
        }

        return true;
    }

    public bool FocusNext() => FocusBy(1);

    public bool FocusPrevious() => FocusBy(-1);

    public bool FocusLeft() => FocusHorizontal(-1);

    public bool FocusRight() => FocusHorizontal(1);

    public bool FocusUp() => FocusVertical(-1);

    public bool FocusDown() => FocusVertical(1);

    public bool Contains(Guid paneId) => Enumerate(Root).Contains(paneId);

    private bool FocusBy(int offset)
    {
        IReadOnlyList<Guid> panes = PaneIds;
        if (panes.Count == 0)
        {
            return false;
        }

        int current = ActivePaneId is Guid active ? panes.IndexOf(active) : -1;
        int next = current < 0
            ? 0
            : (current + offset + panes.Count) % panes.Count;
        ActivePaneId = panes[next];
        return true;
    }

    private bool FocusHorizontal(int direction)
    {
        if (ActivePaneId is not Guid activePaneId)
        {
            return false;
        }

        var bounds = new Dictionary<Guid, PaneBounds>();
        CollectBounds(Root, new PaneBounds(0, 0, 1, 1), bounds);
        if (!bounds.TryGetValue(activePaneId, out PaneBounds active))
        {
            return false;
        }

        var candidates = bounds
            .Where(item => item.Key != activePaneId)
            .Select(item => (item.Key, Bounds: item.Value))
            .Where(item => direction < 0
                ? item.Bounds.Right <= active.Left + double.Epsilon
                : item.Bounds.Left >= active.Right - double.Epsilon)
            .OrderBy(item => direction < 0
                ? active.Left - item.Bounds.Right
                : item.Bounds.Left - active.Right)
            .ThenBy(item => Math.Abs(item.Bounds.CenterY - active.CenterY))
            .ToArray();
        if (candidates.Length == 0)
        {
            return false;
        }

        ActivePaneId = candidates[0].Key;
        return true;
    }

    private bool FocusVertical(int direction)
    {
        if (ActivePaneId is not Guid activePaneId)
        {
            return false;
        }

        var bounds = new Dictionary<Guid, PaneBounds>();
        CollectBounds(Root, new PaneBounds(0, 0, 1, 1), bounds);
        if (!bounds.TryGetValue(activePaneId, out PaneBounds active))
        {
            return false;
        }

        var candidates = bounds
            .Where(item => item.Key != activePaneId)
            .Select(item => (item.Key, Bounds: item.Value))
            .Where(item => direction < 0
                ? item.Bounds.Bottom <= active.Top + double.Epsilon
                : item.Bounds.Top >= active.Bottom - double.Epsilon)
            .OrderBy(item => direction < 0
                ? active.Top - item.Bounds.Bottom
                : item.Bounds.Top - active.Bottom)
            .ThenBy(item => Math.Abs(item.Bounds.CenterX - active.CenterX))
            .ToArray();
        if (candidates.Length == 0)
        {
            return false;
        }

        ActivePaneId = candidates[0].Key;
        return true;
    }

    private static void CollectBounds(
        PaneNode? node,
        PaneBounds bounds,
        IDictionary<Guid, PaneBounds> result)
    {
        switch (node)
        {
            case TerminalPaneNode terminal:
                result[terminal.PaneId] = bounds;
                break;
            case SplitPaneNode { Orientation: SplitOrientation.Vertical } split:
                double firstWidth = bounds.Width * split.Ratio;
                CollectBounds(
                    split.First,
                    bounds with { Width = firstWidth },
                    result);
                CollectBounds(
                    split.Second,
                    bounds with
                    {
                        Left = bounds.Left + firstWidth,
                        Width = bounds.Width - firstWidth,
                    },
                    result);
                break;
            case SplitPaneNode split:
                double firstHeight = bounds.Height * split.Ratio;
                CollectBounds(
                    split.First,
                    bounds with { Height = firstHeight },
                    result);
                CollectBounds(
                    split.Second,
                    bounds with
                    {
                        Top = bounds.Top + firstHeight,
                        Height = bounds.Height - firstHeight,
                    },
                    result);
                break;
        }
    }

    private readonly record struct PaneBounds(
        double Left,
        double Top,
        double Width,
        double Height)
    {
        public double Right => Left + Width;

        public double Bottom => Top + Height;

        public double CenterX => Left + (Width / 2);

        public double CenterY => Top + (Height / 2);
    }

    private static IEnumerable<Guid> Enumerate(PaneNode? node)
    {
        switch (node)
        {
            case TerminalPaneNode terminal:
                yield return terminal.PaneId;
                break;
            case SplitPaneNode split:
                foreach (Guid paneId in Enumerate(split.First))
                {
                    yield return paneId;
                }

                foreach (Guid paneId in Enumerate(split.Second))
                {
                    yield return paneId;
                }

                break;
        }
    }

    private static PaneNode? Replace(
        PaneNode? node,
        Guid paneId,
        Func<TerminalPaneNode, PaneNode> replacement) =>
        node switch
        {
            TerminalPaneNode terminal when terminal.PaneId == paneId => replacement(terminal),
            SplitPaneNode split => split with
            {
                First = Replace(split.First, paneId, replacement) ?? split.First,
                Second = Replace(split.Second, paneId, replacement) ?? split.Second,
            },
            _ => node,
        };

    private static PaneNode? UpdateSplit(
        PaneNode? node,
        Guid firstDescendantPaneId,
        Func<SplitPaneNode, SplitPaneNode> update)
    {
        if (node is not SplitPaneNode split)
        {
            return node;
        }

        if (Enumerate(split.First).FirstOrDefault() == firstDescendantPaneId)
        {
            return update(split);
        }

        return split with
        {
            First = UpdateSplit(split.First, firstDescendantPaneId, update) ?? split.First,
            Second = UpdateSplit(split.Second, firstDescendantPaneId, update) ?? split.Second,
        };
    }

    private static PaneNode? RemoveNode(PaneNode node, Guid paneId, out bool removed)
    {
        if (node is TerminalPaneNode terminal)
        {
            removed = terminal.PaneId == paneId;
            return removed ? null : node;
        }

        var split = (SplitPaneNode)node;
        PaneNode? first = RemoveNode(split.First, paneId, out removed);
        if (removed)
        {
            return first is null ? split.Second : split with { First = first };
        }

        PaneNode? second = RemoveNode(split.Second, paneId, out removed);
        return removed
            ? second is null ? split.First : split with { Second = second }
            : split;
    }
}

internal static class PaneIdListExtensions
{
    public static int IndexOf(this IReadOnlyList<Guid> values, Guid value)
    {
        for (int index = 0; index < values.Count; index++)
        {
            if (values[index] == value)
            {
                return index;
            }
        }

        return -1;
    }
}
