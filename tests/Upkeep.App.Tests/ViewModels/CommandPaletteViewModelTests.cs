using Upkeep.App.Tests.Fakes;
using Upkeep.App.ViewModels;

namespace Upkeep.App.Tests.ViewModels;

public class CommandPaletteViewModelTests
{
    private static readonly CommandPaletteItem[] Items =
    [
        new("Home", "Home"),
        new("Cleanup", "Cleanup"),
        new("Files", "Files"),
        new("Drivers", "Drivers & updates"),
    ];

    private static CommandPaletteViewModel CreateViewModel() => new(new FakeLocalizationService());

    [Fact]
    public void Filter_EmptyQuery_ReturnsEverythingInOrder()
    {
        var result = CommandPaletteViewModel.Filter(Items, string.Empty).ToList();

        Assert.Equal(Items, result);
    }

    [Fact]
    public void Filter_WhitespaceQuery_ReturnsEverything()
    {
        // A query the user hasn't really typed anything into yet shouldn't read as "no matches".
        var result = CommandPaletteViewModel.Filter(Items, "   ").ToList();

        Assert.Equal(Items, result);
    }

    [Theory]
    [InlineData("file")]
    [InlineData("FILE")]
    [InlineData("Files")]
    public void Filter_IsCaseInsensitiveSubstringMatch(string query)
    {
        var result = CommandPaletteViewModel.Filter(Items, query).ToList();

        Assert.Single(result);
        Assert.Equal("Files", result[0].Tag);
    }

    [Fact]
    public void Filter_MatchesPartOfATitleWithMoreThanOneWord()
    {
        var result = CommandPaletteViewModel.Filter(Items, "update").ToList();

        Assert.Single(result);
        Assert.Equal("Drivers", result[0].Tag);
    }

    [Fact]
    public void Filter_NothingMatches_ReturnsEmpty()
    {
        var result = CommandPaletteViewModel.Filter(Items, "nonexistent section").ToList();

        Assert.Empty(result);
    }

    [Fact]
    public void Constructor_LoadsEveryNavSection()
    {
        var viewModel = CreateViewModel();

        Assert.Equal(8, viewModel.Results.Count);
        Assert.False(viewModel.HasNoResults);
    }

    [Fact]
    public void QueryChanged_FiltersResultsLive()
    {
        var viewModel = CreateViewModel();

        // FakeLocalizationService echoes the key, so "NavFiles.Content" is what a title reads as.
        viewModel.Query = "NavFiles";

        Assert.Single(viewModel.Results);
        Assert.Equal("Files", viewModel.Results[0].Tag);
    }

    [Fact]
    public void QueryChanged_NothingMatches_SetsHasNoResults()
    {
        var viewModel = CreateViewModel();

        viewModel.Query = "does not exist anywhere";

        Assert.Empty(viewModel.Results);
        Assert.True(viewModel.HasNoResults);
    }

    [Fact]
    public void Reset_ClearsTheQueryAndShowsEverythingAgain()
    {
        var viewModel = CreateViewModel();
        viewModel.Query = "NavFiles";

        viewModel.Reset();

        Assert.Equal(string.Empty, viewModel.Query);
        Assert.Equal(8, viewModel.Results.Count);
    }
}
