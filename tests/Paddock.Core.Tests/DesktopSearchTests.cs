using Paddock.Core.Services;

namespace Paddock.Core.Tests;

public class DesktopSearchTests
{
    private static readonly SearchItem Chrome = new(@"C:\s\a\Google Chrome.lnk", "Google Chrome", "a", "INTERNET");
    private static readonly SearchItem Earth = new(@"C:\s\b\Google Earth Pro.lnk", "Google Earth Pro", "b", "DESIGN & CAD");
    private static readonly SearchItem Drive = new(@"C:\d\Google Drive.lnk", "Google Drive", null, DesktopSearch.DesktopGroup);
    private static readonly SearchItem Word = new(@"C:\s\c\Word.lnk", "Word", "c", "OFFICE");
    private static readonly SearchItem Isik = new(@"C:\s\c\Işık Raporu.docx", "Işık Raporu", "c", "OFFICE");

    private static readonly SearchItem[] All = [Word, Drive, Earth, Chrome, Isik];

    [Fact]
    public void Results_AreOrderedByPadThenName_WithDesktopItemsLast()
    {
        var results = DesktopSearch.Find(All, "google");

        Assert.Equal([Earth, Chrome, Drive], results);
    }

    [Theory]
    [InlineData("WORD")]
    [InlineData("wor")]
    public void Matching_IgnoresCase(string query)
        => Assert.Equal([Word], DesktopSearch.Find(All, query));

    [Theory]
    [InlineData("isik")]
    [InlineData("IŞIK")]
    [InlineData("ışık rap")]
    public void Matching_IgnoresTurkishLetters(string query)
        => Assert.Equal([Isik], DesktopSearch.Find(All, query));

    [Fact]
    public void EveryWordMustAppear_InAnyOrder()
        => Assert.Equal([Chrome], DesktopSearch.Find(All, "chr goo"));

    [Fact]
    public void Initials_FindAnItem()
        => Assert.Equal([Earth], DesktopSearch.Find(All, "gep"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void EmptyQuery_FindsNothing(string? query)
        => Assert.Empty(DesktopSearch.Find(All, query));
}
