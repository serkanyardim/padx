using Paddock.Core.Services;

namespace Paddock.Core.Tests;

public class WebSearchTests
{
    [Theory]
    [InlineData("TR", "google.com.tr")]
    [InlineData("tr", "google.com.tr")]
    [InlineData("GB", "google.co.uk")]
    [InlineData("JP", "google.co.jp")]
    [InlineData("DE", "google.de")]
    [InlineData("US", "google.com")]
    [InlineData("", "google.com")]
    [InlineData(null, "google.com")]
    public void GoogleDomain_FollowsTheCountry(string? country, string expected)
        => Assert.Equal(expected, WebSearch.GoogleDomain(country));

    [Fact]
    public void GoogleUrl_EscapesTheQuery_AndAsksForLocalResults()
        => Assert.Equal(
            "https://www.google.com.tr/search?q=%C4%B1%C5%9F%C4%B1k%20%26%20g%C3%B6lge&gl=tr",
            WebSearch.GoogleUrl(" ışık & gölge ", "TR"));

    [Fact]
    public void GoogleUrl_WithoutACountry_UsesGoogleCom()
        => Assert.Equal("https://www.google.com/search?q=padx", WebSearch.GoogleUrl("padx", null));
}
