using Upkeep.App.Core.Cleanup;

namespace Upkeep.App.Core.Tests.Cleanup;

public class JunkCatalogTests
{
    [Fact]
    public void All_CoversEveryDefinedCategoryExactlyOnce()
    {
        var defined = Enum.GetValues<JunkCategoryId>();

        Assert.Equal(defined.Length, JunkCatalog.All.Count);
        Assert.Equal(defined.Length, JunkCatalog.All.Select(category => category.Id).Distinct().Count());
    }

    [Fact]
    public void Get_EveryCategory_IsRetrievableById()
    {
        foreach (var id in Enum.GetValues<JunkCategoryId>())
        {
            Assert.Equal(id, JunkCatalog.Get(id).Id);
        }
    }

    [Fact]
    public void Get_UnknownCategory_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => JunkCatalog.Get((JunkCategoryId)999));

    [Fact]
    public void SystemScopedCategories_AllRequireElevation()
    {
        // Scope is what decides whether work goes through the elevated helper; the two must never
        // drift apart, or a system path would be handed to the unelevated shell to delete.
        foreach (var category in JunkCatalog.ForScope(JunkScope.System))
        {
            Assert.True(category.RequiresElevation);
        }

        foreach (var category in JunkCatalog.ForScope(JunkScope.User))
        {
            Assert.False(category.RequiresElevation);
        }
    }

    [Fact]
    public void OtherUsersTemp_IsNotSelectedByDefault()
    {
        // Touching another account's files is a different decision than cleaning your own, even
        // when it is only temp files.
        Assert.False(JunkCatalog.Get(JunkCategoryId.OtherUsersTemp).SelectedByDefault);
    }

    [Fact]
    public void IrreversibleCategories_AreExactlyTheOnesWindowsCannotUndo()
    {
        var irreversible = JunkCatalog.All
            .Where(category => category.Removal == RemovalKind.Irreversible)
            .Select(category => category.Id)
            .ToHashSet();

        Assert.Equal([JunkCategoryId.RecycleBin, JunkCategoryId.WindowsUpdateCleanup], irreversible);
    }

    [Fact]
    public void NoJunkCategoryIsQuarantined()
    {
        // Quarantine is for files a user might want back. Caches rebuild themselves, and keeping
        // copies would mean a cleanup frees nothing (ADR-0006).
        Assert.DoesNotContain(JunkCatalog.All, category => category.Removal == RemovalKind.Quarantined);
    }

    [Fact]
    public void ResourceKeys_AreDistinctPerCategory()
    {
        var nameKeys = JunkCatalog.All.Select(category => category.NameKey).ToList();
        var descriptionKeys = JunkCatalog.All.Select(category => category.DescriptionKey).ToList();

        Assert.Equal(nameKeys.Count, nameKeys.Distinct().Count());
        Assert.Equal(descriptionKeys.Count, descriptionKeys.Distinct().Count());
    }
}
