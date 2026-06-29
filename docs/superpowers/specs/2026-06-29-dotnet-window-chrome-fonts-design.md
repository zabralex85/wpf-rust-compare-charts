# .NET Window Chrome + IBM Plex Fonts — Design Spec

**Date:** 2026-06-29
**Status:** Approved (user-directed)

## Goal

Bring the .NET app to visual parity with the Rust app's window: a **frameless
window** with custom min / maximize / close controls in the header (mirroring the
Rust `feat/window-chrome`), and the real **IBM Plex** fonts (Mono + Sans)
replacing the Segoe/Consolas fallback. Native WPF, no new behavior beyond the
window chrome.

## Window chrome (frameless)

- `MainWindow`: `WindowStyle="None"` + a `System.Windows.Shell.WindowChrome`
  (`CaptionHeight` = the top-bar height so the bar is the drag region;
  `ResizeBorderThickness` so edges still resize; `GlassFrameThickness=0`,
  `CornerRadius=0`). This keeps native resize / Aero-snap / maximize while the
  XAML draws the bar.
- Custom controls in `TopBar` (right side): minimize `⎯`, maximize/restore `▢`,
  close `✕`. Click handlers in code-behind resolve `Window.GetWindow(this)` and
  call `WindowState = Minimized` / toggle `Maximized↔Normal` / `Close()`.
- The control buttons are marked `WindowChrome.IsHitTestVisibleInChrome="True"`
  so they receive clicks inside the caption drag region.
- Maximized inset: a maximized `WindowStyle=None` window overshoots the work area
  by the resize border; handle it (a small root `Margin` toggled on
  `StateChanged`, or `WindowChrome` defaults) so content isn't clipped under the
  taskbar.

## IBM Plex fonts

- Bundle TrueType files in `dotnet/src/TelemetryPoc.App/Fonts/` (committed,
  ~1–2 MB total): `IBMPlexMono-Regular.ttf`, `IBMPlexMono-SemiBold.ttf`,
  `IBMPlexSans-Regular.ttf`, `IBMPlexSans-SemiBold.ttf` — sourced from the IBM/plex
  repo (`packages/plex-{mono,sans}/fonts/complete/ttf/`).
- Add them to the csproj as `<Resource>` items.
- `Theme.xaml`: point `MonoFont`/`SansFont` `FontFamily` at the bundled families
  via a pack URI with a font-fallback chain, e.g.
  `MonoFont = "pack://application:,,,/Fonts/#IBM Plex Mono, Consolas"` and
  `SansFont = "pack://application:,,,/Fonts/#IBM Plex Sans, Segoe UI"` (so a
  missing-glyph or load failure still falls back gracefully).

## Components

`Theme.xaml` (font resources) · `Fonts/*.ttf` (bundled) · `MainWindow.xaml`
(+ `.xaml.cs`: WindowChrome + button handlers + StateChanged inset) ·
`TopBar.xaml` (window control buttons + `.win-btn`-style) · `TelemetryPoc.App.csproj`
(`<Resource>` font entries).

## Testing

XAML / window / fonts are **build-verified + confirmed by launching the app**
(no headless verification). There is no new pure logic to unit-test. Live check:
the window has no OS title bar, the custom ⎯ ▢ ✕ work, the bar drags the window,
edges resize, and the UI renders in IBM Plex.

## Non-goals

No tabs, no transport/interaction changes (separate features). No theme color
changes. If the IBM Plex `.ttf` cannot be fetched at build time, the font step is
skipped and the Segoe/Consolas fallback stays — the window chrome ships
regardless.
