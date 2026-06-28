# .NET WPF INU Reskin — Phase 2 (Live PARAMETERS Panel) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fill the OVERVIEW left column with the INU **PARAMETERS** panel — a grouped, live table of the ~30 channels with status dots, severity colors, critical-row highlight, and an `ALL · N CH` count — updated every replay tick.

**Architecture:** Pure logic (the group table + the per-row display computation) lives in `TelemetryPoc.App.Viz` (now referencing `TelemetryPoc.Core` for `ChannelMeta`/`ValueFormat`) and is xUnit-tested. Thin WPF ViewModels wrap those pure builders, convert hex→`Brush`, and refresh on a `RideSession` tick event. `ParamPanel.xaml` renders the grouped `ItemsControl`. Mirrors the Rust app's `groups.ts` + `paramrow.ts` so the two stacks group/color identically.

**Tech Stack:** .NET 8 WPF, xUnit, the existing `TelemetryPoc.Core` data layer.

## Global Constraints

- `TelemetryPoc.Core` stays **unchanged** (its `ValueFormat.SeverityColor` returns the OLD palette `#d22/#2a2/#888` — do NOT use it for the INU panel; a new INU mapper lives in Viz).
- Pure logic (grouping, row display) goes in `TelemetryPoc.App.Viz` and is xUnit-tested; WPF ViewModels + XAML are build-verified + launch-confirmed.
- Replay + UI updates stay on the WPF UI thread (the `RideSession` DispatcherTimer).
- INU palette for rows (exact): value text `#c3ccd8`; dim `#566273`; ok-green `#2fd17a`; critical-red `#ff4d52`; critical-row bg `#1a0e11`; panel `#10151d`; panel-title `#8b98a9`; border `#1d2632`; column-header bg `#0c1119`; addr `#4f5a68`.
- Grouping mirrors Rust `groups.ts` exactly: groups + member columns + order below; unknown columns → `System`; within a group, sort by `DisplayOrder`.
- Row display mirrors Rust `paramrow.ts`: null latest → `"—"` dim; enum rows take enum severity, non-enum rows are `"ok"`; `critical` ⇔ severity `"critical"`.

## Grouping table (verbatim, mirrors `rust/src/ui/app/groups.ts`)

```
INU Mode:     inu_mode1, inu_mode2
Velocity:     vel_x, vel_y, vel_z, plat_azim, vclimb
Attitude:     roll, pitch, heading_t, heading_m, sky_pitch, sky_roll, sky_azim, sky_heading, prsnt_head
Acceleration: acc_x, acc_y, acc_z
Body Rates:   roll_r, pitch_r, yaw_r
Position:     lat, lon
(anything else) -> System
ORDER: INU Mode, Velocity, Attitude, Acceleration, Body Rates, Position, System
```

## Existing API (consumed)

- `TelemetryStore`: `Channels` (`IReadOnlyList<ChannelMeta>`), `Latest(long) → double?`, `EnumIndex` (`IReadOnlyDictionary<(long,long), EnumValue>`).
- `ChannelMeta(long Id, string Name, string ColumnName, string Unit, string Type, double Min, double Max, string Widget, long DisplayOrder, string Addr)`.
- `EnumValue(long ChannelId, long Code, string Label, string Severity)`.
- `ValueFormat.FormatValue(ChannelMeta, double, index) → string`, `ValueFormat.DecodeEnum(long, double, index) → EnumValue?`. (`index` type: `IReadOnlyDictionary<(long ChannelId, long Code), EnumValue>` — same shape as `TelemetryStore.EnumIndex`.)
- `RideSession` (Phase 1): `TelemetryStore Store`, `Start()`, DispatcherTimer `Tick`.

## File Structure

- `dotnet/src/TelemetryPoc.App.Viz/TelemetryPoc.App.Viz.csproj` — add a ProjectReference to Core.
- `dotnet/src/TelemetryPoc.App.Viz/ParamGrouping.cs` (new) — group table + `Group()`.
- `dotnet/src/TelemetryPoc.App.Viz/Severity.cs` (new) — `Hex(severity) → INU hex`.
- `dotnet/src/TelemetryPoc.App.Viz/ParamRowView.cs` (new) — `Build()` → `ParamRowDisplay`.
- `dotnet/tests/TelemetryPoc.Core.Tests/ParamGroupingTests.cs`, `ParamRowViewTests.cs` (new).
- `dotnet/src/TelemetryPoc.App/RideSession.cs` (modify) — add `MetaLoaded` + `Ticked` events.
- `dotnet/src/TelemetryPoc.App/ViewModels/ParamRowViewModel.cs`, `ParamGroupViewModel.cs`, `OverviewViewModel.cs` (new).
- `dotnet/src/TelemetryPoc.App/Views/ParamPanel.xaml` + `.xaml.cs` (new).
- `dotnet/src/TelemetryPoc.App/Views/OverviewView.xaml` (modify) — host ParamPanel in a left column.
- `dotnet/src/TelemetryPoc.App/MainWindow.xaml.cs` (modify) — build `OverviewViewModel`, set `OverviewView.DataContext`.
- `dotnet/src/TelemetryPoc.App/Views/OverviewView.xaml.cs` (modify) — expose the hosted `ParamPanel` DataContext via the control DataContext.

---

### Task 1: Viz grouping + severity color (pure, xUnit)

**Files:**
- Modify: `dotnet/src/TelemetryPoc.App.Viz/TelemetryPoc.App.Viz.csproj` (ref Core)
- Create: `ParamGrouping.cs`, `Severity.cs`
- Create: `dotnet/tests/TelemetryPoc.Core.Tests/ParamGroupingTests.cs`

**Interfaces:**
- Produces:
  - `record ParamGroup(string Name, IReadOnlyList<ChannelMeta> Channels)`
  - `ParamGrouping.GroupOf(string columnName) → string`
  - `ParamGrouping.Group(IReadOnlyList<ChannelMeta> channels) → IReadOnlyList<ParamGroup>` (ORDER-ordered, empty groups dropped, each group sorted by `DisplayOrder`)
  - `Severity.Hex(string? severity) → string` (`"critical"→"#FFFF4D52"`, `"ok"→"#FF2FD17A"`, else `"#FF566273"`)

- [ ] **Step 1: Add the Core reference** to `TelemetryPoc.App.Viz.csproj` (new `<ItemGroup>`):

```xml
<ItemGroup>
  <ProjectReference Include="..\TelemetryPoc.Core\TelemetryPoc.Core.csproj" />
</ItemGroup>
```

- [ ] **Step 2: Write the failing test** — `dotnet/tests/TelemetryPoc.Core.Tests/ParamGroupingTests.cs`:

```csharp
using TelemetryPoc.App.Viz;
using TelemetryPoc.Core;
using Xunit;

public class ParamGroupingTests
{
    private static ChannelMeta Ch(string col, long order, string widget = "table")
        => new(order, col.ToUpperInvariant(), col, "", "real", 0, 1, widget, order, "I_01");

    [Theory]
    [InlineData("roll", "Attitude")]
    [InlineData("vel_x", "Velocity")]
    [InlineData("acc_z", "Acceleration")]
    [InlineData("lat", "Position")]
    [InlineData("inu_mode2", "INU Mode")]
    [InlineData("something_else", "System")]
    public void GroupOf_maps_columns(string col, string group)
        => Assert.Equal(group, ParamGrouping.GroupOf(col));

    [Fact]
    public void Group_orders_groups_and_sorts_by_display_order()
    {
        var channels = new[]
        {
            Ch("acc_z", 5), Ch("roll", 3), Ch("pitch", 2), Ch("inu_mode1", 1),
        };
        var groups = ParamGrouping.Group(channels);
        // ORDER: INU Mode before Attitude before Acceleration; empty groups dropped
        Assert.Equal(new[] { "INU Mode", "Attitude", "Acceleration" }, groups.Select(g => g.Name).ToArray());
        // Attitude sorted by DisplayOrder: pitch(2) before roll(3)
        Assert.Equal(new[] { "pitch", "roll" }, groups[1].Channels.Select(c => c.ColumnName).ToArray());
    }

    [Fact]
    public void Severity_hex_maps_to_inu_palette()
    {
        Assert.Equal("#FFFF4D52", Severity.Hex("critical"));
        Assert.Equal("#FF2FD17A", Severity.Hex("ok"));
        Assert.Equal("#FF566273", Severity.Hex(null));
        Assert.Equal("#FF566273", Severity.Hex("whatever"));
    }
}
```

- [ ] **Step 3: Run → fail** — (from `dotnet/`) `dotnet test --filter ParamGroupingTests`
Expected: compile error — types not defined.

- [ ] **Step 4: Implement `Severity.cs`**:

```csharp
namespace TelemetryPoc.App.Viz;

public static class Severity
{
    public static string Hex(string? severity) => severity switch
    {
        "critical" => "#FFFF4D52",
        "ok" => "#FF2FD17A",
        _ => "#FF566273",
    };
}
```

- [ ] **Step 5: Implement `ParamGrouping.cs`**:

```csharp
using TelemetryPoc.Core;

namespace TelemetryPoc.App.Viz;

public sealed record ParamGroup(string Name, IReadOnlyList<ChannelMeta> Channels);

public static class ParamGrouping
{
    private static readonly (string Group, string[] Cols)[] Groups =
    {
        ("INU Mode", new[] { "inu_mode1", "inu_mode2" }),
        ("Velocity", new[] { "vel_x", "vel_y", "vel_z", "plat_azim", "vclimb" }),
        ("Attitude", new[] { "roll", "pitch", "heading_t", "heading_m", "sky_pitch", "sky_roll", "sky_azim", "sky_heading", "prsnt_head" }),
        ("Acceleration", new[] { "acc_x", "acc_y", "acc_z" }),
        ("Body Rates", new[] { "roll_r", "pitch_r", "yaw_r" }),
        ("Position", new[] { "lat", "lon" }),
    };

    private static readonly Dictionary<string, string> GroupOfMap = BuildMap();
    private static readonly string[] Order = Groups.Select(g => g.Group).Append("System").ToArray();

    private static Dictionary<string, string> BuildMap()
    {
        var m = new Dictionary<string, string>();
        foreach (var (group, cols) in Groups)
            foreach (var c in cols) m[c] = group;
        return m;
    }

    public static string GroupOf(string columnName)
        => GroupOfMap.TryGetValue(columnName, out var g) ? g : "System";

    public static IReadOnlyList<ParamGroup> Group(IReadOnlyList<ChannelMeta> channels)
    {
        var buckets = new Dictionary<string, List<ChannelMeta>>();
        foreach (var ch in channels)
        {
            var g = GroupOf(ch.ColumnName);
            if (!buckets.TryGetValue(g, out var list)) buckets[g] = list = new List<ChannelMeta>();
            list.Add(ch);
        }
        var result = new List<ParamGroup>();
        foreach (var group in Order)
        {
            if (!buckets.TryGetValue(group, out var list) || list.Count == 0) continue;
            list.Sort((a, b) => a.DisplayOrder.CompareTo(b.DisplayOrder));
            result.Add(new ParamGroup(group, list));
        }
        return result;
    }
}
```

- [ ] **Step 6: Run → pass** — `dotnet test --filter ParamGroupingTests` (all green), then full `dotnet test`.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(dotnet): App.Viz ParamGrouping + INU severity color (xUnit)"
```

---

### Task 2: Viz per-row display builder (pure, xUnit)

**Files:**
- Create: `dotnet/src/TelemetryPoc.App.Viz/ParamRowView.cs`
- Create: `dotnet/tests/TelemetryPoc.Core.Tests/ParamRowViewTests.cs`

**Interfaces:**
- Consumes: `ParamGrouping`/`Severity` (Task 1), Core `ValueFormat`/`ChannelMeta`/`EnumValue`.
- Produces:
  - `record ParamRowDisplay(string Text, string ValueColor, string DotColor, bool Critical)`
  - `ParamRowView.Build(ChannelMeta ch, double? latest, IReadOnlyDictionary<(long,long), EnumValue> index) → ParamRowDisplay`

Mirrors Rust `paramrow.ts`: null latest → `"—"`, value dim, dot dim, not critical. Else `Text = ValueFormat.FormatValue`; severity = (enum row → decoded severity, else `"ok"`); `Critical = severity=="critical"`; `DotColor = Severity.Hex(severity)`; `ValueColor` = critical → red; ok+enum → green; else value-text `#FFC3CCD8`.

- [ ] **Step 1: Write the failing test** — `ParamRowViewTests.cs`:

```csharp
using TelemetryPoc.App.Viz;
using TelemetryPoc.Core;
using Xunit;

public class ParamRowViewTests
{
    private static ChannelMeta Real(string col)
        => new(1, col.ToUpperInvariant(), col, "deg", "real", -10, 10, "table", 1, "I_01");
    private static ChannelMeta Enum(long id, string col)
        => new(id, col.ToUpperInvariant(), col, "", "enum", 0, 1, "table", 1, "I_01");

    private static readonly IReadOnlyDictionary<(long, long), EnumValue> Idx = ValueFormat.BuildEnumIndex(new[]
    {
        new EnumValue(7, 0, "Normal", "ok"),
        new EnumValue(7, 1, "Critical", "critical"),
    });

    [Fact]
    public void Null_latest_is_dash_dim()
    {
        var d = ParamRowView.Build(Real("roll"), null, Idx);
        Assert.Equal("—", d.Text);
        Assert.Equal("#FF566273", d.ValueColor);
        Assert.Equal("#FF566273", d.DotColor);
        Assert.False(d.Critical);
    }

    [Fact]
    public void Real_row_is_ok_value_text_color()
    {
        var d = ParamRowView.Build(Real("roll"), 1.5, Idx);
        Assert.Equal("1.500", d.Text);
        Assert.Equal("#FFC3CCD8", d.ValueColor);   // non-enum ok → plain text color
        Assert.Equal("#FF2FD17A", d.DotColor);     // ok → green dot
        Assert.False(d.Critical);
    }

    [Fact]
    public void Enum_ok_is_green_value()
    {
        var d = ParamRowView.Build(Enum(7, "inu_mode1"), 0, Idx);
        Assert.Equal("Normal", d.Text);
        Assert.Equal("#FF2FD17A", d.ValueColor);   // enum ok → green value
        Assert.False(d.Critical);
    }

    [Fact]
    public void Enum_critical_is_red_and_flagged()
    {
        var d = ParamRowView.Build(Enum(7, "inu_mode2"), 1, Idx);
        Assert.Equal("Critical", d.Text);
        Assert.Equal("#FFFF4D52", d.ValueColor);
        Assert.Equal("#FFFF4D52", d.DotColor);
        Assert.True(d.Critical);
    }
}
```

- [ ] **Step 2: Run → fail** — `dotnet test --filter ParamRowViewTests`

- [ ] **Step 3: Implement `ParamRowView.cs`**:

```csharp
using TelemetryPoc.Core;

namespace TelemetryPoc.App.Viz;

public sealed record ParamRowDisplay(string Text, string ValueColor, string DotColor, bool Critical);

public static class ParamRowView
{
    private const string TextColor = "#FFC3CCD8";
    private const string Dim = "#FF566273";
    private const string Green = "#FF2FD17A";
    private const string Red = "#FFFF4D52";

    public static ParamRowDisplay Build(ChannelMeta ch, double? latest,
        IReadOnlyDictionary<(long, long), EnumValue> index)
    {
        if (latest is null)
            return new ParamRowDisplay("—", Dim, Dim, false);

        var text = ValueFormat.FormatValue(ch, latest.Value, index);
        string? severity = ch.Type == "enum"
            ? ValueFormat.DecodeEnum(ch.Id, latest.Value, index)?.Severity
            : "ok";
        var critical = severity == "critical";
        var dot = Severity.Hex(severity);
        var valueColor = critical ? Red
            : severity == "ok" && ch.Type == "enum" ? Green
            : TextColor;
        return new ParamRowDisplay(text, valueColor, dot, critical);
    }
}
```

- [ ] **Step 4: Run → pass** — `dotnet test --filter ParamRowViewTests`, then full `dotnet test`.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(dotnet): App.Viz ParamRowView display builder (xUnit)"
```

---

### Task 3: ViewModels + RideSession refresh events

**Files:**
- Modify: `dotnet/src/TelemetryPoc.App/RideSession.cs` (add `MetaLoaded` + `Ticked`)
- Create: `ViewModels/ParamRowViewModel.cs`, `ViewModels/ParamGroupViewModel.cs`, `ViewModels/OverviewViewModel.cs`

**Interfaces:**
- Consumes: `ParamGrouping.Group`, `ParamRowView.Build`, `RideSession`, `TelemetryStore`.
- Produces:
  - `RideSession.MetaLoaded` (`event Action?`, raised after `ApplyMeta`), `RideSession.Ticked` (`event Action?`, raised at end of `Tick`).
  - `OverviewViewModel(RideSession session)` with `ObservableCollection<ParamGroupViewModel> Groups` and `string ChannelCountText` (`"ALL · N CH"`), building on `MetaLoaded`, refreshing on `Ticked`.

- [ ] **Step 1: Add events to `RideSession.cs`** — add the fields and raise them. Add near the other members:

```csharp
public event Action? MetaLoaded;
public event Action? Ticked;
```

In `Start()`, immediately after `Store.ApplyMeta(channels, enums);` add:

```csharp
MetaLoaded?.Invoke();
```

At the **end** of `Tick()` (after `TPlusText = ...`) add:

```csharp
Ticked?.Invoke();
```

- [ ] **Step 2: Create `ViewModels/ParamRowViewModel.cs`**:

```csharp
using System.ComponentModel;
using System.Windows.Media;
using TelemetryPoc.App.Viz;
using TelemetryPoc.Core;

namespace TelemetryPoc.App.ViewModels;

public sealed class ParamRowViewModel : INotifyPropertyChanged
{
    private readonly ChannelMeta _ch;
    public ParamRowViewModel(ChannelMeta ch) { _ch = ch; }

    public string Name => _ch.Name;
    public string Unit => _ch.Unit;
    public string Addr => _ch.Addr;

    private string _value = "—";
    public string Value { get => _value; private set { _value = value; Raise(nameof(Value)); } }

    private Brush _valueBrush = Brushes.Gray;
    public Brush ValueBrush { get => _valueBrush; private set { _valueBrush = value; Raise(nameof(ValueBrush)); } }

    private Brush _dotBrush = Brushes.Gray;
    public Brush DotBrush { get => _dotBrush; private set { _dotBrush = value; Raise(nameof(DotBrush)); } }

    private Brush _rowBg = Brushes.Transparent;
    public Brush RowBackground { get => _rowBg; private set { _rowBg = value; Raise(nameof(RowBackground)); } }

    private static readonly Brush CriticalBg =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF1A0E11")!);

    public void Refresh(TelemetryStore store)
    {
        var d = ParamRowView.Build(_ch, store.Latest(_ch.Id), store.EnumIndex);
        Value = d.Text;
        ValueBrush = Hex(d.ValueColor);
        DotBrush = Hex(d.DotColor);
        RowBackground = d.Critical ? CriticalBg : Brushes.Transparent;
    }

    private static Brush Hex(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        b.Freeze();
        return b;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
```

- [ ] **Step 3: Create `ViewModels/ParamGroupViewModel.cs`**:

```csharp
using System.Collections.ObjectModel;
using TelemetryPoc.Core;

namespace TelemetryPoc.App.ViewModels;

public sealed class ParamGroupViewModel
{
    public string Name { get; }
    public ObservableCollection<ParamRowViewModel> Rows { get; } = new();

    public ParamGroupViewModel(string name, IReadOnlyList<ChannelMeta> channels)
    {
        Name = name;
        foreach (var ch in channels) Rows.Add(new ParamRowViewModel(ch));
    }

    public void Refresh(TelemetryStore store)
    {
        foreach (var r in Rows) r.Refresh(store);
    }
}
```

- [ ] **Step 4: Create `ViewModels/OverviewViewModel.cs`**:

```csharp
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using TelemetryPoc.App.Viz;
using TelemetryPoc.Core;

namespace TelemetryPoc.App.ViewModels;

public sealed class OverviewViewModel : INotifyPropertyChanged
{
    private readonly RideSession _session;
    public ObservableCollection<ParamGroupViewModel> Groups { get; } = new();

    private string _channelCountText = "ALL · 0 CH";
    public string ChannelCountText
    {
        get => _channelCountText;
        private set { _channelCountText = value; Raise(nameof(ChannelCountText)); }
    }

    public OverviewViewModel(RideSession session)
    {
        _session = session;
        _session.MetaLoaded += BuildGroups;
        _session.Ticked += RefreshRows;
    }

    private void BuildGroups()
    {
        Groups.Clear();
        var store = _session.Store;
        foreach (var g in ParamGrouping.Group(store.Channels))
            Groups.Add(new ParamGroupViewModel(g.Name, g.Channels));
        ChannelCountText = string.Format(CultureInfo.InvariantCulture, "ALL · {0} CH", store.Channels.Count);
        RefreshRows();
    }

    private void RefreshRows()
    {
        foreach (var g in Groups) g.Refresh(_session.Store);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
```

- [ ] **Step 5: Build + test** — (from `dotnet/`) `dotnet build` (0 errors) and `dotnet test` (still green — no new unit tests this task; the VMs are build-verified, logic is covered by Tasks 1–2).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(dotnet): param-panel ViewModels + RideSession MetaLoaded/Ticked events"
```

---

### Task 4: ParamPanel.xaml + wire into OverviewView

**Files:**
- Create: `dotnet/src/TelemetryPoc.App/Views/ParamPanel.xaml` + `.xaml.cs`
- Modify: `dotnet/src/TelemetryPoc.App/Views/OverviewView.xaml`
- Modify: `dotnet/src/TelemetryPoc.App/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `OverviewViewModel` (DataContext) with `Groups` + `ChannelCountText`; each `ParamGroupViewModel` has `Name` + `Rows`; each `ParamRowViewModel` has `Name`/`Value`/`Unit`/`Addr`/`ValueBrush`/`DotBrush`/`RowBackground`.

- [ ] **Step 1: Create `Views/ParamPanel.xaml`** (header + scrollable grouped list):

```xml
<UserControl x:Class="TelemetryPoc.App.Views.ParamPanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Background="{StaticResource Panel}">
    <DockPanel>
        <Border DockPanel.Dock="Top" Background="{StaticResource ColHeaderBg}"
                BorderBrush="{StaticResource Border1}" BorderThickness="0,0,0,1" Padding="10,7">
            <Grid>
                <TextBlock Text="PARAMETERS" Foreground="{StaticResource PanelTitle}"
                           FontFamily="{StaticResource MonoFont}" FontSize="11" FontWeight="Bold" />
                <TextBlock Text="{Binding ChannelCountText}" HorizontalAlignment="Right"
                           Foreground="{StaticResource TextDim}" FontFamily="{StaticResource MonoFont}" FontSize="10" />
            </Grid>
        </Border>
        <ScrollViewer VerticalScrollBarVisibility="Auto">
            <ItemsControl ItemsSource="{Binding Groups}">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <StackPanel>
                            <Border Background="{StaticResource Panel2}" Padding="10,3">
                                <TextBlock Text="{Binding Name}" Foreground="{StaticResource PanelTitle}"
                                           FontFamily="{StaticResource MonoFont}" FontSize="10" />
                            </Border>
                            <ItemsControl ItemsSource="{Binding Rows}">
                                <ItemsControl.ItemTemplate>
                                    <DataTemplate>
                                        <Border Background="{Binding RowBackground}" Padding="10,2">
                                            <Grid>
                                                <Grid.ColumnDefinitions>
                                                    <ColumnDefinition Width="12" />
                                                    <ColumnDefinition Width="*" />
                                                    <ColumnDefinition Width="Auto" />
                                                    <ColumnDefinition Width="Auto" />
                                                </Grid.ColumnDefinitions>
                                                <Ellipse Grid.Column="0" Width="6" Height="6" VerticalAlignment="Center"
                                                         Fill="{Binding DotBrush}" />
                                                <TextBlock Grid.Column="1" Text="{Binding Name}" Foreground="{StaticResource TextData}"
                                                           FontFamily="{StaticResource MonoFont}" FontSize="11" />
                                                <TextBlock Grid.Column="2" Text="{Binding Value}" Foreground="{Binding ValueBrush}"
                                                           FontFamily="{StaticResource MonoFont}" FontSize="11" Margin="0,0,6,0" />
                                                <TextBlock Grid.Column="3" Text="{Binding Unit}" Foreground="{StaticResource TextDim}"
                                                           FontFamily="{StaticResource MonoFont}" FontSize="9" VerticalAlignment="Center" />
                                            </Grid>
                                        </Border>
                                    </DataTemplate>
                                </ItemsControl.ItemTemplate>
                            </ItemsControl>
                        </StackPanel>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </ScrollViewer>
    </DockPanel>
</UserControl>
```

`Views/ParamPanel.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace TelemetryPoc.App.Views;

public partial class ParamPanel : UserControl
{
    public ParamPanel() => InitializeComponent();
}
```

- [ ] **Step 2: Host it in `Views/OverviewView.xaml`** (left column ~320px + empty right placeholder for later phases):

```xml
<UserControl x:Class="TelemetryPoc.App.Views.OverviewView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:views="clr-namespace:TelemetryPoc.App.Views"
             Background="{StaticResource Bg}">
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="320" />
            <ColumnDefinition Width="*" />
        </Grid.ColumnDefinitions>
        <views:ParamPanel Grid.Column="0" Margin="6" />
        <TextBlock Grid.Column="1" Text="WIDGETS (Phase 3+)" Foreground="{StaticResource TextDim}"
                   FontFamily="{StaticResource MonoFont}" FontSize="11"
                   HorizontalAlignment="Center" VerticalAlignment="Center" />
    </Grid>
</UserControl>
```

> The nested `ParamPanel` inherits `OverviewView`'s `DataContext` (the `OverviewViewModel`), so its `{Binding Groups}` / `{Binding ChannelCountText}` resolve.

- [ ] **Step 3: Wire the DataContext in `MainWindow.xaml.cs`** — name the OverviewView and set its DataContext. First give the view a name in `MainWindow.xaml`: change `<views:OverviewView Grid.Row="1" />` to `<views:OverviewView x:Name="Overview" Grid.Row="1" />`. Then in `MainWindow.xaml.cs` add the VM:

```csharp
using System.Windows;
using System.Windows.Data;
using TelemetryPoc.App.ViewModels;

namespace TelemetryPoc.App;

public partial class MainWindow : Window
{
    private readonly RideSession _session = new();

    public MainWindow()
    {
        InitializeComponent();
        TopBar.ClockText.SetBinding(System.Windows.Controls.TextBlock.TextProperty,
            new Binding(nameof(RideSession.ClockText)) { Source = _session });
        Overview.DataContext = new OverviewViewModel(_session);
        Loaded += (_, _) => _session.Start();
    }
}
```

> Order matters: construct `OverviewViewModel` (subscribes to `MetaLoaded`/`Ticked`) **before** `_session.Start()` fires on Loaded, so the panel builds when meta loads.

- [ ] **Step 4: Build + launch-verify** — (from `dotnet/`) `dotnet build`; generate a DB if needed; `dotnet run --project src/TelemetryPoc.App`. Confirm the left PARAMETERS panel shows grouped rows (INU Mode, Velocity, Attitude, …), live values updating, status dots, `ALL · 30 CH`, and the INU Mode critical row highlighted when `inu_mode2` reads Critical. Close it. (Controller does the live visual check.)

- [ ] **Step 5: Full test run** — (from `dotnet/`) `dotnet test` → all green.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(dotnet): live grouped PARAMETERS panel in OVERVIEW"
```

---

## Self-Review

**Spec coverage (Phase 2):** grouped live param table (Tasks 1–4); groups mirror `groups.ts` (Task 1); status dots + severity color + critical highlight mirror `paramrow.ts` with INU palette (Task 2); `ALL · N CH` count (Task 3/4); live refresh per tick via `RideSession.Ticked` (Task 3). ✓

**Placeholder scan:** No TBD/TODO. Every code step is complete. The right-column "WIDGETS (Phase 3+)" text is an intentional placeholder for later phases, not a gap. ✓

**Type consistency:** `ParamGroup(Name, Channels)`, `ParamGrouping.Group`/`GroupOf`, `Severity.Hex`, `ParamRowDisplay(Text, ValueColor, DotColor, Critical)`, `ParamRowView.Build(ch, double?, index)`, `OverviewViewModel(RideSession)` with `Groups`/`ChannelCountText`, `RideSession.MetaLoaded`/`Ticked` — names/signatures consistent across Tasks 1→4. `store.EnumIndex` type matches `ValueFormat`'s `index` param. Brush conversion via `ColorConverter.ConvertFromString` on `#AARRGGBB` hex from Viz. ✓

**Note:** Core's `ValueFormat.SeverityColor` (old palette) is deliberately unused; the INU mapper is `Severity.Hex` in Viz. Core stays untouched.
