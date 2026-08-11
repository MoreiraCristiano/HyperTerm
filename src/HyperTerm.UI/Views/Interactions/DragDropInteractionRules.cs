namespace HyperTerm.UI.Views;

internal static class DragDropInteractionRules
{
    public static double GetAdaptiveTabWidth(
        double viewportWidth,
        int tabCount,
        double minimumWidth = 96,
        double maximumWidth = 190) =>
        tabCount <= 0
            ? maximumWidth
            : Math.Clamp(viewportWidth / tabCount, minimumWidth, maximumWidth);

    public static bool CanMoveFolder(string sourceFolder, string destinationFolder) =>
        sourceFolder.Length > 0 &&
        !destinationFolder.Equals(sourceFolder, StringComparison.OrdinalIgnoreCase) &&
        !destinationFolder.StartsWith(
            $"{sourceFolder}/",
            StringComparison.OrdinalIgnoreCase);

    public static bool InsertTabAfter(double pointerX, double targetWidth) =>
        pointerX >= targetWidth / 2;

    public static double GetTabAutoScrollDirection(
        double pointerX,
        double viewportWidth,
        double edgeSize = 32) =>
        pointerX < edgeSize
            ? -1
            : pointerX > viewportWidth - edgeSize ? 1 : 0;

    public static double GetTabWheelScrollOffset(
        double currentOffset,
        double maximumOffset,
        double horizontalDelta,
        double verticalDelta,
        double scrollStep = 48)
    {
        double wheelDelta = horizontalDelta != 0 ? horizontalDelta : verticalDelta;
        return Math.Clamp(
            currentOffset - wheelDelta * scrollStep,
            0,
            Math.Max(0, maximumOffset));
    }
}
