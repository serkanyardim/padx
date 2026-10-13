using System.Globalization;
using System.Text;

namespace Paddock.Core.Services;

/// <summary>One thing the desktop search can find: an item in a pad, or one still on the desktop.</summary>
/// <param name="Path">Where the item lives on disk.</param>
/// <param name="Name">The name shown for it, as on the desktop (no shortcut extension).</param>
/// <param name="PadId">The pad holding it, or null for an item on the desktop itself.</param>
/// <param name="Group">The pad's title, or <see cref="DesktopSearch.DesktopGroup"/>.</param>
public sealed record SearchItem(string Path, string Name, string? PadId, string Group);

/// <summary>
/// Search-as-you-type over desktop items. Matching ignores case and Turkish
/// letters (ı/İ/i, ş/s, ğ/g…), so "isik" finds "Işık" and "WORD" finds "Word".
/// </summary>
public static class DesktopSearch
{
    /// <summary>The group shown for items that are on the desktop, not in a pad.</summary>
    public const string DesktopGroup = "DESKTOP";

    /// <summary>
    /// The items matching <paramref name="query"/>, ordered by pad name and
    /// then item name; items on the desktop itself come last. An empty query
    /// matches nothing.
    /// </summary>
    public static IReadOnlyList<SearchItem> Find(IEnumerable<SearchItem> items, string? query)
    {
        var folded = Fold(query ?? string.Empty).Trim();
        if (folded.Length == 0)
            return [];

        var words = folded.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var order = StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true);

        return items
            .Where(item => Matches(Fold(item.Name), words))
            .OrderBy(item => item.PadId is null)
            .ThenBy(item => item.Group, order)
            .ThenBy(item => item.Name, order)
            .ToList();
    }

    /// <summary>
    /// Every word of the query appears in the name ("chr goo" finds Google
    /// Chrome), or a single word spells the name's initials ("gc").
    /// </summary>
    private static bool Matches(string name, string[] words)
    {
        if (words.All(name.Contains))
            return true;

        return words.Length == 1 && words[0].Length >= 2 && Initials(name).StartsWith(words[0], StringComparison.Ordinal);
    }

    private static string Initials(string name)
    {
        var initials = new StringBuilder();
        var atStart = true;

        foreach (var c in name)
        {
            var isWordChar = char.IsLetterOrDigit(c);
            if (isWordChar && atStart)
                initials.Append(c);

            atStart = !isWordChar;
        }

        return initials.ToString();
    }

    /// <summary>Lower case, Turkish letters to their plain Latin look-alikes.</summary>
    public static string Fold(string text)
    {
        var folded = new StringBuilder(text.Length);

        foreach (var c in text)
        {
            folded.Append(c switch
            {
                'ı' or 'I' or 'İ' or 'i' => 'i',
                'ş' or 'Ş' => 's',
                'ğ' or 'Ğ' => 'g',
                'ç' or 'Ç' => 'c',
                'ö' or 'Ö' => 'o',
                'ü' or 'Ü' => 'u',
                _ => char.ToLowerInvariant(c),
            });
        }

        return folded.ToString();
    }
}
