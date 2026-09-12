using Upkeep.App.ViewModels;

namespace Upkeep.App.Tests.ViewModels;

public class HomeViewModelTests
{
    [Theory]
    [InlineData(5, "HomeGreetingMorning")]
    [InlineData(11, "HomeGreetingMorning")]
    [InlineData(12, "HomeGreetingAfternoon")]
    [InlineData(17, "HomeGreetingAfternoon")]
    [InlineData(18, "HomeGreetingEvening")]
    [InlineData(22, "HomeGreetingEvening")]
    [InlineData(23, "HomeGreetingNight")]
    [InlineData(0, "HomeGreetingNight")]
    [InlineData(4, "HomeGreetingNight")]
    public void GreetingKeyFor_PicksTheGreetingForThatHour(int hour, string expectedKey) =>
        Assert.Equal(expectedKey, HomeViewModel.GreetingKeyFor(hour));

    [Fact]
    public void GreetingKeyFor_CoversEveryHourOfTheDay()
    {
        // No hour may fall through to an empty greeting on the first screen of the app.
        for (int hour = 0; hour < 24; hour++)
        {
            Assert.False(string.IsNullOrEmpty(HomeViewModel.GreetingKeyFor(hour)));
        }
    }
}
