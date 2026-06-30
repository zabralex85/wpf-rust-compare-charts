using System.Reflection;
using NetArchTest.Rules;
using Xunit;

namespace TelemetryPoc.Tests;

public class ArchitectureTests
{
    private static readonly Assembly Domain = typeof(TelemetryPoc.Domain.TelemetryStore).Assembly;
    private static readonly Assembly Application = typeof(TelemetryPoc.Application.RideEngine).Assembly;
    private static readonly Assembly Infrastructure = typeof(TelemetryPoc.Infrastructure.SqliteRideSource).Assembly;
    private static readonly Assembly Presentation = typeof(TelemetryPoc.Presentation.GaugeViz).Assembly;

    [Fact]
    public void Domain_depends_on_nothing_outward()
    {
        var result = Types.InAssembly(Domain)
            .Should().NotHaveDependencyOnAny(
                "TelemetryPoc.Application", "TelemetryPoc.Infrastructure",
                "TelemetryPoc.Presentation", "TelemetryPoc.App",
                "Microsoft.Data.Sqlite", "SkiaSharp", "PresentationFramework")
            .GetResult();
        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? new System.Collections.Generic.List<string>()));
    }

    [Fact]
    public void Application_depends_only_on_Domain()
    {
        var result = Types.InAssembly(Application)
            .Should().NotHaveDependencyOnAny(
                "TelemetryPoc.Infrastructure", "TelemetryPoc.Presentation", "TelemetryPoc.App")
            .GetResult();
        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? new System.Collections.Generic.List<string>()));
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_Presentation_or_WPF()
    {
        var result = Types.InAssembly(Infrastructure)
            .Should().NotHaveDependencyOnAny("TelemetryPoc.Presentation", "TelemetryPoc.App", "PresentationFramework")
            .GetResult();
        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? new System.Collections.Generic.List<string>()));
    }

    [Fact]
    public void Presentation_does_not_depend_on_Infrastructure_or_WPF()
    {
        var result = Types.InAssembly(Presentation)
            .Should().NotHaveDependencyOnAny("TelemetryPoc.Infrastructure", "TelemetryPoc.App", "PresentationFramework")
            .GetResult();
        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? new System.Collections.Generic.List<string>()));
    }
}
