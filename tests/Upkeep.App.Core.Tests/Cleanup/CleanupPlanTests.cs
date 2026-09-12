using Upkeep.App.Core.Cleanup;

namespace Upkeep.App.Core.Tests.Cleanup;

public class CleanupPlanTests
{
    private static JunkCategoryScan Scan(JunkCategoryId id, long bytes = 1024, int items = 2) =>
        new(id, [.. Enumerable.Range(0, items).Select(index => new JunkItem($@"C:\temp\{id}-{index}", bytes / Math.Max(items, 1)))]);

    [Fact]
    public void From_KeepsOnlySelectedCategories()
    {
        var scans = new[] { Scan(JunkCategoryId.UserTemp), Scan(JunkCategoryId.ShaderCache) };

        var plan = CleanupPlan.From(scans, new HashSet<JunkCategoryId> { JunkCategoryId.UserTemp });

        Assert.Single(plan.Categories);
        Assert.Equal(JunkCategoryId.UserTemp, plan.Categories[0].Scan.CategoryId);
    }

    [Fact]
    public void From_DropsCategoriesWithNothingToDo()
    {
        // A plan should contain work, not a list of things that turned out to be empty.
        var scans = new[] { Scan(JunkCategoryId.UserTemp), JunkCategoryScan.Empty(JunkCategoryId.ShaderCache) };

        var plan = CleanupPlan.From(scans, new HashSet<JunkCategoryId> { JunkCategoryId.UserTemp, JunkCategoryId.ShaderCache });

        Assert.Single(plan.Categories);
    }

    [Fact]
    public void From_PutsUserScopedWorkBeforeSystemWork()
    {
        // Everything that needs no elevation happens first, so a declined UAC prompt still leaves
        // a run that did most of what was asked.
        var scans = new[] { Scan(JunkCategoryId.WindowsTemp), Scan(JunkCategoryId.UserTemp) };

        var plan = CleanupPlan.From(scans, new HashSet<JunkCategoryId>(scans.Select(scan => scan.CategoryId)));

        Assert.Equal(JunkScope.User, plan.Categories[0].Category.Scope);
        Assert.Equal(JunkScope.System, plan.Categories[1].Category.Scope);
    }

    [Fact]
    public void EstimatedBytes_SumsEveryChosenCategory()
    {
        var scans = new[] { Scan(JunkCategoryId.UserTemp, 1000, 2), Scan(JunkCategoryId.ShaderCache, 500, 1) };

        var plan = CleanupPlan.From(scans, new HashSet<JunkCategoryId>(scans.Select(scan => scan.CategoryId)));

        Assert.Equal(1500, plan.EstimatedBytes);
        Assert.Equal(3, plan.ItemCount);
    }

    [Fact]
    public void RequiresElevation_OnlyWhenSomethingMachineWideWasChosen()
    {
        var userOnly = CleanupPlan.From([Scan(JunkCategoryId.UserTemp)], new HashSet<JunkCategoryId> { JunkCategoryId.UserTemp });
        var withSystem = CleanupPlan.From([Scan(JunkCategoryId.WindowsTemp)], new HashSet<JunkCategoryId> { JunkCategoryId.WindowsTemp });

        Assert.False(userOnly.RequiresElevation);
        Assert.True(withSystem.RequiresElevation);
    }

    [Fact]
    public void HasIrreversibleWork_IsTrueForTheRecycleBin()
    {
        var plan = CleanupPlan.From(
            [new JunkCategoryScan(JunkCategoryId.RecycleBin, []) { ReportedBytes = 2048, ReportedItemCount = 12 }],
            new HashSet<JunkCategoryId> { JunkCategoryId.RecycleBin });

        Assert.True(plan.HasIrreversibleWork);
        Assert.Equal(2048, plan.EstimatedBytes);
    }

    [Fact]
    public void NeedsRestorePoint_ForSystemOrIrreversibleWorkOnly()
    {
        // A user-scope cache clean has nothing a restore point would help with; taking one anyway
        // would cost a minute and a chunk of disk for no benefit.
        var caches = CleanupPlan.From([Scan(JunkCategoryId.ShaderCache)], new HashSet<JunkCategoryId> { JunkCategoryId.ShaderCache });
        var system = CleanupPlan.From([Scan(JunkCategoryId.WindowsTemp)], new HashSet<JunkCategoryId> { JunkCategoryId.WindowsTemp });

        Assert.False(caches.NeedsRestorePoint);
        Assert.True(system.NeedsRestorePoint);
    }

    [Fact]
    public void From_NothingSelected_IsAnEmptyPlan()
    {
        var plan = CleanupPlan.From([Scan(JunkCategoryId.UserTemp)], new HashSet<JunkCategoryId>());

        Assert.True(plan.IsEmpty);
        Assert.False(plan.RequiresElevation);
        Assert.False(plan.NeedsRestorePoint);
    }

    [Fact]
    public void ForScope_SplitsWorkTheWayTheExecutorRunsIt()
    {
        var scans = new[] { Scan(JunkCategoryId.UserTemp), Scan(JunkCategoryId.WindowsTemp), Scan(JunkCategoryId.ShaderCache) };

        var plan = CleanupPlan.From(scans, new HashSet<JunkCategoryId>(scans.Select(scan => scan.CategoryId)));

        Assert.Equal(2, plan.ForScope(JunkScope.User).Count());
        Assert.Single(plan.ForScope(JunkScope.System));
    }
}
