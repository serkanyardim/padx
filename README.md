# padx

**Desktop, organized.** A free, open-source desktop icon organizer for Windows 10/11 —
pads that group your icons, sorted for you, styled the way you like.

> padx is based on [Paddock](https://github.com/HappyDane/Paddock) by HappyDane (MIT),
> and has grown well beyond it.

---

## Features

**Pads** — Group desktop icons into pads that live on the desktop: above the wallpaper,
always below your apps, never stealing focus. Move by the title bar, resize from the corner,
double-click the title to roll a pad up. Win+D (show desktop) keeps them visible.

**Auto Organize** — One click sorts the whole desktop into category pads: System, Folders,
Documents, Media, Office, Internet, Communication, Dev Tools, IT & Network, Design & CAD,
Games, Utilities, Installers… Pads are sized to fit their icons exactly and laid out as a
tidy table — aligned rows, aligned columns, even gaps. Locked pads are left alone.
An **Accept / Revert** bar lets you undo the whole thing, file by file.

**Smart categories, fully offline** — Shortcuts are judged by what they launch, not just
their name: the target program, its product and company info, the real target of MSI
"advertised" shortcuts, and game-engine detection (Unity, Unreal, Steam) for games no list
could know. Built-in icons (This PC, Recycle Bin, your user folder) get their own System pad;
all-users desktop shortcuts are included with one administrator prompt.

**Look** — A minimalist "digital" style throughout. Per-pad colours, plus **Glass** and
**Frost** materials with real blur-behind, adjustable tint and shine. Global opacity, corner
radius, edge colour and thickness, title bar colour/opacity/font/size/alignment, icon size,
desktop-style label shadows, an optional resize grip — all with a live preview.

**Configs** — Save the whole setup (settings, pads, and what is in each pad) under a name,
load it back any time, or have one load at every startup.

**Everyday comfort** — Ctrl + drag on empty desktop draws a new pad. The × hides a pad
(bring hidden pads back from the tray or with a hotkey). Rename items inside pads. Restore
everything to the desktop, sorted by type, in one step. Configurable global hotkeys.

## How icons work

Windows only draws the top level of your Desktop folder, so the only reliable way to take
an icon off the desktop is to move the file. padx does exactly that, and keeps it close:

- Each pad owns a hidden folder at `%USERPROFILE%\Desktop\.Paddock\<pad-id>`.
- Dropping an item into a pad **moves** it there; dragging it out or deleting the pad moves
  it **straight back** to the desktop. Nothing is ever deleted, no name is ever overwritten.
- Items from elsewhere (a folder in Explorer) are only referenced, never moved.

## Getting started

**Requirements:** Windows 10 (1903+) or Windows 11, [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
git clone https://github.com/serkanyardim/padx.git
cd padx
dotnet build
dotnet run --project src/Paddock.App
```

Single self-contained exe:

```bash
dotnet publish src/Paddock.App -c Release -p:PublishProfile=win-x64
```

padx lives in the system tray: right-click it for **New Pad**, **Auto Organize**, **Config**,
**Settings** and **About**.

## Default shortcuts

| Action | Keys |
|---|---|
| Show / hide pads | `Ctrl+F12` |
| New pad | `Ctrl+Alt+N` |
| Roll up / expand all pads | `Ctrl+Alt+R` |
| Show hidden pads | `Ctrl+Alt+H` |
| Open settings | `Ctrl+Alt+P` |
| Draw a pad on the desktop | `Ctrl` + drag on empty desktop |

All of them can be changed in **Settings → Shortcuts**.

## Where things are kept

- Settings, log and saved configs: `%AppData%\Paddock\`
- Pad contents: `%USERPROFILE%\Desktop\.Paddock\` (hidden)

## Project structure

```
src/
  Paddock.App/        WPF application (views, view models, styles)
  Paddock.Core/       Core logic, no UI: models, categorizer, layout planner, configs
  Paddock.Shell/      Windows integration: P/Invoke, COM, shell, hooks
tests/
  Paddock.Core.Tests/ Unit tests (xUnit)
```

The internal project and folder names still say *Paddock*; keeping them means existing
settings, configs and pad contents carry over untouched.

## Credits

- **padx** — developed by axzelx.
- Based on **[Paddock](https://github.com/HappyDane/Paddock)** by HappyDane, released under the MIT license.

## License

MIT — see [LICENSE](LICENSE).
