using Upkeep.App.Core.Files;

namespace Upkeep.App.Core.Tests.Files;

public class TreemapLayoutTests
{
    private static UsageNode Node(string name, long size) => new(name, $@"C:\{name}", size, IsDirectory: true);

    [Fact]
    public void Squarify_PlacesEveryNodeWithSize()
    {
        UsageNode[] nodes = [Node("AppData", 40), Node("Videos", 20), Node("Pictures", 10)];

        var rects = TreemapLayout.Squarify(nodes, 400, 300);

        Assert.Equal(3, rects.Count);
        Assert.All(rects, rect => Assert.True(rect.Width > 0 && rect.Height > 0));
    }

    [Fact]
    public void Squarify_AreasAreProportionalToSize()
    {
        UsageNode[] nodes = [Node("big", 75), Node("small", 25)];

        var rects = TreemapLayout.Squarify(nodes, 200, 200);

        double bigArea = rects.Single(rect => rect.Node.Name == "big").Width * rects.Single(rect => rect.Node.Name == "big").Height;
        double smallArea = rects.Single(rect => rect.Node.Name == "small").Width * rects.Single(rect => rect.Node.Name == "small").Height;

        Assert.Equal(3, bigArea / smallArea, precision: 2);
    }

    [Fact]
    public void Squarify_FillsTheCanvasItWasGiven()
    {
        UsageNode[] nodes = [Node("a", 50), Node("b", 30), Node("c", 20)];

        var rects = TreemapLayout.Squarify(nodes, 300, 200);
        double covered = rects.Sum(rect => rect.Width * rect.Height);

        Assert.Equal(300 * 200, covered, precision: 1);
    }

    [Fact]
    public void Squarify_RectanglesDoNotEscapeTheCanvas()
    {
        UsageNode[] nodes = [Node("a", 60), Node("b", 25), Node("c", 10), Node("d", 5)];

        var rects = TreemapLayout.Squarify(nodes, 500, 400);

        Assert.All(rects, rect =>
        {
            Assert.True(rect.X >= -0.001 && rect.Y >= -0.001);
            Assert.True(rect.X + rect.Width <= 500.001);
            Assert.True(rect.Y + rect.Height <= 400.001);
        });
    }

    [Fact]
    public void Squarify_KeepsRectanglesCloseToSquare()
    {
        // The whole point of the squarified algorithm: long thin slivers can't be compared by eye
        // or clicked reliably.
        UsageNode[] nodes = [.. Enumerable.Range(1, 8).Select(index => Node($"n{index}", index * 10))];

        var rects = TreemapLayout.Squarify(nodes, 600, 400);

        Assert.All(rects, rect =>
        {
            double ratio = Math.Max(rect.Width / rect.Height, rect.Height / rect.Width);
            Assert.True(ratio < 8, $"{rect.Node.Name} came out {ratio:0.0}:1");
        });
    }

    [Fact]
    public void Squarify_DropsZeroSizedNodesInsteadOfDrawingInvisibleSlivers()
    {
        UsageNode[] nodes = [Node("real", 100), Node("empty", 0)];

        var rects = TreemapLayout.Squarify(nodes, 200, 200);

        Assert.Single(rects);
        Assert.Equal("real", rects[0].Node.Name);
    }

    [Fact]
    public void Squarify_NothingToShow_IsAnEmptyLayout()
    {
        Assert.Empty(TreemapLayout.Squarify([], 200, 200));
        Assert.Empty(TreemapLayout.Squarify([Node("a", 0)], 200, 200));
    }

    [Theory]
    [InlineData(0, 200)]
    [InlineData(200, 0)]
    [InlineData(-10, 200)]
    public void Squarify_NoCanvasToDrawOn_IsEmpty(double width, double height) =>
        Assert.Empty(TreemapLayout.Squarify([Node("a", 100)], width, height));

    [Fact]
    public void Squarify_SingleNode_TakesTheWholeCanvas()
    {
        var rects = TreemapLayout.Squarify([Node("only", 42)], 120, 80);

        var rect = Assert.Single(rects);
        Assert.Equal(120, rect.Width, precision: 3);
        Assert.Equal(80, rect.Height, precision: 3);
    }
}
