using Upkeep.App.Core.Updates;

namespace Upkeep.App.Core.Tests.Updates;

public class SemanticVersionComparerTests
{
    [Theory]
    [InlineData("1.0.0", "v1.0.1")]
    [InlineData("1.0.0", "v1.1.0")]
    [InlineData("1.9.9", "v2.0.0")]
    [InlineData("1.0.0.0", "1.0.1")]
    public void IsNewer_CandidateAhead_ReturnsTrue(string current, string candidate) =>
        Assert.True(SemanticVersionComparer.IsNewer(current, candidate));

    [Theory]
    [InlineData("1.2.3", "v1.2.3")]
    [InlineData("2.0.0", "v1.9.9")]
    [InlineData("1.0.1", "v1.0.0")]
    public void IsNewer_CandidateNotAhead_ReturnsFalse(string current, string candidate) =>
        Assert.False(SemanticVersionComparer.IsNewer(current, candidate));

    [Fact]
    public void IsNewer_FourPartAssemblyVersionAgainstMatchingTag_IsNotNewer()
    {
        // MSBuild pads assembly versions to X.Y.Z.0, and System.Version sorts an unspecified
        // component lower — without normalization this reported an update for the running build.
        Assert.False(SemanticVersionComparer.IsNewer("1.4.2.0", "v1.4.2"));
    }

    [Theory]
    [InlineData("not-a-version", "v1.0.0")]
    [InlineData("1.0.0", "latest")]
    [InlineData("", "v1.0.0")]
    [InlineData("1.0.0", "")]
    public void IsNewer_UnparsableInput_ReturnsFalse(string current, string candidate)
    {
        // A malformed tag from the API must read as "no update", never as one.
        Assert.False(SemanticVersionComparer.IsNewer(current, candidate));
    }

    [Fact]
    public void IsNewer_UppercaseVPrefix_IsAccepted() =>
        Assert.True(SemanticVersionComparer.IsNewer("1.0.0", "V1.1.0"));
}
