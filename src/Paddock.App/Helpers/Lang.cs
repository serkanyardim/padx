using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Paddock.Core.Services;

namespace Paddock.App.Helpers;

/// <summary>
/// Translations. padx is written in English; a language is a JSON file of
/// "English text": "translation" pairs — lang/tr.json — so anything without
/// a translation simply stays English.
///
/// Windows and menus are translated as they open, by walking their controls
/// and swapping fixed texts; text that code builds goes through <see cref="T"/>
/// and <see cref="F"/>. Languages ship inside padx, and a file dropped in a
/// "lang" folder next to padx.exe adds or overrides one.
/// </summary>
internal static class Lang
{
    /// <summary>The language padx is written in.</summary>
    public const string English = "en";

    private static Dictionary<string, string> _texts = new();

    /// <summary>The language in use, e.g. "tr".</summary>
    public static string Code { get; private set; } = English;

    /// <summary>The setting that follows Windows.</summary>
    public const string Auto = "auto";

    /// <summary>
    /// What "auto" means on this PC: a language padx has for Windows' display
    /// language (Turkish → "tr"), otherwise English.
    /// </summary>
    public static string ForWindows()
    {
        var windows = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return Available().Any(l => l.Code == windows) ? windows : English;
    }

    /// <summary>Switches to a language ("auto" follows Windows); English, or an unknown code, clears every translation.</summary>
    public static void Load(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || code == Auto)
            code = ForWindows();

        Code = English;
        _texts = new Dictionary<string, string>();

        if (string.IsNullOrWhiteSpace(code) || code == English || Read(code) is not { } texts)
            return;

        Code = code;
        _texts = texts;
        Log.Info($"Language: {code} ({texts.Count} texts).");
    }

    /// <summary>Every language available: code and its own name ("Türkçe").</summary>
    public static IReadOnlyList<(string Code, string Name)> Available()
    {
        var codes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var resource in typeof(Lang).Assembly.GetManifestResourceNames())
        {
            if (resource.StartsWith(ResourcePrefix, StringComparison.Ordinal) && resource.EndsWith(".json", StringComparison.Ordinal))
                codes.Add(resource[ResourcePrefix.Length..^".json".Length]);
        }

        try
        {
            if (Directory.Exists(Folder))
            {
                foreach (var file in Directory.EnumerateFiles(Folder, "*.json"))
                    codes.Add(Path.GetFileNameWithoutExtension(file));
            }
        }
        catch (Exception)
        {
            // An unreadable folder just adds nothing.
        }

        var languages = new List<(string, string)> { (English, "English") };
        foreach (var code in codes.Where(c => c != English))
            languages.Add((code, Read(code)?.GetValueOrDefault("_language") ?? code.ToUpperInvariant()));

        return languages;
    }

    /// <summary>The translation of an English text, or the text itself. Surrounding spaces are kept.</summary>
    public static string T(string english)
    {
        if (_texts.Count == 0 || string.IsNullOrEmpty(english))
            return english;

        if (_texts.TryGetValue(english, out var exact) && exact.Length > 0)
            return exact;

        var trimmed = english.Trim();
        if (trimmed.Length == english.Length || !_texts.TryGetValue(trimmed, out var inner) || inner.Length == 0)
            return english;

        var start = english.IndexOf(trimmed, StringComparison.Ordinal);
        return english[..start] + inner + english[(start + trimmed.Length)..];
    }

    /// <summary>Upper case the way the language writes it (Turkish: i → İ).</summary>
    public static string Upper(string text)
    {
        try
        {
            return text.ToUpper(System.Globalization.CultureInfo.GetCultureInfo(Code));
        }
        catch (System.Globalization.CultureNotFoundException)
        {
            return text.ToUpperInvariant();
        }
    }

    /// <summary>Translates a format ("Moved {0} item(s)…") and fills it in.</summary>
    public static string F(string englishFormat, params object?[] args)
    {
        try
        {
            return string.Format(T(englishFormat), args);
        }
        catch (FormatException)
        {
            // A broken translation must not break the app.
            return string.Format(englishFormat, args);
        }
    }

    /// <summary>Translates every window and context menu as it appears.</summary>
    public static void Install()
    {
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => Translate((DependencyObject)sender)));
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent,
            new RoutedEventHandler((sender, _) => Translate((DependencyObject)sender)));
    }

    /// <summary>
    /// Swaps the fixed texts of a window or menu for their translations. Bound
    /// texts are left alone (their source translates them). Walks the logical
    /// tree, so tabs not shown yet are covered too.
    /// </summary>
    public static void Translate(DependencyObject root)
    {
        if (_texts.Count == 0)
            return;

        // Copies: changing a text changes the very collections being walked.
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>().ToList())
            Translate(child);

        switch (root)
        {
            case TextBlock text when !System.Windows.Data.BindingOperations.IsDataBound(text, TextBlock.TextProperty):
                foreach (var run in text.Inlines.OfType<Run>().ToList())
                    TranslateText(run, Run.TextProperty);
                break;

            case Window window:
                TranslateText(window, Window.TitleProperty);
                break;
        }

        if (root is ContentControl content)
            TranslateText(content, ContentControl.ContentProperty);

        if (root is HeaderedContentControl headered)
            TranslateText(headered, HeaderedContentControl.HeaderProperty);

        if (root is HeaderedItemsControl menu)
            TranslateText(menu, HeaderedItemsControl.HeaderProperty);

        if (root is MenuItem item)
            TranslateText(item, MenuItem.InputGestureTextProperty);

        if (root is FrameworkElement element)
            TranslateText(element, FrameworkElement.ToolTipProperty);
    }

    private static void TranslateText(DependencyObject target, DependencyProperty property)
    {
        if (target.GetValue(property) is not string text || System.Windows.Data.BindingOperations.IsDataBound(target, property))
            return;

        var translated = T(text);
        if (ReferenceEquals(translated, text))
            return;

        try
        {
            target.SetValue(property, translated);
        }
        catch (Exception ex)
        {
            // One text that cannot be set must not leave the rest of the window untranslated.
            Log.Warn($"Could not translate \"{text}\": {ex.Message}");
        }
    }

    private const string ResourcePrefix = "Paddock.App.lang.";

    private static string Folder => Path.Combine(AppContext.BaseDirectory, "lang");

    /// <summary>A language file: next to padx.exe if there is one, else the one built in.</summary>
    private static Dictionary<string, string>? Read(string code)
    {
        try
        {
            var file = Path.Combine(Folder, code + ".json");
            if (File.Exists(file))
                return Parse(File.ReadAllText(file));

            using var stream = typeof(Lang).Assembly.GetManifestResourceStream(ResourcePrefix + code + ".json");
            if (stream is null)
                return null;

            using var reader = new StreamReader(stream);
            return Parse(reader.ReadToEnd());
        }
        catch (Exception ex)
        {
            Log.Warn($"Language '{code}' could not be read: {ex.Message}");
            return null;
        }
    }

    private static Dictionary<string, string> Parse(string json)
        => JsonSerializer.Deserialize<Dictionary<string, string>>(json, new JsonSerializerOptions
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        }) ?? new Dictionary<string, string>();
}
