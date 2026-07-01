# Avalonia Port of the .NET WPF Shell (Linux-capable) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Windows-only WPF shell `TelemetryPoc.App` with an Avalonia UI shell that builds and runs natively on Linux (Kali), Windows, and macOS, keeping the four inner onion rings untouched.

**Architecture:** In-place framework swap of the outermost project only. `net8.0-windows`+`UseWPF` → `net8.0`+Avalonia 11. The Generic Host DI graph, all ViewModels, and all SkiaSharp drawing logic (in the Presentation ring) are reused; only framework-typed surfaces (XAML dialect, per-frame hook, brushes, pointer events, window chrome, the Skia host control) are rewritten. Charts move `ScottPlot.WPF`→`ScottPlot.Avalonia`; the map's `SKElement` host becomes a custom Avalonia control that leases the Skia canvas.

**Tech Stack:** .NET 8, Avalonia 11, ScottPlot.Avalonia 5.1.59, SkiaSharp 4.148 (already used by the Presentation ring), Microsoft.Extensions.Hosting, xUnit + NetArchTest (existing suite). Kali test host reached via an MCP command-exec server.

## Global Constraints

- **Do not modify** `TelemetryPoc.Domain`, `TelemetryPoc.Application`, `TelemetryPoc.Infrastructure`, `TelemetryPoc.Presentation`. Only `TelemetryPoc.App` changes.
- Project name stays **`TelemetryPoc.App`** (in-place replacement, not a parallel project).
- Target framework for `TelemetryPoc.App`: **`net8.0`** (not `-windows`). No `UseWPF`, no `System.Windows.*`, no Win32 P/Invoke anywhere in the final state.
- Keep behavioral parity with the Rust app: same 33 ms UI cadence, same eviction/rounding/latency model (all already in the untouched rings — do not reimplement).
- GUI code is **build-verified + launch-confirmed**, not unit-tested (repo convention in `CLAUDE.md`). The existing **170 xUnit tests must stay green** and are the regression gate; run `dotnet test` after tasks that touch shared/VM code.
- Avalonia XAML files use the **`.axaml`** extension and Avalonia xmlns; brushes are `IBrush`; `Visibility.Collapsed/Visible` → `IsVisible` (bool); colors via `Color.Parse("#AARRGGBB")`.
- ScottPlot version pin: **5.1.59** (match the current `ScottPlot.WPF` version exactly).
- Do all work on a feature branch (e.g. `feat/dotnet-avalonia-linux`). Commit after every task.

---

## File Structure

New / rewritten files in `dotnet/src/TelemetryPoc.App/`:

- `TelemetryPoc.App.csproj` — rewritten (Avalonia SDK-style, fonts as `AvaloniaResource`).
- `Program.cs` — **new** (Avalonia entry point; WPF generated this implicitly).
- `App.axaml` + `App.axaml.cs` — rewritten (Fluent-less Avalonia `Application`, theme include, Generic Host lifetime).
- `MainWindow.axaml` + `MainWindow.axaml.cs` — rewritten (Avalonia frameless window; Win32 chrome block deleted).
- `Resources/Theme.axaml` — rewritten from `Theme.xaml` (Avalonia resources + styles).
- `Controls/SkiaCanvasControl.cs` — **new** (leases the Skia canvas; replaces `SKElement`).
- `Controls/FrameClock.cs` — **new** (self-invalidating per-frame tick; replaces `CompositionTarget.Rendering`).
- `RideSession.cs` — modified (Avalonia `DispatcherTimer`).
- `ViewModels/*.cs` — modified where they reference `System.Windows.*` (`HudViewModel`, `ParamRowViewModel`; others only if flagged by build).
- `Views/*.axaml` + `Views/*.axaml.cs` — rewritten from the `.xaml` pairs (dialect + pointer-event port).

Deleted: `App.xaml`, `MainWindow.xaml`, `Resources/Theme.xaml`, all `Views/*.xaml`, `AssemblyInfo.cs` (WPF theme-info attributes), and the WPF `.xaml.cs` variants (replaced by `.axaml.cs`).

---

## Task 0: Kali test host bring-up (environment prerequisite)

**Files:** none (environment). This task is a gate, not code.

**Goal:** Give this session command execution on the Kali VM and prove the *existing* solution's tests run on Linux before any porting starts — establishing that the four rings are already Linux-clean.

- [ ] **Step 1: Install .NET 8 SDK on Kali**

On the Kali VM shell:
```bash
sudo apt update && sudo apt install -y dotnet-sdk-8.0
dotnet --version   # expect 8.0.x
```
If the distro package is missing/old, use Microsoft's script:
```bash
wget https://dot.net/v1/dotnet-install.sh -O /tmp/dotnet-install.sh
bash /tmp/dotnet-install.sh --channel 8.0
export PATH="$HOME/.dotnet:$PATH"
```

- [ ] **Step 2: Install Avalonia's Linux runtime deps**

```bash
sudo apt install -y libx11-6 libice6 libsm6 libfontconfig1 libgl1 libx11-xcb1 libskiasharp
```
(SkiaSharp bundles its native lib via NuGet; the `libfontconfig1`/GL/X11 libs are what Avalonia needs to open a window.)

- [ ] **Step 3: Start the MCP command server on Kali**

```bash
sudo apt install -y nodejs npm
npx -y supergateway --stdio "npx -y mcp-server-commands" \
  --port 8765 --ssePath /sse --messagePath /message
```

- [ ] **Step 4: Port-forward + register the MCP server**

VirtualBox (VM powered off): Settings → Network → Adapter 1 (NAT) → Advanced → Port Forwarding → rule Host `127.0.0.1:8765` → Guest `:8765`. Boot the VM, start the server (Step 3), then on Windows:
```powershell
claude mcp add --transport sse kali http://127.0.0.1:8765/sse
```
Reconnect the session; confirm `mcp__kali__*` tools load.

> **Security:** `mcp-server-commands` is an unauthenticated remote shell. Keep it on NAT+`127.0.0.1` host-forward only (never bridged/public). Kill the server process when the port work is done.

- [ ] **Step 5: Clone/sync the repo onto Kali and run the existing tests**

Via the Kali MCP (or SSH fallback), in the repo root on Kali:
```bash
cd dotnet && dotnet test
```
Expected: all **170 tests pass** on Linux. This proves the rings are already cross-platform. If any fail, stop and investigate before porting (a ring has a hidden Windows dependency the spec assumed absent).

- [ ] **Step 6: Commit** — nothing to commit (environment only). Record in the branch's first commit message that Kali is verified green.

---

## Task 1: Avalonia project skeleton — blank window launches

**Files:**
- Modify (rewrite): `dotnet/src/TelemetryPoc.App/TelemetryPoc.App.csproj`
- Create: `dotnet/src/TelemetryPoc.App/Program.cs`
- Create: `dotnet/src/TelemetryPoc.App/App.axaml`, `App.axaml.cs`
- Create: `dotnet/src/TelemetryPoc.App/MainWindow.axaml`, `MainWindow.axaml.cs`
- Delete: `App.xaml`, `App.xaml.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`, `AssemblyInfo.cs`

**Interfaces:**
- Produces: an Avalonia `Application` subclass `App` and a `Window` subclass `MainWindow` that later tasks extend. `App.axaml.cs` will hold the Generic Host graph in Task 5; for now it just opens a blank `MainWindow`.

This task deliberately stubs out the real UI so the project compiles and runs a blank window before the porting churn. The old WPF views still exist on disk but are excluded from compilation (see Step 1 — the rewritten csproj no longer sets `UseWPF`, so `.xaml` files are not compiled; we delete them in their per-view tasks).

- [ ] **Step 1: Rewrite the csproj**

`dotnet/src/TelemetryPoc.App/TelemetryPoc.App.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <BuiltInComInteropSupport>true</BuiltInComInteropSupport>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <AvaloniaUseCompiledBindingsByDefault>false</AvaloniaUseCompiledBindingsByDefault>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\TelemetryPoc.Domain\TelemetryPoc.Domain.csproj" />
    <ProjectReference Include="..\TelemetryPoc.Application\TelemetryPoc.Application.csproj" />
    <ProjectReference Include="..\TelemetryPoc.Infrastructure\TelemetryPoc.Infrastructure.csproj" />
    <ProjectReference Include="..\TelemetryPoc.Presentation\TelemetryPoc.Presentation.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Avalonia" Version="11.2.3" />
    <PackageReference Include="Avalonia.Desktop" Version="11.2.3" />
    <PackageReference Include="Avalonia.Fonts.Inter" Version="11.2.3" />
    <PackageReference Include="Avalonia.Diagnostics" Version="11.2.3" Condition="'$(Configuration)'=='Debug'" />
    <PackageReference Include="ScottPlot.Avalonia" Version="5.1.59" />
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="10.0.9" />
  </ItemGroup>

  <ItemGroup>
    <AvaloniaResource Include="Fonts\*.ttf" />
    <AvaloniaResource Include="Resources\**\*.axaml" />
  </ItemGroup>

  <ItemGroup>
    <None Update="appsettings.json"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></None>
  </ItemGroup>

</Project>
```
> `OutputType` stays `WinExe` so no console window appears on Windows; it is ignored on Linux. `Avalonia.Fonts.Inter` is a fallback UI font so text renders even before the embedded IBM Plex fonts are wired (Task 3). Verify the exact latest Avalonia 11.2.x patch is available; keep all four Avalonia packages on the same version.

- [ ] **Step 2: Add the Avalonia app manifest**

Create `dotnet/src/TelemetryPoc.App/app.manifest` (Avalonia template default; enables per-monitor DPI on Windows, inert on Linux):
```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true</dpiAware>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
    </windowsSettings>
  </application>
</assembly>
```

- [ ] **Step 3: Write `Program.cs`**

```csharp
using Avalonia;

namespace TelemetryPoc.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
```

- [ ] **Step 4: Write `App.axaml` (stub) and `App.axaml.cs` (stub)**

`App.axaml`:
```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="TelemetryPoc.App.App">
  <Application.Styles>
    <FluentTheme />
  </Application.Styles>
</Application>
```
`App.axaml.cs`:
```csharp
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace TelemetryPoc.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
```
> The `FluentTheme` and the `new MainWindow()` here are temporary. Task 3 swaps `FluentTheme` for the ported `Theme.axaml`; Task 5 replaces the `new MainWindow()` with the Generic Host graph.

- [ ] **Step 5: Write the stub `MainWindow.axaml` / `.axaml.cs`**

`MainWindow.axaml`:
```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="TelemetryPoc.App.MainWindow"
        Title="INU-MONITOR (.NET)" Width="1600" Height="900"
        WindowState="Maximized" Background="#FF0A0E14">
  <TextBlock Text="Avalonia shell — porting in progress"
             HorizontalAlignment="Center" VerticalAlignment="Center" Foreground="#FFC3CCD8" />
</Window>
```
`MainWindow.axaml.cs`:
```csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TelemetryPoc.App;

public partial class MainWindow : Window
{
    public MainWindow() => AvaloniaXamlLoader.Load(this);
}
```

- [ ] **Step 6: Delete the WPF shell files**

```bash
cd dotnet/src/TelemetryPoc.App
rm App.xaml App.xaml.cs MainWindow.xaml MainWindow.xaml.cs AssemblyInfo.cs
```

- [ ] **Step 7: Build**

Run: `cd dotnet && dotnet build src/TelemetryPoc.App`
Expected: build FAILS — the old `Views/*.xaml.cs` and `ViewModels/*.cs` still reference `System.Windows.*`, which no longer exists. This is expected mid-port. To get a *launchable* skeleton now, temporarily exclude the not-yet-ported UI from compilation:

Add to the csproj (temporary, removed in Task 13's cleanup):
```xml
  <ItemGroup>
    <Compile Remove="Views\**\*.cs" />
    <Compile Remove="ViewModels\**\*.cs" />
    <Compile Remove="RideSession.cs" />
    <AvaloniaXaml Remove="Views\**\*.xaml" />
  </ItemGroup>
```
Also delete the now-uncompiled WPF view XAML so they don't get picked up: leave them for their per-view tasks (they are `.xaml`, not `.axaml`, and not referenced by `AvaloniaXaml` globs, so they are inert).

- [ ] **Step 8: Build + run the blank window (Windows)**

Run: `cd dotnet && dotnet build src/TelemetryPoc.App`
Expected: PASS.
Run: `dotnet run --project dotnet/src/TelemetryPoc.App`
Expected: a maximized dark window opens showing "Avalonia shell — porting in progress". Close it.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat(dotnet): Avalonia project skeleton — blank window launches"
```

---

## Task 2: Port ViewModels + RideSession off System.Windows

**Files:**
- Modify: `ViewModels/HudViewModel.cs`, `ViewModels/ParamRowViewModel.cs`
- Modify: `RideSession.cs`
- Create: `Controls/FrameClock.cs`
- Modify: `TelemetryPoc.App.csproj` (un-exclude `ViewModels`, `RideSession.cs`)

**Interfaces:**
- Produces:
  - `FrameClock` — an Avalonia `Control` that raises `event EventHandler<TimeSpan>? Rendering` once per rendered frame (replacement for WPF `CompositionTarget.Rendering`). Placed invisibly in the visual tree by the Hud view (Task 10).
  - `HudViewModel` constructor gains a parameter change: it no longer subscribes to a static frame event in its constructor. Instead expose `public void Tick(double elapsedMs)` that the `FrameClock` calls, and keep `OnTick()` (data cadence) as-is. Later tasks (Hud view) wire `FrameClock.Rendering` → `HudViewModel.Tick`.
  - `ParamRowViewModel` brush properties change type `Brush` → `IBrush` (`ValueBrush`, `DotBrush`, `RowBackground`).

- [ ] **Step 1: Write `FrameClock`**

`dotnet/src/TelemetryPoc.App/Controls/FrameClock.cs`:
```csharp
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace TelemetryPoc.App.Controls;

/// <summary>
/// Drop-in replacement for WPF's CompositionTarget.Rendering: an invisible control
/// that re-invalidates itself every frame, raising <see cref="Rendering"/> once per
/// composited frame (~display refresh). Used by the HUD to measure real render FPS.
/// </summary>
public sealed class FrameClock : Control
{
    public event EventHandler<TimeSpan>? Rendering;
    private readonly System.Diagnostics.Stopwatch _sw = System.Diagnostics.Stopwatch.StartNew();

    public FrameClock()
    {
        IsHitTestVisible = false;
        Width = 0; Height = 0;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        Rendering?.Invoke(this, _sw.Elapsed);
        // Schedule the next frame. Background priority yields to input/layout so this
        // ticks at the compositor's pace instead of spinning the CPU.
        Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Background);
    }
}
```

- [ ] **Step 2: Port `HudViewModel`**

Replace `using System.Windows.Media;` (removed) and the `CompositionTarget.Rendering += OnRender;` wiring. Rename `OnRender(object?, EventArgs)` to `public void Tick(TimeSpan elapsed)` and drive the FPS meter from the passed elapsed time instead of an internal stopwatch:

```csharp
// remove: using System.Windows.Media;
// remove the _sw field (elapsed now comes from FrameClock)
// constructor: remove `CompositionTarget.Rendering += OnRender;` — keep `_session.Ticked += OnTick;`

public void Tick(TimeSpan elapsed)
{
    if (_session.IsPaused) { _wasPaused = true; return; }
    if (_wasPaused) { _fps.Reset(); _wasPaused = false; }
    _fps.Tick(elapsed.TotalMilliseconds);
    FpsText = _fps.Fps().ToString("F0", Inv);
    FrameText = _fps.FrameTimeMs().ToString("F1", Inv);
}
```
Everything else in `HudViewModel` (the `OnTick` data-cadence handler, properties, `INotifyPropertyChanged`) is unchanged.

- [ ] **Step 3: Port `ParamRowViewModel` brushes to Avalonia.Media**

```csharp
using Avalonia.Media;   // replaces System.Windows.Media
// ...
private IBrush _valueBrush = Brushes.Gray;
public IBrush ValueBrush { get => _valueBrush; private set { _valueBrush = value; Raise(nameof(ValueBrush)); } }

private IBrush _dotBrush = Brushes.Gray;
public IBrush DotBrush { get => _dotBrush; private set { _dotBrush = value; Raise(nameof(DotBrush)); } }

private IBrush _rowBg = Brushes.Transparent;
public IBrush RowBackground { get => _rowBg; private set { _rowBg = value; Raise(nameof(RowBackground)); } }

private static readonly IBrush CriticalBg = Hex("#FF1A0E11");

// Refresh(): RowBackground = d.Critical ? CriticalBg : Brushes.Transparent;  (unchanged shape)

private static IBrush Hex(string hex) => new SolidColorBrush(Color.Parse(hex));
```
> Avalonia brushes are not frozen (`Freeze()` is removed — Avalonia has no such method). `Color.Parse` accepts `#AARRGGBB`, matching the existing hex strings from `ParamRowView.Build`.

- [ ] **Step 4: Port `RideSession` timer**

```csharp
using Avalonia.Threading;   // replaces System.Windows.Threading
// ...
private DispatcherTimer? _timer;
// ...
_timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) => Tick())
    { };
_timer.Start();
```
Match the existing tick handler name/signature (the current code sets `Interval` then presumably attaches a `Tick` handler — preserve whatever method it calls; only the type/priority source changes). Avalonia's `DispatcherTimer` ctor `(TimeSpan interval, DispatcherPriority priority, EventHandler handler)` starts stopped; call `.Start()`.

- [ ] **Step 5: Un-exclude the ported files**

In the csproj, remove `RideSession.cs` and `ViewModels\**\*.cs` from the temporary `<Compile Remove>` block (leave `Views\**` excluded until their tasks).

- [ ] **Step 6: Build**

Run: `cd dotnet && dotnet build src/TelemetryPoc.App`
Expected: PASS (Views still excluded).

- [ ] **Step 7: Run the full test suite**

Run: `cd dotnet && dotnet test`
Expected: 170 pass (VMs are not unit-tested, but this confirms no ring regressed and the solution still composes).

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat(dotnet): port ViewModels + RideSession off System.Windows (Avalonia)"
```

---

## Task 3: Port the theme resources + embedded fonts

**Files:**
- Create: `Resources/Theme.axaml`
- Delete: `Resources/Theme.xaml`
- Modify: `App.axaml` (include the theme, keep FluentTheme as base for default control templates)

**Interfaces:**
- Produces: named resources consumed by every view — brushes (`Bg`, `Panel`, `Panel2`, `PanelHeader`, `Border1`, `ColHeaderBg`, `TextData`, `TextDim`, `PanelTitle`, `AccentCyan`, `Green`, `Amber`, `Red`, `CriticalBg`, `AlarmPillBg`, `CautionPillBg`), font families (`SansFont`, `MonoFont`), and the `CaptionButton` style. Keys are unchanged from the WPF theme so views bind identically.

- [ ] **Step 1: Create `Resources/Theme.axaml` — colors, brushes, fonts**

```xml
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <Color x:Key="BgColor">#FF0A0E14</Color>
  <SolidColorBrush x:Key="Bg" Color="{StaticResource BgColor}" />
  <SolidColorBrush x:Key="Panel" Color="#FF10151D" />
  <SolidColorBrush x:Key="Panel2" Color="#FF0C121A" />
  <SolidColorBrush x:Key="PanelHeader" Color="#FF131A24" />
  <SolidColorBrush x:Key="Border1" Color="#FF1D2632" />
  <SolidColorBrush x:Key="ColHeaderBg" Color="#FF0C1119" />
  <SolidColorBrush x:Key="TextData" Color="#FFC3CCD8" />
  <SolidColorBrush x:Key="TextDim" Color="#FF566273" />
  <SolidColorBrush x:Key="PanelTitle" Color="#FF8B98A9" />
  <SolidColorBrush x:Key="AccentCyan" Color="#FF38C5E0" />
  <SolidColorBrush x:Key="Green" Color="#FF2FD17A" />
  <SolidColorBrush x:Key="Amber" Color="#FFF5B440" />
  <SolidColorBrush x:Key="Red" Color="#FFFF4D52" />
  <SolidColorBrush x:Key="CriticalBg" Color="#FF1A0E11" />
  <SolidColorBrush x:Key="AlarmPillBg" Color="#33FF4D52" />
  <SolidColorBrush x:Key="CautionPillBg" Color="#33F5B440" />

  <!-- IBM Plex fonts embedded as AvaloniaResource in Fonts\ -->
  <FontFamily x:Key="SansFont">avares://TelemetryPoc.App/Fonts/#IBM Plex Sans</FontFamily>
  <FontFamily x:Key="MonoFont">avares://TelemetryPoc.App/Fonts/#IBM Plex Mono</FontFamily>
</ResourceDictionary>
```
> The `avares://<assembly>/Fonts/#<Family Name>` form embeds the TTFs cross-platform, so identical text rendering on Kali and Windows — this retires the "fonts on Linux" risk. The family name after `#` must match the font's internal name ("IBM Plex Sans" / "IBM Plex Mono").

- [ ] **Step 2: Port the `CaptionButton` style into `Theme.axaml`**

WPF `Style TargetType="Button"` + `ControlTemplate` becomes an Avalonia keyed style with a `ControlTemplate`. Append inside the `ResourceDictionary`:
```xml
  <ControlTheme x:Key="CaptionButton" TargetType="Button">
    <Setter Property="Width" Value="30" />
    <Setter Property="Height" Value="24" />
    <Setter Property="Background" Value="Transparent" />
    <Setter Property="Foreground" Value="{StaticResource TextDim}" />
    <Setter Property="BorderThickness" Value="0" />
    <Setter Property="FontFamily" Value="{StaticResource MonoFont}" />
    <Setter Property="FontSize" Value="12" />
    <Setter Property="Cursor" Value="Hand" />
    <Setter Property="Template">
      <ControlTemplate>
        <Border x:Name="bd" Background="{TemplateBinding Background}">
          <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"
                            Content="{TemplateBinding Content}" />
        </Border>
      </ControlTemplate>
    </Setter>
    <Style Selector="^:pointerover /template/ Border#bd">
      <Setter Property="Background" Value="{StaticResource Border1}" />
    </Style>
  </ControlTheme>
```
> Avalonia uses `ControlTheme` (keyed) for what WPF calls a keyed `Style TargetType`. Hover uses the `:pointerover` pseudo-class selector instead of WPF `Trigger`s. If the WPF template had `IsMouseOver`/`IsPressed` triggers with other setters, port each as a `Style Selector="^:pointerover"` / `^:pressed"` block. Read the full `Theme.xaml` and reproduce every setter/trigger — do not drop any.

- [ ] **Step 3: Wire the theme into `App.axaml`**

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="TelemetryPoc.App.App">
  <Application.Styles>
    <FluentTheme />
  </Application.Styles>
  <Application.Resources>
    <ResourceInclude Source="avares://TelemetryPoc.App/Resources/Theme.axaml" />
  </Application.Resources>
</Application>
```
> Keep `FluentTheme` as the base so stock controls (Button, ScrollViewer, Slider) have templates; the theme dictionary only overrides colors/fonts and adds `CaptionButton`. If the reference dark look needs the ScrollViewer corner/scrollbar styling that WPF had (see commit `6d61869`), port those styles into `Theme.axaml` too.

- [ ] **Step 4: Delete the WPF theme + build**

```bash
rm dotnet/src/TelemetryPoc.App/Resources/Theme.xaml
cd dotnet && dotnet build src/TelemetryPoc.App
```
Expected: PASS.

- [ ] **Step 5: Launch-confirm fonts load**

Temporarily set the stub `MainWindow.axaml` TextBlock `FontFamily="{StaticResource MonoFont}"` and run:
Run: `dotnet run --project dotnet/src/TelemetryPoc.App`
Expected: the placeholder text renders in IBM Plex Mono (monospace). Revert the temporary FontFamily edit.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(dotnet): port theme resources + embedded fonts to Avalonia"
```

---

## Task 4: Skia canvas host control (retire the SKElement risk early)

**Files:**
- Create: `Controls/SkiaCanvasControl.cs`

**Interfaces:**
- Produces: `SkiaCanvasControl : Control` with `public event Action<SKCanvas, int, int>? PaintSurface;` (canvas, pixelWidth, pixelHeight), raised inside the Avalonia render pass with a leased Skia canvas. `InvalidateVisual()` triggers a repaint. This is the drop-in host for the map (Task 13) — replacing WPF `SKElement` + `SKPaintSurfaceEventArgs`. Build it now, in isolation, because the Skia-lease API is the one genuinely new mechanism in the port.

- [ ] **Step 1: Write `SkiaCanvasControl`**

`dotnet/src/TelemetryPoc.App/Controls/SkiaCanvasControl.cs`:
```csharp
using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace TelemetryPoc.App.Controls;

/// <summary>
/// Avalonia replacement for SkiaSharp.Views.WPF.SKElement: leases the underlying Skia
/// canvas during the render pass and raises <see cref="PaintSurface"/> so callers can
/// draw with SkiaSharp directly. Pixel size is passed so callers size their raster to
/// the device surface.
/// </summary>
public sealed class SkiaCanvasControl : Control
{
    public event Action<SKCanvas, int, int>? PaintSurface;

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = Bounds;
        if (bounds.Width < 1 || bounds.Height < 1) { return; }
        context.Custom(new SkiaDrawOp(new Rect(bounds.Size), this));
    }

    private sealed class SkiaDrawOp : ICustomDrawOperation
    {
        private readonly SkiaCanvasControl _owner;
        public SkiaDrawOp(Rect bounds, SkiaCanvasControl owner) { Bounds = bounds; _owner = owner; }

        public Rect Bounds { get; }
        public bool HitTest(Point p) => false;
        public bool Equals(ICustomDrawOperation? other) => false;
        public void Dispose() { }

        public void Render(ImmediateDrawingContext context)
        {
            var lease = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (lease is null) { return; }   // non-Skia backend (should not happen on desktop)
            using var api = lease.Lease();
            var canvas = api.SkCanvas;
            int w = (int)Math.Ceiling(Bounds.Width);
            int h = (int)Math.Ceiling(Bounds.Height);
            _owner.PaintSurface?.Invoke(canvas, w, h);
        }
    }
}
```
> `ISkiaSharpApiLeaseFeature` / `Avalonia.Skia` gives the live `SKCanvas`. Note the DPI subtlety: `Bounds` is in DIPs; the leased canvas is already scaled by Avalonia's render transform, so drawing in DIP coordinates matches the rest of the UI. Keep the map's existing "rasterise once to an `SKImage`, blit each frame" logic (it sizes the raster from the passed w/h).

- [ ] **Step 2: Build**

Run: `cd dotnet && dotnet build src/TelemetryPoc.App`
Expected: PASS.

- [ ] **Step 3: Spike-verify the lease draws (temporary)**

Temporarily put a `SkiaCanvasControl` in the stub `MainWindow.axaml` (`xmlns:c="using:TelemetryPoc.App.Controls"`, `<c:SkiaCanvasControl x:Name="Spike"/>`) and in code-behind:
```csharp
Spike.PaintSurface += (canvas, w, h) =>
{
    canvas.Clear(new SKColor(0x0A, 0x0E, 0x14));
    using var p = new SKPaint { Color = new SKColor(0x38, 0xC5, 0xE0) };
    canvas.DrawRect(20, 20, w - 40, h - 40, p);
};
```
Run: `dotnet run --project dotnet/src/TelemetryPoc.App`
Expected: a cyan rectangle on the dark background — proves the Skia lease renders. Revert the spike edits to `MainWindow`.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(dotnet): Skia canvas host control via ISkiaSharpApiLeaseFeature"
```

---

## Task 5: Shell + Generic Host DI + frameless window

**Files:**
- Rewrite: `App.axaml.cs` (Generic Host graph), `MainWindow.axaml`, `MainWindow.axaml.cs`
- Modify: csproj (drop the temporary `Views\**` XAML exclusion is NOT yet — views still stubbed; keep `Views\**\*.cs` excluded, but MainWindow now composes real regions as they land)

**Interfaces:**
- Consumes: DI registrations identical to the old `App.xaml.cs` (RideOptions/env overrides, ports→adapters, VM singletons, `MainWindow`).
- Produces: a running host + a frameless maximized `MainWindow` whose constructor takes the same VMs `(RideSession, TopBarViewModel, OverviewViewModel, TransportViewModel, HudViewModel)`; regions are placeholders until their view tasks fill them.

- [ ] **Step 1: Port the Generic Host graph into `App.axaml.cs`**

Move the entire `Host.CreateApplicationBuilder()` block from the deleted WPF `App.xaml.cs` (env-var overrides for `RIDE_DB`/`RIDE_MBTILES`/`RIDE_SPEED`, `RideOptions` binding, all `AddSingleton` port/adapter/VM registrations, `AddSingleton<MainWindow>()`) verbatim into `OnFrameworkInitializationCompleted`:
```csharp
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TelemetryPoc.App.ViewModels;
using TelemetryPoc.Application;
using TelemetryPoc.Infrastructure;

namespace TelemetryPoc.App;

public partial class App : Application
{
    private IHost? _host;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var builder = Host.CreateApplicationBuilder();
            // ... paste the exact env-override + Configure<RideOptions> + AddSingleton block
            //     from the old App.xaml.cs, unchanged ...
            builder.Services.AddSingleton<MainWindow>();
            _host = builder.Build();
            _host.Start();

            desktop.MainWindow = _host.Services.GetRequiredService<MainWindow>();
            desktop.ShutdownRequested += (_, _) => _host?.Dispose();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
```
> `builder.Logging.AddDebug()` stays. `AppContext.BaseDirectory` in `RidePathResolver` works cross-platform. The only WPF→Avalonia change is the outer lifecycle hook and `desktop.MainWindow = ...` instead of `window.Show()`.

- [ ] **Step 2: Rewrite `MainWindow.axaml` (Avalonia frameless)**

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:views="using:TelemetryPoc.App.Views"
        x:Class="TelemetryPoc.App.MainWindow"
        Title="INU-MONITOR (.NET)" Width="1600" Height="900"
        WindowState="Maximized"
        SystemDecorations="None" ExtendClientAreaToDecorationsHint="True"
        ExtendClientAreaTitleBarHeightHint="0"
        Background="{StaticResource Bg}">
  <Grid x:Name="RootGrid" RowDefinitions="Auto,*,Auto">
    <!-- views:TopBar / OverviewView / TransportBar / Hud land here as their tasks complete -->
    <TextBlock x:Name="ErrorBanner" IsVisible="False" Foreground="{StaticResource Red}"
               Background="{StaticResource Panel2}" Padding="12,8" FontFamily="{StaticResource MonoFont}"
               HorizontalAlignment="Center" VerticalAlignment="Top" ZIndex="100" Grid.RowSpan="3" />
  </Grid>
</Window>
```
> Avalonia's `WindowState="Maximized"` already clamps to the work area (respects the taskbar/panel) — the entire WPF `WM_GETMINMAXINFO` Win32 clamp is deleted, not ported. `SystemDecorations="None"` gives the frameless look; a draggable caption is added in the TopBar task via `PointerPressed`→`BeginMoveDrag`. `Panel.ZIndex` → `ZIndex`; `Visibility="Collapsed"` → `IsVisible="False"`.

- [ ] **Step 3: Rewrite `MainWindow.axaml.cs` (no Win32)**

```csharp
using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using TelemetryPoc.App.ViewModels;

namespace TelemetryPoc.App;

public partial class MainWindow : Window
{
    private readonly RideSession _session;

    public MainWindow(RideSession session, TopBarViewModel topBar, OverviewViewModel overview,
        TransportViewModel transport, HudViewModel hud)
    {
        AvaloniaXamlLoader.Load(this);
        _session = session;
        // Region DataContexts are assigned as each view is added to MainWindow.axaml
        // in its task (e.g. TopBar.DataContext = topBar;). Keep the VM fields wired here.
        Opened += (_, _) => _session.StartAsync();
        Closed += (_, _) => _session.Dispose();
        _session.ErrorChanged += () => Dispatcher.UIThread.Post(() =>
        {
            var banner = this.FindControl<TextBlock>("ErrorBanner")!;
            banner.Text = _session.Error;
            banner.IsVisible = _session.Error is not null;
        });
    }

    // Parameterless ctor kept only if the XAML previewer needs it; DI uses the ctor above.
}
```
> WPF `Loaded`→Avalonia `Opened`; `Dispatcher.Invoke`→`Dispatcher.UIThread.Post`. The `SourceInitialized`/`HwndSource`/all P/Invoke structs are gone. As each view task adds its control to `MainWindow.axaml` with an `x:Name`, add the matching `Xxx.DataContext = xxx;` line here.

- [ ] **Step 4: Build**

Run: `cd dotnet && dotnet build src/TelemetryPoc.App`
Expected: PASS (views still stubbed/excluded; MainWindow references only VMs + ErrorBanner).

- [ ] **Step 5: Launch-confirm host starts + data flows**

Ensure a `RIDE_DB` is available (`data/ride_small.db` or generate one). Run:
`RIDE_DB=../../data/ride_small.db dotnet run --project dotnet/src/TelemetryPoc.App`
Expected: the frameless dark window opens maximized; debug logs show the host started and the ride replay ticking (no view content yet, but no crash and no error banner). Close it.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(dotnet): Avalonia shell + Generic Host DI + frameless window"
```

---

## Tasks 6–12: Port the plain-XAML views (one task each)

Each of these tasks follows the **same recipe** applied to one view. They are separate tasks because a reviewer can accept/reject each view independently, and each ends with a launch-confirmable deliverable. Do them in this order (leaf views first, then the composite `OverviewView`/`WidgetGridView` that host them):

- **Task 6:** `TopBar` (+ frameless window drag)
- **Task 7:** `ParamPanel`
- **Task 8:** `GaugeView` (arc geometry)
- **Task 9:** `TransportBar`
- **Task 10:** `Hud` (+ `FrameClock` wiring)
- **Task 11:** `OverviewView` (composes the widget grid + panels)
- **Task 12:** `WidgetGridView` (drag/drop widget reflow)

**Per-view recipe (apply to each):**

**Files (per view `Xxx`):**
- Create: `Views/Xxx.axaml`, `Views/Xxx.axaml.cs`
- Delete: `Views/Xxx.xaml`, `Views/Xxx.xaml.cs`
- Modify: `MainWindow.axaml` (add `<views:Xxx .../>` in the right row) + `MainWindow.axaml.cs` (`Xxx.DataContext = xxx;`) once the view is a direct child of the window; for views nested inside `OverviewView`/`WidgetGridView`, wire them in their parent instead.

- [ ] **Step 1: Translate the XAML** — copy `Xxx.xaml` → `Xxx.axaml` and apply the dialect transforms:
  - Root xmlns → `https://github.com/avaloniaui`; keep `x:` namespace; add `xmlns:views="using:TelemetryPoc.App.Views"` / `xmlns:c="using:TelemetryPoc.App.Controls"` as needed.
  - `clr-namespace:...;assembly=...` → `using:...`.
  - `Visibility="Collapsed"|"Visible"` and all `Visibility` bindings → `IsVisible` (bool). If a VM exposes `Visibility`, either add an `IsVisible` bool or use Avalonia's built-in bool handling; do **not** keep `System.Windows.Visibility`.
  - `{StaticResource Key}` stays; verify every key exists in `Theme.axaml`.
  - `Grid.RowDefinitions="Auto,*"` shorthand is fine; WPF `<Grid.RowDefinitions>` element form also works.
  - Panel attached props: `DockPanel.Dock`, `Grid.Row/Column` unchanged. `Panel.ZIndex`→`ZIndex`.
  - `TextBlock`/`Border`/`StackPanel`/`Canvas`/`Path`/`Image` exist; check property diffs (`TextBlock.TextTrimming`, `Border.CornerRadius` are fine).
  - Bindings `{Binding Prop}` unchanged. `ItemsControl`/`ListBox` `ItemsSource` — Avalonia uses `ItemsSource` (not WPF `ItemsSource` alias `Items`); confirm.
  - Styles referenced by key: `Style="{StaticResource CaptionButton}"` → `Theme="{StaticResource CaptionButton}"` (Avalonia `ControlTheme` is applied via the `Theme` property, not `Style`).
- [ ] **Step 2: Translate the code-behind** — `Xxx.xaml.cs` → `Xxx.axaml.cs`:
  - `using System.Windows.Controls;` → `using Avalonia.Controls;`; `System.Windows.Input` → `Avalonia.Input`.
  - `public partial class Xxx : UserControl { public Xxx() { InitializeComponent(); } }` → `{ public Xxx() { AvaloniaXamlLoader.Load(this); } }` (add `using Avalonia.Markup.Xaml;`). Named elements are found via `this.FindControl<T>("Name")` or generated fields (Avalonia generates fields for `x:Name` when `AvaloniaXamlLoader` is used with the compiled XAML).
  - Pointer events (see mapping table below).
- [ ] **Step 3: Build** — `dotnet build src/TelemetryPoc.App`. Expected PASS. Un-exclude this view's `.cs` from the csproj `<Compile Remove>` (remove the whole `Views\**` exclusion once the *first* view lands, then each subsequent view compiles automatically; if a not-yet-ported view breaks the build, keep it excluded individually).
- [ ] **Step 4: Wire into the window/parent + launch-confirm** — add the view to `MainWindow.axaml` (or its parent), assign DataContext, run the app against `ride_small.db`, and confirm the region renders and updates against `docs/reference/dashboard-target.md`.
- [ ] **Step 5: Commit** — `git commit -m "feat(dotnet): port <Xxx> view to Avalonia"`.

**WPF → Avalonia pointer/input mapping (used by Tasks 6, 9, 11, 12 and the map):**

| WPF | Avalonia |
|---|---|
| `MouseEventArgs` / `MouseMove` | `PointerEventArgs` / `PointerMoved` |
| `MouseButtonEventArgs` / `MouseLeftButtonDown` | `PointerPressedEventArgs` / `PointerPressed` (check `e.GetCurrentPoint(this).Properties.IsLeftButtonPressed`) |
| `MouseLeftButtonUp` | `PointerReleased` (`PointerReleasedEventArgs`) |
| `MouseWheelEventArgs` / `MouseWheel` (`e.Delta`) | `PointerWheelEventArgs` / `PointerWheelChanged` (`e.Delta.Y`) |
| `MouseLeave` | `PointerExited` |
| `e.GetPosition(el)` | `e.GetPosition(el)` (same) |
| `e.LeftButton == MouseButtonState.Pressed` | `e.GetCurrentPoint(this).Properties.IsLeftButtonPressed` |
| `e.ClickCount` (double-click) | `e.ClickCount` (Avalonia `PointerPressedEventArgs.ClickCount`) |
| `el.CaptureMouse()` / `ReleaseMouseCapture()` | `e.Pointer.Capture(el)` / `e.Pointer.Capture(null)` |
| `Visibility.Collapsed/Visible` | `IsVisible = false/true` |
| `Window` drag on caption | `PointerPressed` → `BeginMoveDrag(e)` |

**Task 6 note (TopBar):** the frameless window has no OS title bar, so the TopBar must be draggable: in `TopBar.axaml.cs` handle `PointerPressed` and call `(this.VisualRoot as Window)?.BeginMoveDrag(e);` (guard to the left button and non-interactive areas). Window min/max/close buttons (if present) call `window.WindowState`/`window.Close()`.

**Task 8 note (GaugeView arc):** the WPF `PathGeometry`/`PathFigure`/`ArcSegment` port to Avalonia geometry. Avalonia `ArcSegment` uses `Point` (end), `Size`, `IsLargeArc` (bool), `SweepDirection` (`Clockwise`/`CounterClockwise`), `RotationAngle`. The values (`StartPoint="24.6,95.4"`, `Point="95.4,95.4"`, `Size="50,50"`, `IsLargeArc="True"`, `SweepDirection="Clockwise"`) carry over. If the gauge value-arc is drawn dynamically in code-behind, port that geometry-building code to `Avalonia.Media.StreamGeometry`/`PathGeometry` equivalently. Confirm the 270° arc renders identically to the reference.

**Task 10 note (Hud + FrameClock):** add `<c:FrameClock x:Name="Clock"/>` to `Hud.axaml`, and in `Hud.axaml.cs` wire `Clock.Rendering += (_, elapsed) => (DataContext as HudViewModel)?.Tick(elapsed);`. This replaces the WPF `CompositionTarget.Rendering` subscription that used to live in `HudViewModel`'s constructor. Confirm the FPS/frame-time numbers update live and freeze when paused (the `IsPaused`/`_wasPaused` logic in `Tick` is preserved).

**Task 12 note (WidgetGridView):** the drag/drop widget reflow uses `Thumb`/`DragDelta` or raw pointer events + a `DropGhost` (`Visibility`→`IsVisible`). Port the pointer handlers per the table; Avalonia has `Thumb` with `DragStarted`/`DragDelta`/`DragCompleted` if the WPF code used it. Keep the reorder logic (which VM method it calls) unchanged.

---

## Task 13: Port `LineChartView` (ScottPlot.Avalonia)

**Files:**
- Create: `Views/LineChartView.axaml`, `Views/LineChartView.axaml.cs`
- Delete: `Views/LineChartView.xaml`, `Views/LineChartView.xaml.cs`
- Modify: csproj — remove the temporary `<Compile Remove="Views\**\*.cs" />` block entirely (all views now ported).

**Interfaces:**
- Consumes: `LineChartViewModel` (unchanged), `AvaPlot` from `ScottPlot.Avalonia`.

- [ ] **Step 1: Translate the XAML** — `WpfPlot` → `AvaPlot`:
```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:sp="using:ScottPlot.Avalonia"
             x:Class="TelemetryPoc.App.Views.LineChartView"
             Background="{StaticResource Panel}">
  <Grid Margin="4">
    <sp:AvaPlot x:Name="Plot" PointerMoved="OnHover" PointerExited="OnHoverLeave" />
    <Border x:Name="Tip" IsVisible="False" Background="{StaticResource Panel2}"
            BorderBrush="{StaticResource Border1}" BorderThickness="1" Padding="5,2"
            HorizontalAlignment="Left" VerticalAlignment="Top" IsHitTestVisible="False">
      <TextBlock x:Name="TipText" Foreground="{StaticResource TextData}"
                 FontFamily="{StaticResource MonoFont}" FontSize="10" />
    </Border>
  </Grid>
</UserControl>
```
- [ ] **Step 2: Translate the code-behind** — the ScottPlot plot-building API (`Plot.Add.*`, axis styling, `Plot.Axes.*`) is shared across backends, so the series/axis code is unchanged. Only:
  - `using ScottPlot.WPF;` → `using ScottPlot.Avalonia;`.
  - `WpfPlot Plot` → `AvaPlot Plot`; refresh call `Plot.Refresh()` is the same.
  - Hover: `OnHover(object sender, MouseEventArgs e)` → `OnHover(object? sender, PointerEventArgs e)`; `Tip.Visibility = Visibility.Collapsed/Visible` → `Tip.IsVisible = false/true`; `e.GetPosition(Plot)` → `e.GetPosition(Plot)`. Tooltip positioning via `Canvas.SetLeft`/margin — Avalonia `Tip.Margin` or `RenderTransform`.
  - `OnHoverLeave(...)` → `PointerExited` handler setting `Tip.IsVisible = false`.
- [ ] **Step 3: Build** — `dotnet build src/TelemetryPoc.App`. Expected PASS with the whole `Views\**` exclusion removed.
- [ ] **Step 4: Launch-confirm** — run the app; confirm a strip chart renders, scrolls with the ride, and the hover tooltip shows the sampled value. Compare against the reference.
- [ ] **Step 5: Commit** — `git commit -m "feat(dotnet): port LineChartView to ScottPlot.Avalonia"`.

---

## Task 14: Port `MapWidgetView` (Skia host + pointer interaction)

**Files:**
- Create: `Views/MapWidgetView.axaml`, `Views/MapWidgetView.axaml.cs`
- Delete: `Views/MapWidgetView.xaml`, `Views/MapWidgetView.xaml.cs`

**Interfaces:**
- Consumes: `SkiaCanvasControl` (Task 4), `MapWidgetViewModel`, and the untouched Presentation-ring drawing (`BasemapRenderer.Render`, `TrackOverlay.Draw`, `MapInteract.Pan/ZoomAt`, `TileMath`, `MapStyle`).

- [ ] **Step 1: Translate the XAML**
```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:c="using:TelemetryPoc.App.Controls"
             x:Class="TelemetryPoc.App.Views.MapWidgetView"
             Background="{StaticResource Panel}">
  <c:SkiaCanvasControl x:Name="Skia" />
</UserControl>
```
- [ ] **Step 2: Translate the code-behind** — this is the largest single port. Rewrite `MapWidgetView.axaml.cs` keeping the caching/preview logic verbatim, changing only the host + input types:
  - `SKElement Skia` + `Skia.PaintSurface += OnPaintSurface` (WPF `SKPaintSurfaceEventArgs`) → `Skia.PaintSurface += OnPaint;` where `OnPaint(SKCanvas canvas, int w, int h)` (the `SkiaCanvasControl` signature). The body is the old `OnPaintSurface` with `e.Surface.Canvas`→`canvas`, `e.Info.Width/Height`→`w`/`h`. `Skia.InvalidateVisual()` unchanged.
  - `DispatcherTimer` (`System.Windows.Threading`) → `Avalonia.Threading.DispatcherTimer` (`_zoomDebounce`), same 140 ms interval + `Tick`.
  - Mouse handlers → pointer handlers per the mapping table:
    - `OnWheel(object, MouseWheelEventArgs)` → `OnWheel(object?, PointerWheelEventArgs)`; `e.Delta > 0` → `e.Delta.Y > 0`; `e.GetPosition(Skia)`→`e.GetPosition(Skia)`; `e.Handled = true` (keep, so the outer ScrollViewer doesn't scroll).
    - `OnDown(object, MouseButtonEventArgs)` → `OnDown(object?, PointerPressedEventArgs)`; `Skia.CaptureMouse()`→`e.Pointer.Capture(Skia)`.
    - `OnMove(object, MouseEventArgs)` → `PointerMoved` handler.
    - `OnUp(object, MouseButtonEventArgs)` → `PointerReleased`; `Skia.ReleaseMouseCapture()`→`e.Pointer.Capture(null)`.
    - `OnMaybeDoubleClick` → merge into `OnDown` using `e.ClickCount >= 2`.
    - Subscribe in the ctor via Avalonia events: `Skia.PointerWheelChanged += OnWheel; Skia.PointerPressed += OnDown; Skia.PointerMoved += OnMove; Skia.PointerReleased += OnUp;`.
  - `Point` (`System.Windows`) → `Avalonia.Point`. `DataContextChanged`/`Loaded`/`Unloaded` → Avalonia `DataContextChanged`/`AttachedToVisualTree`/`DetachedFromVisualTree` (or `Loaded`/`Unloaded`, which Avalonia also has).
  - The basemap raster (`SKSurface.Create(new SKImageInfo(w,h, SKColorType.Bgra8888, SKAlphaType.Premul))` + `Snapshot()`) is plain SkiaSharp — unchanged.
- [ ] **Step 3: Build** — `dotnet build src/TelemetryPoc.App`. Expected PASS.
- [ ] **Step 4: Launch-confirm** — run against a DB with a tileset (`RIDE_MBTILES` → `fixture.mbtiles` or a real `israel.mbtiles`). Confirm: basemap renders, GPS track overlays, wheel zooms around the cursor, drag pans, double-click re-fits, and the debounced rebuild is smooth. Without a tileset, confirm the background + track still draw. Compare to the reference.
- [ ] **Step 5: Commit** — `git commit -m "feat(dotnet): port MapWidgetView to Avalonia Skia host + pointer input"`.

---

## Task 15: Run the full app on Kali (Linux verification)

**Files:** none (verification), plus any Linux-only fixes surfaced.

- [ ] **Step 1: Build the whole solution on Kali via MCP**

On Kali (through `mcp__kali__*`): `cd dotnet && dotnet build`
Expected: PASS on Linux.

- [ ] **Step 2: Run the test suite on Kali** — `dotnet test`. Expected: 170 pass on Linux.

- [ ] **Step 3: Launch the app on the Kali desktop**

The app opens a window, so run it from the VM's desktop session (not a headless MCP shell). With a DB present:
```bash
RIDE_DB=data/ride_small.db dotnet run --project dotnet/src/TelemetryPoc.App
```
Expected: the INU dashboard opens on the Kali desktop. Via MCP, confirm the **process stays alive and logs are clean** (`ps`, tail the app log / stderr); the **user visually confirms** the dashboard against `docs/reference/dashboard-target.md` in the VirtualBox window.

- [ ] **Step 4: Fix any Linux-only issues**

Likely candidates and fixes:
  - Fonts not resolving → confirm the `avares://` family names match the TTFs' internal names.
  - `RIDE_DB` path/casing → Linux is case-sensitive; verify `RidePathResolver` auto-resolve and pass an explicit path first.
  - Missing GL/X11 → install the deps from Task 0 Step 2.
Commit each fix separately with a `fix(dotnet):` message.

- [ ] **Step 5: Commit** — commit any fixes; if none, no commit.

---

## Task 16: Docs + whole-branch review

**Files:**
- Modify: `CLAUDE.md`, `dotnet/` README notes if any, this plan's status.

- [ ] **Step 1: Update `CLAUDE.md`**
  - Replace WPF/ScottPlot.WPF/Mapsui-era descriptions of the .NET app with Avalonia (`TelemetryPoc.App` is now an Avalonia 11 shell; runs on Linux/Windows/macOS).
  - Record the benchmark-meaning change: the comparison is now **Avalonia-Skia vs Rust-Skia** (was WPF-DirectX vs Rust).
  - Update the `dotnet run` command note (unchanged command, but now cross-platform; mention Kali/X11 deps).

- [ ] **Step 2: Full test + build gate**

Run: `cd dotnet && dotnet test`
Expected: 170 pass. Run `dotnet build` — clean, and confirm **NetArchTest ring-boundary tests pass** (the Avalonia shell must not be referenced by inner rings).

- [ ] **Step 3: Whole-branch review** — request code review of the full branch (per repo convention: subagent-driven TDD with a final whole-branch review before merge). Address findings.

- [ ] **Step 4: Commit + open PR**

```bash
git add -A
git commit -m "docs(dotnet): record Avalonia port + Skia-vs-Skia benchmark change"
```
Open a PR summarizing: WPF→Avalonia in-place port, four rings untouched, Linux-verified on Kali, benchmark now Skia-vs-Skia.

---

## Self-Review Notes (author)

- **Spec coverage:** every spec section maps to a task — packages (T1), rings untouched (global constraint + T15 test), theme/fonts (T3), Skia host (T4), shell/DI (T5), plain views incl. gauges (T6–12), charts (T13), map (T14), Kali MCP + verify (T0/T15), tests on Linux (T0/T15), benchmark-note + docs (T16), risks (font→T3 embedded, Skia-lease→T4 spike, ScottPlot→T13, paths→T15).
- **Placeholders:** none — new files have full code; mechanical view ports have an explicit transform recipe + per-file notes + gates (full re-transcription of 2316 lines of existing XAML is deliberately delegated to the recipe, since the executor reads the existing `.xaml` and transforms it).
- **Type consistency:** `FrameClock.Rendering(EventHandler<TimeSpan>)` ↔ `HudViewModel.Tick(TimeSpan)` (T2/T10); `SkiaCanvasControl.PaintSurface(Action<SKCanvas,int,int>)` ↔ map `OnPaint(SKCanvas,int,int)` (T4/T14); `ParamRowViewModel` brushes `IBrush` throughout (T2).
