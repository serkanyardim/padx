namespace Paddock.Core.Services;

/// <summary>The newest published release, as far as an update check is concerned.</summary>
/// <param name="Version">Release version, from its tag ("v2.1.0" → 2.1.0).</param>
/// <param name="PageUrl">The release page on GitHub.</param>
/// <param name="SetupUrl">Direct download of its installer, when it has one.</param>
public sealed record ReleaseInfo(Version Version, string PageUrl, string? SetupUrl);

/// <summary>Pure helpers for "is there a newer padx?" — the network call lives in the app.</summary>
public static class UpdateCheck
{
    /// <summary>"v2.1.0", "2.1", "v3.0.0-beta" → a comparable version; null if it is not one.</summary>
    public static Version? ParseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return null;

        var text = tag.Trim().TrimStart('v', 'V');
        var end = text.IndexOfAny(['-', '+', ' ']);
        if (end >= 0)
            text = text[..end];

        return Version.TryParse(text, out var version) ? Normalize(version) : null;
    }

    /// <summary>True when <paramref name="latest"/> is a later version than <paramref name="current"/>.</summary>
    public static bool IsNewer(Version latest, Version current) => Normalize(latest) > Normalize(current);

    /// <summary>2.1 and 2.1.0 and 2.1.0.0 are the same release.</summary>
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));
}
