namespace HyperTerm.UI.Views;

internal static class DragDropInteractionRules
{
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
}
