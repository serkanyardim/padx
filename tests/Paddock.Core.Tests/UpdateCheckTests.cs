using Paddock.Core.Services;

namespace Paddock.Core.Tests;

public class UpdateCheckTests
{
    [Theory]
    [InlineData("v2.1.0", "2.1.0")]
    [InlineData("2.1", "2.1.0")]
    [InlineData("V3.0.2", "3.0.2")]
    [InlineData("v3.0.0-beta", "3.0.0")]
    public void ParseTag_ReadsReleaseTags(string tag, string expected)
        => Assert.Equal(Version.Parse(expected), UpdateCheck.ParseTag(tag));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    public void ParseTag_RejectsNonVersions(string? tag)
        => Assert.Null(UpdateCheck.ParseTag(tag));

    [Theory]
    [InlineData("2.1.0", "2.0.0", true)]
    [InlineData("2.0.1", "2.0.0", true)]
    [InlineData("2.0.0", "2.0.0.0", false)]
    [InlineData("2.0.0", "2.1.0", false)]
    [InlineData("10.0.0", "9.9.9", true)]
    public void IsNewer_ComparesVersionsNotText(string latest, string current, bool newer)
        => Assert.Equal(newer, UpdateCheck.IsNewer(Version.Parse(latest), Version.Parse(current)));
}
