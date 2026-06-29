# .NET Window Chrome + IBM Plex Fonts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bundle the real IBM Plex fonts and make the WPF window frameless with custom min/maximize/close controls in the header — visual parity with the Rust app.

**Architecture:** Two independent build-verified changes: (1) download IBM Plex `.ttf`, add them as csproj `<Resource>`s, repoint the `Theme.xaml` font families; (2) `WindowStyle=None` + `WindowChrome` on `MainWindow` with custom caption buttons in `TopBar`.

**Tech Stack:** .NET 8 WPF, `System.Windows.Shell.WindowChrome`.

## Global Constraints

- `TelemetryPoc.Core` unchanged. No new pure logic; XAML/window/fonts are build-verified + launch-confirmed.
- Fonts bundled from IBM/plex (`packages/plex-{mono,sans}/fonts/complete/ttf/`), committed (~700 KB total).
- Font families: `MonoFont = "pack://application:,,,/Fonts/#IBM Plex Mono, Consolas"`, `SansFont = "pack://application:,,,/Fonts/#IBM Plex Sans, Segoe UI"` (fallback chain kept).
- Window: `WindowStyle=None` + `WindowChrome` (`CaptionHeight=46` = top-bar height, `ResizeBorderThickness=6`, `GlassFrameThickness=0`, `CornerRadius=0`); native resize/snap/maximize preserved; caption buttons `WindowChrome.IsHitTestVisibleInChrome="True"`; maximize inset handled so content isn't clipped.

## File Structure

- `dotnet/src/TelemetryPoc.App/Fonts/IBMPlexMono-Regular.ttf`, `IBMPlexMono-SemiBold.ttf`, `IBMPlexSans-Regular.ttf`, `IBMPlexSans-SemiBold.ttf` (new, downloaded).
- `dotnet/src/TelemetryPoc.App/TelemetryPoc.App.csproj` (modify — `<Resource>` entries).
- `dotnet/src/TelemetryPoc.App/Resources/Theme.xaml` (modify — `MonoFont`/`SansFont`).
- `dotnet/src/TelemetryPoc.App/MainWindow.xaml` + `.xaml.cs` (modify — WindowChrome + handlers).
- `dotnet/src/TelemetryPoc.App/Views/TopBar.xaml` + `.xaml.cs` (modify — caption buttons).

---

### Task 1: Bundle IBM Plex fonts

**Files:**
- Create: the four `Fonts/*.ttf`
- Modify: `TelemetryPoc.App.csproj`, `Resources/Theme.xaml`

- [ ] **Step 1: Download the four TTFs** into `dotnet/src/TelemetryPoc.App/Fonts/` (use `curl -L`):

```bash
cd dotnet/src/TelemetryPoc.App && mkdir -p Fonts && cd Fonts
B=https://github.com/IBM/plex/raw/master/packages
curl -sL "$B/plex-mono/fonts/complete/ttf/IBMPlexMono-Regular.ttf"  -o IBMPlexMono-Regular.ttf
curl -sL "$B/plex-mono/fonts/complete/ttf/IBMPlexMono-SemiBold.ttf" -o IBMPlexMono-SemiBold.ttf
curl -sL "$B/plex-sans/fonts/complete/ttf/IBMPlexSans-Regular.ttf"  -o IBMPlexSans-Regular.ttf
curl -sL "$B/plex-sans/fonts/complete/ttf/IBMPlexSans-SemiBold.ttf" -o IBMPlexSans-SemiBold.ttf
```
Verify each is a real TrueType file (`file IBMPlexMono-Regular.ttf` → "TrueType Font data", ~100–200 KB each, not an HTML error page). If a SemiBold URL 404s, use `-Medium.ttf` or `-Bold.ttf` instead and note it; Regular is mandatory.

- [ ] **Step 2: Add `<Resource>` entries** to `TelemetryPoc.App.csproj` (a new `<ItemGroup>`):

```xml
<ItemGroup>
  <Resource Include="Fonts\IBMPlexMono-Regular.ttf" />
  <Resource Include="Fonts\IBMPlexMono-SemiBold.ttf" />
  <Resource Include="Fonts\IBMPlexSans-Regular.ttf" />
  <Resource Include="Fonts\IBMPlexSans-SemiBold.ttf" />
</ItemGroup>
```

- [ ] **Step 3: Repoint the font families** in `Resources/Theme.xaml`:

```xml
    <FontFamily x:Key="SansFont">pack://application:,,,/Fonts/#IBM Plex Sans</FontFamily>
    <FontFamily x:Key="MonoFont">pack://application:,,,/Fonts/#IBM Plex Mono</FontFamily>
```

> If WPF can't resolve the embedded family at runtime (blank text), append the fallback: `…/#IBM Plex Mono, Consolas`. The internal family names are "IBM Plex Mono" / "IBM Plex Sans" (verified from the TTF name table).

- [ ] **Step 4: Build + launch-verify** — (from `dotnet/`) `dotnet build`; run the app. Confirm the UI text renders in **IBM Plex** (mono numbers have IBM Plex's distinct shapes; not Consolas). Close it. (Controller does the live check.)

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(dotnet): bundle IBM Plex Mono/Sans fonts"
```

---

### Task 2: Frameless window + caption controls

**Files:**
- Modify: `MainWindow.xaml` + `.xaml.cs`, `Views/TopBar.xaml` + `.xaml.cs`

- [ ] **Step 1: Add WindowChrome to `MainWindow.xaml`** — add the shell namespace + the chrome, set `WindowStyle="None"`, keep `ResizeMode="CanResize"`:

```xml
<Window x:Class="TelemetryPoc.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:views="clr-namespace:TelemetryPoc.App.Views"
        xmlns:shell="clr-namespace:System.Windows.Shell;assembly=PresentationFramework"
        Title="INU-MONITOR (.NET)" Height="900" Width="1600" WindowState="Maximized"
        WindowStyle="None" ResizeMode="CanResize" Background="{StaticResource Bg}">
    <shell:WindowChrome.WindowChrome>
        <shell:WindowChrome CaptionHeight="46" ResizeBorderThickness="6"
                            GlassFrameThickness="0" CornerRadius="0" />
    </shell:WindowChrome.WindowChrome>
    <Grid x:Name="RootGrid">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>
        <views:TopBar x:Name="TopBar" Grid.Row="0" />
        <views:OverviewView x:Name="Overview" Grid.Row="1" />
        <views:TransportBar x:Name="Transport" Grid.Row="2" />
        <views:Hud x:Name="Hud" Grid.RowSpan="3" />
    </Grid>
</Window>
```

- [ ] **Step 2: Handle the maximize inset in `MainWindow.xaml.cs`** — a `WindowStyle=None` window maximized overshoots the work area by the resize border; pad the root when maximized. Add to the constructor (after the existing DataContext wiring) and a handler:

```csharp
        StateChanged += (_, _) =>
            RootGrid.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
```

(`using System.Windows;` is already present.)

- [ ] **Step 3: Add the caption buttons to `Views/TopBar.xaml`** — at the very end of the right-hand `StackPanel` (after the clock), add three buttons; mark the group hit-test-visible in chrome:

```xml
            <StackPanel Orientation="Horizontal" Margin="10,0,0,0"
                        shell:WindowChrome.IsHitTestVisibleInChrome="True"
                        xmlns:shell="clr-namespace:System.Windows.Shell;assembly=PresentationFramework">
                <Button Content="&#9472;" Click="OnMinimize" Style="{StaticResource CaptionButton}" />
                <Button Content="&#9633;" Click="OnMaximize" Style="{StaticResource CaptionButton}" />
                <Button Content="&#10005;" Click="OnClose" Style="{StaticResource CaptionButtonClose}" />
            </StackPanel>
```

Add the two button styles to `Resources/Theme.xaml`:

```xml
    <Style x:Key="CaptionButton" TargetType="Button">
        <Setter Property="Width" Value="30" />
        <Setter Property="Height" Value="24" />
        <Setter Property="Background" Value="Transparent" />
        <Setter Property="Foreground" Value="{StaticResource TextDim}" />
        <Setter Property="BorderThickness" Value="0" />
        <Setter Property="FontFamily" Value="{StaticResource MonoFont}" />
        <Setter Property="FontSize" Value="12" />
        <Setter Property="Cursor" Value="Hand" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Button">
                    <Border x:Name="bd" Background="{TemplateBinding Background}">
                        <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="bd" Property="Background" Value="#FF1A2230" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
    <Style x:Key="CaptionButtonClose" TargetType="Button" BasedOn="{StaticResource CaptionButton}">
        <Style.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
                <Setter Property="Background" Value="{StaticResource Red}" />
                <Setter Property="Foreground" Value="White" />
            </Trigger>
        </Style.Triggers>
    </Style>
```

> Note: the close-button hover red needs the template's `bd` to bind `Background`; the `BasedOn` template already does (`{TemplateBinding Background}`), and the trigger sets the Button `Background`, which flows to `bd`. Keep both styles in `Theme.xaml` (above the font keys is fine).

- [ ] **Step 4: Add the button handlers to `Views/TopBar.xaml.cs`**:

```csharp
using System.Windows;
using System.Windows.Controls;

namespace TelemetryPoc.App.Views;

public partial class TopBar : UserControl
{
    public TopBar() => InitializeComponent();

    private void OnMinimize(object sender, RoutedEventArgs e)
    {
        var w = Window.GetWindow(this);
        if (w is not null) w.WindowState = WindowState.Minimized;
    }

    private void OnMaximize(object sender, RoutedEventArgs e)
    {
        var w = Window.GetWindow(this);
        if (w is not null)
            w.WindowState = w.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Window.GetWindow(this)?.Close();
}
```

> If `TopBar.xaml.cs` already has a different constructor/usings, keep them and just add the three handlers + the `using System.Windows;`.

- [ ] **Step 5: Build + launch-verify** — (from `dotnet/`) `dotnet build`; run the app. Confirm: no OS title bar; the top bar **drags** the window; the **⎯ ▢ ✕** buttons minimize / maximize-restore / close; dragging the window **edges resizes**; maximizing doesn't clip content under the taskbar. Close it. (Controller does the live check.)

- [ ] **Step 6: Full test run + commit** — (from `dotnet/`) `dotnet test` (still green — no logic change). Then:

```bash
git add -A
git commit -m "feat(dotnet): frameless window + custom caption controls"
```

---

## Self-Review

**Spec coverage:** IBM Plex fonts bundled + families repointed (Task 1); frameless `WindowChrome` window + custom ⎯ ▢ ✕ caption controls + drag/resize/maximize-inset (Task 2). ✓

**Placeholder scan:** No TBD/TODO; complete code + exact download commands. The font-download fallback (SemiBold→Medium/Bold) and the family-fallback chain are explicit contingencies, not gaps. ✓

**Type consistency:** `WindowChrome.IsHitTestVisibleInChrome` on the button group; `CaptionButton`/`CaptionButtonClose` styles referenced by the TopBar buttons and defined in `Theme.xaml`; `OnMinimize`/`OnMaximize`/`OnClose` handlers match the XAML `Click` names; `RootGrid` named for the maximize-inset margin. `MonoFont`/`SansFont` keys unchanged (only their values), so every existing `{StaticResource MonoFont/SansFont}` consumer keeps working. ✓

**Note:** the `shell:` namespace is declared on both `MainWindow` (for `WindowChrome`) and inline on the TopBar button group (for `IsHitTestVisibleInChrome`); declaring it locally on the StackPanel avoids touching the `TopBar` root element's existing attributes.
