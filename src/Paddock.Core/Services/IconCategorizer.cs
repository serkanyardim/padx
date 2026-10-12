namespace Paddock.Core.Services;

/// <summary>
/// The groups Auto Organize sorts desktop items into, in the order their pads
/// are laid out.
/// </summary>
public enum AutoCategory
{
    /// <summary>Built-in desktop icons (Recycle Bin, user folder, This PC…). Always first.</summary>
    System,
    Folders,
    Documents,
    Media,
    Office,
    Browsers,
    Links,
    Communication,
    Development,
    Remote,
    Design,
    Games,
    Utilities,
    Apps,
    Archives,
    Installers,
    Other,
}

/// <summary>What the categorizer knows about one desktop item.</summary>
/// <param name="Path">Item path (file, folder, shortcut).</param>
/// <param name="IsFolder">True for a directory.</param>
/// <param name="Target">For a shortcut: what it launches or opens (exe, document, folder or URL).</param>
/// <param name="TargetIsFolder">True when the shortcut points at a folder.</param>
public sealed record DesktopItemInfo(string Path, bool IsFolder, string? Target = null, bool TargetIsFolder = false)
{
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
    public string Extension => System.IO.Path.GetExtension(Path);
}

/// <summary>
/// Decides which <see cref="AutoCategory"/> a desktop item belongs to.
///
/// Files go by extension. Shortcuts are judged by what they launch: the target
/// executable's name and folder first, then the shortcut's own name — so
/// "Work Browser.lnk" pointing at chrome.exe still lands in Browsers, and an
/// Office shortcut with no readable target still lands in Office by its name.
/// Pure and deterministic, so it is fully unit-tested.
/// </summary>
public static class IconCategorizer
{
    private static readonly HashSet<string> ShortcutExtensions = Set(".lnk", ".url", ".appref-ms", ".website");
    private static readonly HashSet<string> ExecutableExtensions = Set(".exe", ".bat", ".cmd", ".msc", ".ps1", ".com", ".cpl", ".msi");

    private static readonly HashSet<string> DocumentExtensions = Set(
        ".pdf", ".doc", ".docx", ".docm", ".xls", ".xlsx", ".xlsm", ".ppt", ".pptx", ".txt", ".rtf",
        ".odt", ".ods", ".odp", ".csv", ".md", ".epub", ".one", ".xml", ".json", ".log", ".vsdx");

    private static readonly HashSet<string> MediaExtensions = Set(
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".svg", ".ico", ".tif", ".tiff", ".heic",
        ".raw", ".psd", ".mp3", ".mp4", ".mkv", ".avi", ".mov", ".wav", ".flac", ".m4a", ".m4v",
        ".webm", ".wmv", ".aac", ".ogg", ".opus");

    private static readonly HashSet<string> ArchiveExtensions = Set(
        ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".iso", ".cab", ".tgz");

    private static readonly HashSet<string> CodeExtensions = Set(
        ".py", ".js", ".ts", ".html", ".htm", ".css", ".sql", ".cs", ".java", ".go", ".rs", ".php", ".sh",
        ".ipynb", ".yaml", ".yml", ".sln", ".csproj");

    private static readonly HashSet<string> DesignExtensions = Set(
        ".dwg", ".dxf", ".dgn", ".skp", ".rvt", ".ai", ".fig", ".blend", ".sldprt", ".ncz", ".kml", ".kmz", ".shp");

    /// <summary>
    /// App keywords per category, matched against the target exe name, its
    /// folder and the shortcut name. Checked in this order; first hit wins.
    /// </summary>
    private static readonly (AutoCategory Category, string[] Keywords)[] AppRules =
    [
        (AutoCategory.Games, ["steam", "epicgames", "epic games", "battle.net", "riotclient", "riot client", "gog galaxy",
            "ubisoft", "uplay", "origin", "ea app", "minecraft", "xbox", "roblox", "league of legends", "valorant", "steamapps"]),
        (AutoCategory.Browsers, ["chrome", "msedge", "microsoft edge", "firefox", "opera", "brave", "vivaldi", "iexplore",
            "internet explorer", "tor browser", "arc"]),
        (AutoCategory.Communication, ["teams", "outlook", "slack", "discord", "zoom", "whatsapp", "telegram", "skype",
            "thunderbird", "signal", "webex", "mattermost", "mail"]),
        (AutoCategory.Office, ["winword", "word", "excel", "powerpnt", "powerpoint", "onenote", "msaccess", "access",
            "mspub", "publisher", "visio", "project", "acrobat", "acrord", "foxit", "libreoffice", "soffice",
            "notion", "obsidian", "evernote", "joplin", "pdf"]),
        (AutoCategory.Remote, ["putty", "kitty", "winscp", "filezilla", "anydesk", "teamviewer", "mstsc", "remote desktop",
            "rustdesk", "vnc", "vncviewer", "mobaxterm", "termius", "netscan", "wireshark", "nmap", "advanced ip scanner", "angry ip", "winbox", "openvpn", "forticlient", "globalprotect", "wireguard", "citrix"]),
        (AutoCategory.Development, ["code", "visual studio", "devenv", "idea", "idea64", "pycharm", "rider", "webstorm", "clion",
            "goland", "datagrip", "android studio", "sublime", "notepad++", "atom", "cursor", "git", "github", "githubdesktop", "sourcetree",
            "postman", "insomnia", "docker", "ssms", "sql server", "dbeaver", "heidisql", "navicat", "pgadmin",
            "laragon", "xampp", "wampserver", "powershell", "cmd", "terminal", "wt", "python", "node", "unity", "unreal", "windsurf"]),
        (AutoCategory.Design, ["acad", "autocad", "dwg", "dwgsee", "civil 3d", "civil3d", "revit", "sketchup", "solidworks",
            "netcad", "microstation", "qgis", "arcgis", "global mapper", "photoshop", "illustrator", "indesign",
            "lightroom", "premiere", "after effects", "figma", "gimp", "inkscape", "blender", "krita", "canva",
            "coreldraw", "3ds max", "maya", "rhino", "fusion 360", "affinity"]),
        (AutoCategory.Media, ["vlc", "spotify", "potplayer", "mpc-hc", "mpc-be", "itunes", "foobar", "foobar2000", "winamp", "obs", "obs64",
            "audacity", "handbrake", "kodi", "plex", "media player", "wmplayer", "photos", "irfanview", "xnview",
            "sharex", "greenshot", "snagit", "davinci", "capcut", "netflix", "iptvnator", "iptv"]),
        (AutoCategory.Utilities, ["7-zip", "7zfm", "winrar", "everything", "ccleaner", "cpu-z", "gpu-z", "hwinfo", "hwinfo64",
            "crystaldisk", "fastcopy", "teracopy", "rufus", "control panel", "control", "calc", "calculator", "settings", "task manager",
            "taskmgr", "regedit", "explorer", "notepad", "paint", "mspaint", "snipping", "recycle", "this pc",
            "keepass", "bitwarden", "1password", "lastpass", "powertoys", "autohotkey", "treesize", "windirstat",
            "malwarebytes", "defender", "antivirus", "kaspersky", "eset", "avast", "nvidia", "intel", "realtek",
            "logitech", "dell", "lenovo", "hp"]),
    ];

    /// <summary>Display name of a category, used as the pad title.</summary>
    public static string Title(AutoCategory category) => category switch
    {
        AutoCategory.System => "System",
        AutoCategory.Folders => "Folders",
        AutoCategory.Documents => "Documents",
        AutoCategory.Media => "Media",
        AutoCategory.Office => "Office",
        AutoCategory.Browsers => "Browsers",
        AutoCategory.Links => "Links",
        AutoCategory.Communication => "Communication",
        AutoCategory.Development => "Dev Tools",
        AutoCategory.Remote => "Remote & Network",
        AutoCategory.Design => "Design & CAD",
        AutoCategory.Games => "Games",
        AutoCategory.Utilities => "Utilities",
        AutoCategory.Apps => "Apps",
        AutoCategory.Archives => "Archives",
        AutoCategory.Installers => "Installers",
        _ => "Other",
    };

    public static AutoCategory Categorize(DesktopItemInfo item)
    {
        if (item.IsFolder)
            return AutoCategory.Folders;

        var extension = item.Extension;

        if (ShortcutExtensions.Contains(extension))
            return CategorizeShortcut(item);

        if (ExecutableExtensions.Contains(extension))
            return IsInstaller(item.Name, extension) ? AutoCategory.Installers : MatchApp(item.Name, item.Path) ?? AutoCategory.Apps;

        return ByFileExtension(extension) ?? AutoCategory.Other;
    }

    private static AutoCategory CategorizeShortcut(DesktopItemInfo item)
    {
        var target = item.Target?.Trim() ?? string.Empty;

        // A link to a shell object ("::{CLSID}" — Recycle Bin, This PC…) is a system icon.
        if (target.StartsWith("::{", StringComparison.Ordinal))
            return AutoCategory.System;

        if (item.TargetIsFolder)
            return AutoCategory.Folders;

        // Web shortcuts: .url files, or links whose target is a URL.
        if (IsUrl(target) || string.Equals(item.Extension, ".url", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(item.Extension, ".website", StringComparison.OrdinalIgnoreCase))
        {
            // steam://rungameid/… and friends are games, not web pages.
            if (target.StartsWith("steam:", StringComparison.OrdinalIgnoreCase)
                || target.StartsWith("com.epicgames", StringComparison.OrdinalIgnoreCase)
                || target.Contains("epicgames", StringComparison.OrdinalIgnoreCase))
            {
                return AutoCategory.Games;
            }

            return MatchApp(item.Name, null) is { } named and not AutoCategory.Utilities ? named : AutoCategory.Links;
        }

        if (target.Length > 0)
        {
            var targetExtension = SafeExtension(target);

            // A shortcut to a document, picture or archive belongs with those.
            if (!ExecutableExtensions.Contains(targetExtension) && ByFileExtension(targetExtension) is { } byFile)
                return byFile;

            // No extension and not a program: a folder we could not check (network drive, offline share).
            if (targetExtension.Length == 0)
                return AutoCategory.Folders;
        }

        var targetName = target.Length > 0 ? SafeFileNameWithoutExtension(target) : null;

        return MatchApp(targetName, target)
               ?? MatchApp(item.Name, null)
               ?? AutoCategory.Apps;
    }

    private static AutoCategory? ByFileExtension(string extension)
    {
        if (DocumentExtensions.Contains(extension)) return AutoCategory.Documents;
        if (MediaExtensions.Contains(extension)) return AutoCategory.Media;
        if (ArchiveExtensions.Contains(extension)) return AutoCategory.Archives;
        if (DesignExtensions.Contains(extension)) return AutoCategory.Design;
        if (CodeExtensions.Contains(extension)) return AutoCategory.Development;
        return null;
    }

    /// <summary>
    /// Finds the first category whose keywords appear in the name or path.
    /// Short keywords (≤ 6 chars, e.g. "code", "wt", "origin") must match a
    /// whole word, so "Barcode Reader" is not a code editor and "original.pdf"
    /// is not a game launcher.
    /// </summary>
    private static AutoCategory? MatchApp(string? name, string? path)
    {
        var haystacks = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(name)) haystacks.Add(name.ToLowerInvariant());
        if (!string.IsNullOrWhiteSpace(path)) haystacks.Add(path.ToLowerInvariant());

        if (haystacks.Count == 0)
            return null;

        foreach (var (category, keywords) in AppRules)
        {
            foreach (var keyword in keywords)
            {
                foreach (var text in haystacks)
                {
                    if (keyword.Length <= 6 ? ContainsWord(text, keyword) : text.Contains(keyword, StringComparison.Ordinal))
                        return category;
                }
            }
        }

        return null;
    }

    private static bool ContainsWord(string text, string word)
    {
        var index = 0;
        while ((index = text.IndexOf(word, index, StringComparison.Ordinal)) >= 0)
        {
            var before = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            var end = index + word.Length;
            var after = end >= text.Length || !char.IsLetterOrDigit(text[end]);

            if (before && after)
                return true;

            index = end;
        }

        return false;
    }

    private static bool IsInstaller(string name, string extension)
        => string.Equals(extension, ".msi", StringComparison.OrdinalIgnoreCase)
           || name.Contains("setup", StringComparison.OrdinalIgnoreCase)
           || name.Contains("install", StringComparison.OrdinalIgnoreCase);

    private static bool IsUrl(string target)
        => Uri.TryCreate(target, UriKind.Absolute, out var uri) && !uri.IsFile;

    private static string SafeExtension(string path)
    {
        try { return System.IO.Path.GetExtension(path); }
        catch (ArgumentException) { return string.Empty; }
    }

    private static string? SafeFileNameWithoutExtension(string path)
    {
        try { return System.IO.Path.GetFileNameWithoutExtension(path); }
        catch (ArgumentException) { return null; }
    }

    private static HashSet<string> Set(params string[] values) => new(values, StringComparer.OrdinalIgnoreCase);
}
