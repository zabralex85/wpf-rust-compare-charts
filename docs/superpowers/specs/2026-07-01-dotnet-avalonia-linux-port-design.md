# Port the .NET WPF shell to Avalonia UI (Linux-capable)

**Date:** 2026-07-01
**Status:** Design — approved, pending spec review

## Problem

The .NET side of this PoC (`dotnet/`) renders its INU telemetry dashboard with **WPF**. WPF is a Windows-only UI framework (bound to Win32 + DirectX); it cannot run on Linux. The goal is a .NET dashboard that runs natively on Linux (a Kali VM in VirtualBox is the target test host), as well as Windows.

The solution's onion architecture makes this tractable: only the outermost shell is Windows-tied.

## Goal

Replace the WPF shell project `TelemetryPoc.App` with an **Avalonia UI** shell (Avalonia 11) that runs natively on Linux, Windows, and macOS. Keep the four inner onion rings unchanged. The project name stays `TelemetryPoc.App` (in-place replacement, not a parallel `.Avalonia` project).

### Non-goals

- Not keeping the WPF app. It is replaced, not kept alongside. (Consequence accepted below.)
- Not touching `Domain`, `Application`, `Infrastructure`, or `Presentation`.
- Not adding unit tests for the shell (same policy as WPF: XAML/chart/map UI is build-verified and launch-confirmed, not unit-tested).
- Not changing the Rust app.

### Consequence for the benchmark

The PoC's original comparison was **WPF (DirectX) vs Rust**. Avalonia renders through **Skia**, the same engine class the Rust/Tauri side effectively uses. After this port the comparison becomes **Avalonia-Skia vs Rust-Skia**. This is a deliberate, accepted change in what the HUD numbers mean. It should be recorded in `CLAUDE.md` when the port lands.

## What changes vs. what stays

**Untouched (all `net8.0`, already cross-platform):**

- `TelemetryPoc.Domain` — models, store, pure logic.
- `TelemetryPoc.Application` — use cases, ports.
- `TelemetryPoc.Infrastructure` — `SqliteRideSource`, `MbTilesTileSource`, `SysInfoMetricsSampler`, `RidePathResolver`, etc.
- `TelemetryPoc.Presentation` — `BasemapRenderer`, `TrackOverlay`, `MapInteract`, `TileMath`, all UI-shaping logic. These draw with **plain SkiaSharp** (`SkiaSharp` package, not `SkiaSharp.Views.WPF`), so they carry over verbatim.

**Rewritten — `TelemetryPoc.App` only (~2316 lines, mostly mechanical):**

- Target framework: `net8.0-windows` + `<UseWPF>true</UseWPF>` → `net8.0` + Avalonia.
- App/window lifecycle, theme resources, all XAML views + code-behind, ViewModels' framework-type references.

## Package swaps

| WPF (remove) | Avalonia (add) |
|---|---|
| `net8.0-windows`, `<UseWPF>true</UseWPF>` | `net8.0`, `Avalonia` 11.x + `Avalonia.Desktop` + `Avalonia.Themes.Fluent` (or bare, our own theme) |
| `ScottPlot.WPF` (`WpfPlot` control) | `ScottPlot.Avalonia` (`AvaPlot` control), matched to the installed ScottPlot 5.x |
| `SkiaSharp.Views.WPF` (`SKElement`, `SKPaintSurfaceEventArgs`) | *none* — use a custom Avalonia `Control` that leases the Skia canvas via `ISkiaSharpApiLeaseFeature` (Avalonia's renderer is Skia already) |
| `Microsoft.Extensions.Hosting` | unchanged — Generic Host works fine under Avalonia |

Design-time / build tooling: add `Avalonia.Diagnostics` (dev only) and the `Avalonia` MSBuild props that compile `.axaml`.

## Component port map

### Shell + lifecycle

- `App.xaml` / `App.xaml.cs`: WPF `Application.OnStartup(StartupEventArgs)` → Avalonia `Application.OnFrameworkInitializationCompleted()` with `IClassicDesktopStyleApplicationLifetime`. The **entire Generic Host DI graph in `App.xaml.cs` is reused verbatim** (the `Host.CreateApplicationBuilder()` block, env-var overrides, all `AddSingleton` registrations). Only the outer hook changes: build/start the host, resolve `MainWindow`, assign `desktop.MainWindow = window` instead of `window.Show()`. `OnExit` → dispose the host on `desktop.ShutdownRequested` / lifetime exit.
- Add a `Program.cs` `Main` with `BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)` (Avalonia's required entry point; WPF generated this implicitly).
- `MainWindow.xaml` → Avalonia `Window` (`.axaml`). Frameless/custom-chrome settings (from the window-chrome work) re-expressed with Avalonia window properties (`SystemDecorations`, `ExtendClientAreaToDecorationsHint`, etc.).

### Theme

- `Resources/Theme.xaml` → an Avalonia `Styles` + `ResourceDictionary` (`.axaml`). Named brushes (`Panel`, `Panel2`, `Border1`, `TextData`, `TextDim`, `MonoFont`, …) map across; `{StaticResource Key}` syntax is the same. Watch for: `x:Static` differences, `Trigger`/`Style.Triggers` → Avalonia selector styles / pseudo-classes, and control-template differences.

### Plain XAML views (dialect port)

`TopBar`, `TransportBar`, `ParamPanel`, `OverviewView`, `WidgetGridView`, `Hud`, and **`GaugeView`** (confirmed pure XAML: `Canvas` + `Path`/`PathGeometry`/`ArcSegment`, no Skia):

- `UserControl` xmlns → Avalonia namespaces; file extension `.xaml` → `.axaml`.
- Bindings (`{Binding X}`) are largely identical.
- Known dialect fixups: `ArcSegment` (`Point`/`Size`/`IsLargeArc`/`SweepDirection` — Avalonia geometry syntax differs slightly), `Visibility` (WPF `Visibility.Collapsed`) → Avalonia `IsVisible` (bool); `TextBlock`/`Border`/`DockPanel`/`StackPanel`/`Grid`/`Canvas` all exist with minor property diffs; `FontFamily` resource resolution.

### ViewModels

`DashboardViewModel`, `GaugeViewModel`, `HudViewModel`, `LineChartViewModel`, `MapWidgetViewModel`, `OverviewViewModel`, `ParamGroupViewModel`, `ParamRowViewModel`, `TopBarViewModel`, `TransportViewModel`, `WidgetViewModel`, `RideSession`:

- Near-zero change — POCO/MVVM. Replace any `System.Windows.*` types (e.g. `Point`, `DispatcherTimer`, `INotifyPropertyChanged` is fine as-is) with Avalonia equivalents (`Avalonia.Point`, `Avalonia.Threading.DispatcherTimer`). Dispatcher marshalling `Dispatcher.Invoke`/`BeginInvoke` → `Avalonia.Threading.Dispatcher.UIThread.Post/InvokeAsync`.

### Charts — `LineChartView`

- `sp:WpfPlot` → `AvaPlot`. Plot-building API (adding series, axes, styling) is shared across ScottPlot backends, so the plot-population code is largely unchanged.
- Hover tooltip: WPF `MouseMove`/`MouseLeave` → Avalonia `PointerMoved`/`PointerExited`; `e.GetPosition(Plot)` → `e.GetPosition(control)`. The overlay `Border`/`TextBlock` tooltip ports as plain XAML.

### Map — `MapWidgetView` (largest single piece)

The **drawing** is unchanged (`BasemapRenderer.Render`, `TrackOverlay.Draw`, `MapInteract`, `TileMath` — all in the Presentation ring). Only the **host control + input** is rewritten:

- **Skia host:** replace `SKElement` + `OnPaintSurface(SKPaintSurfaceEventArgs)` with a custom Avalonia `Control` (or `UserControl` hosting one) that overrides `Render(DrawingContext)`, pushes an `ICustomDrawOperation`, and inside it obtains the `SKCanvas` via `context.TryGetFeature<ISkiaSharpApiLeaseFeature>()` → `lease.SkCanvas`. The existing paint logic (clear, rebuild-or-blit cached basemap `SKImage`, translate/scale preview transforms, draw track overlay) moves into that operation unchanged. `InvalidateVisual()` exists in Avalonia and drives repaint the same way.
- **Basemap rasterisation:** `SKSurface.Create(new SKImageInfo(...))` + `surface.Snapshot()` is plain SkiaSharp — unchanged.
- **Input events:** `MouseWheelEventArgs` → `PointerWheelEventArgs` (`e.Delta.Y`); `MouseButtonEventArgs`/`MouseEventArgs` → `PointerPressedEventArgs`/`PointerMoved`/`PointerReleasedEventArgs`; `e.GetPosition(Skia)` → `e.GetPosition(this)`; button/click detection via `e.GetCurrentPoint(this).Properties` and `e.ClickCount`; `CaptureMouse`/`ReleaseMouseCapture` → `e.Pointer.Capture(this)` / `e.Pointer.Capture(null)`.
- **Debounce timer:** `System.Windows.Threading.DispatcherTimer` → `Avalonia.Threading.DispatcherTimer` (same API shape).
- Wheel-event `e.Handled = true` (stop the outer scroll viewer stealing zoom) → Avalonia `e.Handled = true`, with tunnel/bubble routing checked.

## Build / verify strategy

### Step 0 — Kali access via MCP (prerequisite)

The Kali VM runs a shell-capable MCP server so this Claude Code session can build/run there.

On Kali:

```bash
sudo apt update && sudo apt install -y nodejs npm
npx -y supergateway \
  --stdio "npx -y mcp-server-commands" \
  --port 8765 --ssePath /sse --messagePath /message
```

VirtualBox NAT port-forward: Host `127.0.0.1:8765` → Guest `:8765`.

On Windows:

```powershell
claude mcp add --transport sse kali http://127.0.0.1:8765/sse
```

Reconnect the session; the Kali command-exec tools load as `mcp__kali__*`.

> **Security note.** `mcp-server-commands` executes arbitrary shell commands with no authentication. Exposed on a port, anyone who can reach that port gets a shell as the Kali user. Keep it bound behind **NAT + `127.0.0.1` host-forward** (not a bridged/public interface), treat the forwarded port as a live shell, and kill the process when the port work is done. Blast radius is limited to the disposable Kali VM.

Fallback if MCP is troublesome: SSH (`openssh-server` on Kali + the same port-forward) driven from the Bash tool. Or code-only (build-verify on Windows; user runs on Kali).

Also required on Kali for the app to run: **.NET 8 SDK** (`dotnet-sdk-8.0`), plus X11/desktop libraries Avalonia needs (`libx11`, `libice`, `libsm`, `libfontconfig1`, mesa/GL). Kali's desktop already supplies most; install the .NET SDK explicitly.

### Verification steps

1. **Windows first:** `dotnet build` + `dotnet run --project dotnet/src/TelemetryPoc.App` on Windows. Avalonia is cross-platform, so a working Windows launch proves the shell before Linux is involved.
2. **Kali build/run via MCP:** `dotnet build` then `dotnet run` on Kali. Confirm: compiles on Linux, the process launches and stays alive, HUD logs are clean, no unhandled exception.
3. **Visual confirm (human):** the user looks at the Avalonia window on the VirtualBox desktop and checks it against `docs/reference/dashboard-target.md`. A headless MCP/SSH shell cannot screenshot rendered pixels, so the pixel-level check is the user's.

## Testing

- The existing **170 xUnit tests** are unchanged (the rings they cover are untouched). Run them on Kali too (`dotnet test`) — that proves the core logic runs on Linux, independent of the GUI.
- `NetArchTest` ring-boundary rules still apply and must still pass (the Avalonia shell must not be referenced by inner rings).
- The Avalonia shell itself: build-verify + launch-confirm only, consistent with the repo's existing "charts/map/GUI are not unit-tested" convention.

## Risks & mitigations

- **Avalonia XAML ≠ WPF.** Triggers, some control properties, and geometry syntax differ. Mitigation: port view-by-view, build after each, fix dialect errors as they surface. The gauge `ArcSegment` and any `Style.Triggers` are the most likely to need rework.
- **Skia-lease custom control is the one genuinely new mechanism.** Mitigation: build it first as a tiny spike (draw a rectangle via the lease) before moving the full map paint logic in.
- **ScottPlot.Avalonia API drift** vs `ScottPlot.WPF` (hover/interaction). Mitigation: the plot-building code is shared; only the control host + pointer events differ — port those explicitly.
- **Fonts on Linux.** The `MonoFont` resource may name a Windows-only family. Mitigation: pick a font present on Kali (e.g. `DejaVu Sans Mono`) or embed the font as an Avalonia asset for identical rendering across OSes.
- **Path/filesystem differences on Linux.** `RIDE_DB` default resolution, case-sensitive paths, `/` separators. Mitigation: verify `RidePathResolver` behaviour on Kali; pass an explicit `RIDE_DB` in the first Linux run.
- **`AppContext.BaseDirectory` / working directory** differences for locating `ride.db` / `.mbtiles` on Linux. Mitigation: test with explicit env vars first, then confirm the auto-resolve.

## Open questions

None blocking. Font choice and exact frameless-window chrome mapping are resolved during implementation.
