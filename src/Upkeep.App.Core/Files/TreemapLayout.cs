namespace Upkeep.App.Core.Files;

/// <summary>One node in the disk usage tree — a folder, or a file inside one.</summary>
public sealed record UsageNode(string Name, string Path, long SizeBytes, bool IsDirectory)
{
    public IReadOnlyList<UsageNode> Children { get; init; } = [];
}

/// <summary>A node placed on the canvas, in the caller's coordinate space.</summary>
public sealed record TreemapRect(UsageNode Node, double X, double Y, double Width, double Height);

/// <summary>
/// Squarified treemap layout: lays children out so each rectangle is as close to square as it can
/// be, because long thin slivers are impossible to compare by eye or to click.
/// <para>
/// Pure geometry — no drawing, no WinUI. The canvas renders whatever comes back, which is what
/// keeps the layout testable and the renderer dumb.
/// </para>
/// </summary>
public static class TreemapLayout
{
    /// <summary>
    /// Places <paramref name="nodes"/> inside a rectangle of the given size, in proportion to
    /// their sizes. Nodes with no size are dropped rather than drawn as invisible slivers.
    /// </summary>
    public static IReadOnlyList<TreemapRect> Squarify(IReadOnlyList<UsageNode> nodes, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        var placed = new List<TreemapRect>();
        if (width <= 0 || height <= 0)
        {
            return placed;
        }

        var remaining = nodes
            .Where(node => node.SizeBytes > 0)
            .OrderByDescending(node => node.SizeBytes)
            .ToList();

        long total = remaining.Sum(node => node.SizeBytes);
        if (total == 0)
        {
            return placed;
        }

        double area = width * height;
        var queue = new Queue<(UsageNode Node, double Area)>(
            remaining.Select(node => (node, node.SizeBytes / (double)total * area)));

        double x = 0, y = 0, remainingWidth = width, remainingHeight = height;

        while (queue.Count > 0)
        {
            double side = Math.Min(remainingWidth, remainingHeight);
            var row = new List<(UsageNode Node, double Area)> { queue.Dequeue() };

            // Keep adding to the row while doing so makes its rectangles *less* lopsided.
            while (queue.Count > 0 && Worst([.. row, queue.Peek()], side) <= Worst(row, side))
            {
                row.Add(queue.Dequeue());
            }

            double rowArea = row.Sum(item => item.Area);

            if (remainingWidth >= remainingHeight)
            {
                double columnWidth = rowArea / remainingHeight;
                double offsetY = y;

                foreach (var (node, itemArea) in row)
                {
                    double itemHeight = itemArea / columnWidth;
                    placed.Add(new TreemapRect(node, x, offsetY, columnWidth, itemHeight));
                    offsetY += itemHeight;
                }

                x += columnWidth;
                remainingWidth -= columnWidth;
            }
            else
            {
                double rowHeight = rowArea / remainingWidth;
                double offsetX = x;

                foreach (var (node, itemArea) in row)
                {
                    double itemWidth = itemArea / rowHeight;
                    placed.Add(new TreemapRect(node, offsetX, y, itemWidth, rowHeight));
                    offsetX += itemWidth;
                }

                y += rowHeight;
                remainingHeight -= rowHeight;
            }
        }

        return placed;
    }

    /// <summary>
    /// The worst aspect ratio in a row — the measure the algorithm minimizes. Lower is squarer.
    /// </summary>
    private static double Worst(IReadOnlyList<(UsageNode Node, double Area)> row, double side)
    {
        double sum = row.Sum(item => item.Area);
        if (sum <= 0 || side <= 0)
        {
            return double.MaxValue;
        }

        double max = row.Max(item => item.Area);
        double min = row.Min(item => item.Area);

        return Math.Max(side * side * max / (sum * sum), sum * sum / (side * side * min));
    }
}
