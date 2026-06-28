# .NET WPF INU Reskin — Phase 1 (Stack Swap + Shell) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move `TelemetryPoc.App` off Blazor Hybrid to native WPF/XAML, apply the INU dark theme, and stand up a themed shell (top bar + empty overview + HUD placeholder) driven by a WPF DispatcherTimer player that replays `ride.db` into the store on the UI thread with a live ticking mission clock.

**Architecture:** The UI-agnostic `TelemetryPoc.Core` is untouched. A new pure class library `TelemetryPoc.App.Viz` (net8.0, no WPF) holds testable helpers (path resolution, clock formatting) so logic is xUnit-tested without a UI. `TelemetryPoc.App` becomes a plain WPF app: `RideSession` (DispatcherTimer) loads the DB and advances the store on the UI thread, raising `INotifyPropertyChanged` clock text that the XAML top bar binds to. WPF views/XAML are build-verified + confirmed by launching (XAML can't be verified headless — the repo's GUI policy).

**Tech Stack:** .NET 8 (`net8.0-windows`, WPF), xUnit, ScottPlot.WPF + Mapsui.Wpf (added now, used in later phases), `Microsoft.Data.Sqlite` (via Core).

## Global Constraints

- Target `net8.0-windows`, `UseWPF=true`, `Nullable=enable`, `ImplicitUsings=enable`. No Blazor packages remain.
- `TelemetryPoc.Core` is **unchanged** in this phase.
- Pure logic lives in `TelemetryPoc.App.Viz` (net8.0, no WPF reference) and is xUnit-tested; XAML/WPF views are build-verified + launch-confirmed only.
- Replay stays **in-process** (no WebSocket). Store `Advance` + metrics run on the **WPF UI thread** (DispatcherTimer is already on the UI thread — no background thread, no `InvokeAsync`).
- INU palette (exact): background `#0a0e14`; panel `#10151d` / `#0c121a`; panel header `#131a24`; border `#1d2632` / `#1a2230`; column-header bg `#0c1119`; text `#c3ccd8`; dim `#566273`; panel-title `#8b98a9`; accent cyan `#38c5e0`; green `#2fd17a`; amber `#f5b440`; red `#ff4d52`; critical-row bg `#1a0e11`.
- Fonts: IBM Plex `.ttf` are **not** bundled this phase (only web woff exists) → `SansFont`/`MonoFont` resources fall back to `Segoe UI` / `Consolas`; real IBM Plex bundling is deferred to Phase 6.
- `RIDE_DB` resolves: env var if the file exists, else walk up from the app base dir for `data/ride.db` then `data/ride_small.db`, else `data/ride.db`. `RIDE_SPEED` (invariant double) defaults `1.0`.

## Existing Core API (consumed, do not change)

- `TelemetryStore` : `Channels` (`IReadOnlyList<ChannelMeta>`), `LastEmitUnixMs` (`long`), `ApplyMeta(channels, enums)`, `ApplyFrame(Sample, long emitUnixMs)`, `ApplyMetrics(Metrics)`, `Latest(long)`, `Series(long)`, `GpsTrack()`.
- `TelemetryDb` (static): `LoadChannels(conn)`, `LoadEnumValues(conn)`, `LoadRideMeta(conn)`, `LoadSamples(conn, channels)`.
- `ReplayPlayer(IReadOnlyList<Sample> samples, TelemetryStore store, double speed)` : `Advance(long elapsedMs, long nowUnixMs) → int`, `Done`.
- `MetricsSampler()` : `Sample() → Metrics(double CpuPct, double RamMb)`.
- `ChannelMeta(long Id, string Name, string ColumnName, string Unit, string Type, double Min, double Max, string Widget, long DisplayOrder, string Addr)`.

## File Structure

- `dotnet/src/TelemetryPoc.App.Viz/TelemetryPoc.App.Viz.csproj` (new, net8.0 classlib)
  - `RidePaths.cs` — pure DB-path resolution.
  - `MissionClock.cs` — pure elapsed-ms → clock strings.
- `dotnet/src/TelemetryPoc.App/TelemetryPoc.App.csproj` (modify — drop Blazor, add ScottPlot.WPF/Mapsui.Wpf + ref Viz)
- Delete: `Components/Dashboard.razor`, `Components/Gauge.razor`, `Components/GpsMap.razor`, `Components/Hud.razor`, `Components/ParamTable.razor`, `Components/StripChart.razor`, `Main.razor`, `_Imports.razor`, `wwwroot/app.css`, `wwwroot/gpsmap.js`, `wwwroot/index.html`.
- `dotnet/src/TelemetryPoc.App/App.xaml` (modify — merge Theme.xaml)
- `dotnet/src/TelemetryPoc.App/Resources/Theme.xaml` (new — palette + font resources)
- `dotnet/src/TelemetryPoc.App/MainWindow.xaml` + `.xaml.cs` (rewrite — host shell, create RideSession)
- `dotnet/src/TelemetryPoc.App/Views/TopBar.xaml` + `.xaml.cs` (new)
- `dotnet/src/TelemetryPoc.App/Views/OverviewView.xaml` + `.xaml.cs` (new — empty themed placeholder)
- `dotnet/src/TelemetryPoc.App/Views/Hud.xaml` + `.xaml.cs` (new — placeholder)
- `dotnet/src/TelemetryPoc.App/RideSession.cs` (new — DispatcherTimer player + clock)
- `dotnet/TelemetryPoc.slnx` (modify — add Viz project)
- `dotnet/tests/TelemetryPoc.Core.Tests/TelemetryPoc.Core.Tests.csproj` (modify — ref App.Viz)
- `dotnet/tests/TelemetryPoc.Core.Tests/RidePathsTests.cs` + `MissionClockTests.cs` (new)

---

### Task 1: csproj swap to native WPF + remove Blazor

Make the project a plain WPF app that builds, with a trivial empty window. This is the demolition + foundation step; later tasks add the shell.

**Files:**
- Modify: `dotnet/src/TelemetryPoc.App/TelemetryPoc.App.csproj`
- Modify: `dotnet/src/TelemetryPoc.App/MainWindow.xaml` + `MainWindow.xaml.cs`
- Modify: `dotnet/src/TelemetryPoc.App/App.xaml` (leave `App.xaml.cs` as-is)
- Delete: all `Components/*.razor`, `Main.razor`, `_Imports.razor`, `wwwroot/*`

**Interfaces:**
- Produces: a buildable WPF `MainWindow` (empty `Grid`), no Blazor types anywhere.

- [ ] **Step 1: Rewrite the csproj** — `dotnet/src/TelemetryPoc.App/TelemetryPoc.App.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\TelemetryPoc.Core\TelemetryPoc.Core.csproj" />
    <ProjectReference Include="..\TelemetryPoc.App.Viz\TelemetryPoc.App.Viz.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="ScottPlot.WPF" Version="5.0.55" />
    <PackageReference Include="Mapsui.Wpf" Version="4.1.9" />
  </ItemGroup>

</Project>
```

> Note: SDK changes from `Microsoft.NET.Sdk.Razor` to `Microsoft.NET.Sdk`. The `App.Viz` ProjectReference is created in Task 2 — to keep Task 1 independently buildable, **temporarily omit the `App.Viz` ProjectReference line** and add it in Task 2's csproj step. (If you implement tasks in order, add it in Task 2.) If a listed NuGet version fails to restore, pick the nearest existing 5.0.x (ScottPlot.WPF) / 4.1.x (Mapsui.Wpf) and note it in the report.

- [ ] **Step 2: Delete the Blazor files**

```bash
cd dotnet/src/TelemetryPoc.App
git rm Components/Dashboard.razor Components/Gauge.razor Components/GpsMap.razor Components/Hud.razor Components/ParamTable.razor Components/StripChart.razor Main.razor _Imports.razor wwwroot/app.css wwwroot/gpsmap.js wwwroot/index.html
```

- [ ] **Step 3: Replace `MainWindow.xaml`** with a plain WPF window:

```xml
<Window x:Class="TelemetryPoc.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="INU-MONITOR (.NET)" Height="900" Width="1600" WindowState="Maximized"
        Background="#0a0e14">
    <Grid />
</Window>
```

- [ ] **Step 4: Replace `MainWindow.xaml.cs`** (drop all Blazor wiring):

```csharp
using System.Windows;

namespace TelemetryPoc.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 5: Simplify `App.xaml`** (remove the empty resources block for now; Theme is merged in Task 3):

```xml
<Application x:Class="TelemetryPoc.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             StartupUri="MainWindow.xaml" />
```

- [ ] **Step 6: Build the solution + run existing tests**

Run (from `dotnet/`): `dotnet build` then `dotnet test`
Expected: build succeeds (plain WPF, no Blazor); the existing Core xUnit tests still pass.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(dotnet): swap App from Blazor Hybrid to native WPF (empty shell)"
```

---

### Task 2: App.Viz class library — pure path + clock helpers (xUnit)

**Files:**
- Create: `dotnet/src/TelemetryPoc.App.Viz/TelemetryPoc.App.Viz.csproj`
- Create: `dotnet/src/TelemetryPoc.App.Viz/RidePaths.cs`
- Create: `dotnet/src/TelemetryPoc.App.Viz/MissionClock.cs`
- Modify: `dotnet/TelemetryPoc.slnx` (register the project)
- Modify: `dotnet/src/TelemetryPoc.App/TelemetryPoc.App.csproj` (add the `App.Viz` ProjectReference from Task 1's note)
- Modify: `dotnet/tests/TelemetryPoc.Core.Tests/TelemetryPoc.Core.Tests.csproj` (add `App.Viz` ProjectReference)
- Create: `dotnet/tests/TelemetryPoc.Core.Tests/RidePathsTests.cs`, `MissionClockTests.cs`

**Interfaces:**
- Produces:
  - `RidePaths.Resolve(string? env, string baseDir, Func<string,bool> exists) → string`
  - `MissionClock.Format(long ms) → string` (`"HH:MM:SS.mmm"`, zero-padded; negatives clamp to 0)
  - `MissionClock.FormatTPlus(long ms) → string` (`"T+HH:MM:SS.mmm"`)

- [ ] **Step 1: Create the classlib csproj** — `dotnet/src/TelemetryPoc.App.Viz/TelemetryPoc.App.Viz.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Register it in `dotnet/TelemetryPoc.slnx`** under `/src/`:

```xml
<Project Path="src/TelemetryPoc.App.Viz/TelemetryPoc.App.Viz.csproj" />
```
(add as a sibling of the existing `src/` project lines)

- [ ] **Step 3: Add the App.Viz ProjectReference to the App csproj** (the line deferred from Task 1) inside the existing `<ItemGroup>` of ProjectReferences in `TelemetryPoc.App.csproj`:

```xml
<ProjectReference Include="..\TelemetryPoc.App.Viz\TelemetryPoc.App.Viz.csproj" />
```

- [ ] **Step 4: Add the test ProjectReference** in `dotnet/tests/TelemetryPoc.Core.Tests/TelemetryPoc.Core.Tests.csproj` (inside the existing ProjectReference `<ItemGroup>`):

```xml
<ProjectReference Include="..\..\src\TelemetryPoc.App.Viz\TelemetryPoc.App.Viz.csproj" />
```

- [ ] **Step 5: Write the failing tests** — `dotnet/tests/TelemetryPoc.Core.Tests/RidePathsTests.cs`:

```csharp
using TelemetryPoc.App.Viz;
using Xunit;

public class RidePathsTests
{
    [Fact]
    public void Env_path_used_when_it_exists()
    {
        var r = RidePaths.Resolve(@"C:\rides\my.db", @"C:\app", p => p == @"C:\rides\my.db");
        Assert.Equal(@"C:\rides\my.db", r);
    }

    [Fact]
    public void Walks_up_for_data_ride_db()
    {
        // base dir C:\repo\dotnet\bin ; data\ride.db lives at C:\repo\data\ride.db
        var hit = @"C:\repo\data\ride.db";
        var r = RidePaths.Resolve(null, @"C:\repo\dotnet\bin", p => p == hit);
        Assert.Equal(hit, r);
    }

    [Fact]
    public void Prefers_ride_db_over_ride_small_db_in_same_dir()
    {
        bool Exists(string p) => p == @"C:\repo\data\ride.db" || p == @"C:\repo\data\ride_small.db";
        var r = RidePaths.Resolve(null, @"C:\repo\dotnet", Exists);
        Assert.Equal(@"C:\repo\data\ride.db", r);
    }

    [Fact]
    public void Falls_back_to_default_when_nothing_found()
    {
        var r = RidePaths.Resolve(null, @"C:\nope", _ => false);
        Assert.Equal(Path.Combine("data", "ride.db"), r);
    }
}
```

`dotnet/tests/TelemetryPoc.Core.Tests/MissionClockTests.cs`:

```csharp
using TelemetryPoc.App.Viz;
using Xunit;

public class MissionClockTests
{
    [Theory]
    [InlineData(0, "00:00:00.000")]
    [InlineData(5000, "00:00:05.000")]
    [InlineData(65_432, "00:01:05.432")]
    [InlineData(3_661_007, "01:01:01.007")]
    [InlineData(-50, "00:00:00.000")]
    public void Format_is_hms_millis(long ms, string expected)
        => Assert.Equal(expected, MissionClock.Format(ms));

    [Fact]
    public void FormatTPlus_prefixes()
        => Assert.Equal("T+00:00:05.000", MissionClock.FormatTPlus(5000));
}
```

- [ ] **Step 6: Run tests → fail** — (from `dotnet/`) `dotnet test`
Expected: compile error / FAIL — `RidePaths` / `MissionClock` not defined.

- [ ] **Step 7: Implement `RidePaths.cs`**:

```csharp
namespace TelemetryPoc.App.Viz;

public static class RidePaths
{
    public static string Resolve(string? env, string baseDir, Func<string, bool> exists)
    {
        if (!string.IsNullOrWhiteSpace(env) && exists(env)) return env;
        var dir = new DirectoryInfo(baseDir);
        while (dir is not null)
        {
            foreach (var name in new[] { "ride.db", "ride_small.db" })
            {
                var p = Path.Combine(dir.FullName, "data", name);
                if (exists(p)) return p;
            }
            dir = dir.Parent;
        }
        return env ?? Path.Combine("data", "ride.db");
    }
}
```

- [ ] **Step 8: Implement `MissionClock.cs`**:

```csharp
using System.Globalization;

namespace TelemetryPoc.App.Viz;

public static class MissionClock
{
    public static string Format(long ms)
    {
        if (ms < 0) ms = 0;
        var t = TimeSpan.FromMilliseconds(ms);
        return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}.{3:000}",
            (int)t.TotalHours, t.Minutes, t.Seconds, t.Milliseconds);
    }

    public static string FormatTPlus(long ms) => "T+" + Format(ms);
}
```

- [ ] **Step 9: Run tests → pass** — (from `dotnet/`) `dotnet test`
Expected: all green (Core tests + 6 new viz tests).

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "feat(dotnet): App.Viz classlib — RidePaths + MissionClock (xUnit)"
```

---

### Task 3: INU theme + shell views (TopBar / OverviewView / Hud)

Add the dark theme resources and the static themed shell. No live data yet — clock binding comes in Task 4.

**Files:**
- Create: `dotnet/src/TelemetryPoc.App/Resources/Theme.xaml`
- Modify: `dotnet/src/TelemetryPoc.App/App.xaml` (merge Theme.xaml)
- Create: `Views/TopBar.xaml` + `.xaml.cs`, `Views/OverviewView.xaml` + `.xaml.cs`, `Views/Hud.xaml` + `.xaml.cs`
- Modify: `MainWindow.xaml` (compose the shell)

**Interfaces:**
- Produces: `TopBar`, `OverviewView`, `Hud` `UserControl`s; theme brushes keyed `Bg`, `Panel`, `PanelHeader`, `Border1`, `TextData`, `TextDim`, `PanelTitle`, `AccentCyan`, `Green`, `Amber`, `Red`, `CriticalBg`; font families `SansFont`, `MonoFont`.

- [ ] **Step 1: Create `Resources/Theme.xaml`**:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
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

    <!-- IBM Plex .ttf not bundled this phase (Phase 6) → system fallbacks -->
    <FontFamily x:Key="SansFont">Segoe UI</FontFamily>
    <FontFamily x:Key="MonoFont">Consolas</FontFamily>
</ResourceDictionary>
```

- [ ] **Step 2: Merge Theme into `App.xaml`**:

```xml
<Application x:Class="TelemetryPoc.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             StartupUri="MainWindow.xaml">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="Resources/Theme.xaml" />
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

- [ ] **Step 3: Create `Views/TopBar.xaml`** (brand + tab + status pills + clock; clock text is a named element bound in Task 4):

```xml
<UserControl x:Class="TelemetryPoc.App.Views.TopBar"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Height="46" Background="{StaticResource Panel2}">
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="Auto" />
            <ColumnDefinition Width="*" />
            <ColumnDefinition Width="Auto" />
        </Grid.ColumnDefinitions>
        <StackPanel Grid.Column="0" Orientation="Horizontal" VerticalAlignment="Center" Margin="12,0">
            <TextBlock Text="INU&#183;MONITOR" Foreground="{StaticResource AccentCyan}"
                       FontFamily="{StaticResource SansFont}" FontWeight="Bold" FontSize="16" />
            <TextBlock Text="AC 4X-ELT / FLT 1182" Foreground="{StaticResource TextDim}"
                       FontFamily="{StaticResource MonoFont}" FontSize="11"
                       VerticalAlignment="Center" Margin="14,0,0,0" />
        </StackPanel>
        <TextBlock Grid.Column="1" Text="OVERVIEW" HorizontalAlignment="Center" VerticalAlignment="Center"
                   Foreground="{StaticResource AccentCyan}" FontFamily="{StaticResource SansFont}"
                   FontSize="12" FontWeight="SemiBold" />
        <StackPanel Grid.Column="2" Orientation="Horizontal" VerticalAlignment="Center" Margin="12,0">
            <TextBlock Text="LINK 1553B&#183;OK" Foreground="{StaticResource Green}"
                       FontFamily="{StaticResource MonoFont}" FontSize="11" Margin="0,0,16,0" />
            <TextBlock x:Name="ClockText" Text="00:00:00.000" Foreground="{StaticResource TextData}"
                       FontFamily="{StaticResource MonoFont}" FontSize="13" />
        </StackPanel>
    </Grid>
</UserControl>
```

`Views/TopBar.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace TelemetryPoc.App.Views;

public partial class TopBar : UserControl
{
    public TopBar() => InitializeComponent();
}
```

- [ ] **Step 4: Create `Views/OverviewView.xaml`** (empty themed placeholder):

```xml
<UserControl x:Class="TelemetryPoc.App.Views.OverviewView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Background="{StaticResource Bg}">
    <TextBlock Text="OVERVIEW" Foreground="{StaticResource TextDim}"
               FontFamily="{StaticResource MonoFont}" FontSize="12"
               HorizontalAlignment="Center" VerticalAlignment="Center" />
</UserControl>
```

`Views/OverviewView.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace TelemetryPoc.App.Views;

public partial class OverviewView : UserControl
{
    public OverviewView() => InitializeComponent();
}
```

- [ ] **Step 5: Create `Views/Hud.xaml`** (placeholder overlay, top-right):

```xml
<UserControl x:Class="TelemetryPoc.App.Views.Hud"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             HorizontalAlignment="Right" VerticalAlignment="Top" Margin="0,52,12,0">
    <Border Background="{StaticResource Panel}" BorderBrush="{StaticResource Border1}"
            BorderThickness="1" CornerRadius="4" Padding="8,4">
        <TextBlock Text="HUD" Foreground="{StaticResource TextDim}"
                   FontFamily="{StaticResource MonoFont}" FontSize="10" />
    </Border>
</UserControl>
```

`Views/Hud.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace TelemetryPoc.App.Views;

public partial class Hud : UserControl
{
    public Hud() => InitializeComponent();
}
```

- [ ] **Step 6: Compose the shell in `MainWindow.xaml`**:

```xml
<Window x:Class="TelemetryPoc.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:views="clr-namespace:TelemetryPoc.App.Views"
        Title="INU-MONITOR (.NET)" Height="900" Width="1600" WindowState="Maximized"
        Background="{StaticResource Bg}">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>
        <views:TopBar x:Name="TopBar" Grid.Row="0" />
        <views:OverviewView Grid.Row="1" />
        <views:Hud Grid.RowSpan="2" />
    </Grid>
</Window>
```

- [ ] **Step 7: Build + verify it launches** — (from `dotnet/`) `dotnet build`; then `dotnet run --project src/TelemetryPoc.App`. Confirm a dark window with the INU top bar (cyan brand, OVERVIEW, green LINK, mono clock) + empty overview + HUD chip. Close it.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat(dotnet): INU theme + shell (TopBar/OverviewView/Hud)"
```

---

### Task 4: RideSession — DispatcherTimer player + live mission clock

Load the DB, replay into the store on the UI thread, and tick the top-bar clock.

**Files:**
- Create: `dotnet/src/TelemetryPoc.App/RideSession.cs`
- Modify: `dotnet/src/TelemetryPoc.App/MainWindow.xaml.cs` (create session, bind clock)

**Interfaces:**
- Consumes: `RidePaths.Resolve`, `MissionClock.Format`, Core `TelemetryStore`/`TelemetryDb`/`ReplayPlayer`/`MetricsSampler`.
- Produces: `RideSession : INotifyPropertyChanged` with `TelemetryStore Store`, `string ClockText`, `string TPlusText`, `string? Error`, `void Start()`.

- [ ] **Step 1: Implement `RideSession.cs`**:

```csharp
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using TelemetryPoc.App.Viz;
using TelemetryPoc.Core;

namespace TelemetryPoc.App;

public sealed class RideSession : INotifyPropertyChanged
{
    public TelemetryStore Store { get; } = new();
    private readonly MetricsSampler _metrics = new();
    private readonly Stopwatch _sw = new();
    private ReplayPlayer? _player;
    private DispatcherTimer? _timer;
    private double _speed = 1.0;
    private long _lastMetricSec = -1;

    private string _clockText = "00:00:00.000";
    public string ClockText { get => _clockText; private set { _clockText = value; Raise(nameof(ClockText)); } }

    private string _tplus = "T+00:00:00.000";
    public string TPlusText { get => _tplus; private set { _tplus = value; Raise(nameof(TPlusText)); } }

    public string? Error { get; private set; }

    public void Start()
    {
        try
        {
            var dbPath = RidePaths.Resolve(
                Environment.GetEnvironmentVariable("RIDE_DB"), AppContext.BaseDirectory, File.Exists);
            _speed = double.TryParse(Environment.GetEnvironmentVariable("RIDE_SPEED"),
                NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : 1.0;

            using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            conn.Open();
            var channels = TelemetryDb.LoadChannels(conn);
            var enums = TelemetryDb.LoadEnumValues(conn);
            var samples = TelemetryDb.LoadSamples(conn, channels);
            Store.ApplyMeta(channels, enums);
            _player = new ReplayPlayer(samples, Store, _speed);

            _sw.Start();
            _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
            _timer.Tick += (_, _) => Tick();
            _timer.Start();
        }
        catch (Exception ex)
        {
            Error = $"DB load failed: {ex.Message}";
            Raise(nameof(Error));
        }
    }

    private void Tick()
    {
        var elapsed = _sw.ElapsedMilliseconds;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _player?.Advance(elapsed, now);

        var rideMs = (long)(elapsed * _speed);
        var rideSec = rideMs / 1000;
        if (Store.LastEmitUnixMs > 0 && rideSec != _lastMetricSec)
        {
            _lastMetricSec = rideSec;
            Store.ApplyMetrics(_metrics.Sample());
        }

        ClockText = MissionClock.Format(rideMs);
        TPlusText = MissionClock.FormatTPlus(rideMs);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
```

- [ ] **Step 2: Wire it in `MainWindow.xaml.cs`** (create the session, bind the top-bar clock):

```csharp
using System.Windows;
using System.Windows.Data;

namespace TelemetryPoc.App;

public partial class MainWindow : Window
{
    private readonly RideSession _session = new();

    public MainWindow()
    {
        InitializeComponent();
        TopBar.ClockText.SetBinding(System.Windows.Controls.TextBlock.TextProperty,
            new Binding(nameof(RideSession.ClockText)) { Source = _session });
        Loaded += (_, _) => _session.Start();
    }
}
```

> Note: `TopBar.ClockText` is the named `TextBlock` from Task 3. Binding its `Text` to the session's `ClockText` makes the clock tick.

- [ ] **Step 3: Build + run** — (from `dotnet/`) `dotnet build`; then generate a DB if needed (`python ../data/simulate.py --out ../data/ride_small.db --duration 10 --rate 10`) and `dotnet run --project src/TelemetryPoc.App`. Confirm the top-bar clock **ticks** (mission time advances) and no DB error. Close it.

- [ ] **Step 4: Run the full test suite** — (from `dotnet/`) `dotnet test` → all green.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(dotnet): RideSession DispatcherTimer player + live mission clock"
```

---

## Self-Review

**Spec coverage (Phase 1 scope):**
- csproj Blazor→WPF, remove Blazor packages/files, add ScottPlot.WPF + Mapsui.Wpf — Task 1 (+ refs in Task 2). ✓
- Theme.xaml (INU palette; IBM Plex deferred to fallback per constraint) — Task 3. ✓
- MainWindow + TopBar + empty OverviewView + Hud placeholder — Task 3. ✓
- DispatcherTimer player loads ride.db (reusing ResolveDbPath/store-init), advances store on UI thread, live clock — Task 4. ✓
- App launches showing themed shell + ticking clock — Task 4 Step 3. ✓
- Pure logic xUnit-tested (RidePaths, MissionClock); XAML build/launch-verified — Tasks 2/3/4. ✓

**Placeholder scan:** No TBD/TODO. Every code step carries complete code. The only deferral (IBM Plex .ttf) is an explicit, justified constraint with a working fallback, not a gap. ✓

**Type consistency:** `RidePaths.Resolve(string?, string, Func<string,bool>)`, `MissionClock.Format(long)`/`FormatTPlus(long)`, `RideSession.ClockText`, `TopBar.ClockText` (named TextBlock) — names/signatures match across Tasks 2→4. Core API used matches the documented signatures. ✓

**Note for the implementer:** ScottPlot.WPF / Mapsui.Wpf are added now but unused until later phases — they must restore (build) but need no code here. If exact versions fail to restore, fall back to the nearest existing and record it.
