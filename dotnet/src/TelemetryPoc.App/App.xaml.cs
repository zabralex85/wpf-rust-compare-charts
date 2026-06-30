// dotnet/src/TelemetryPoc.App/App.xaml.cs

using System.IO;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TelemetryPoc.App.ViewModels;
using TelemetryPoc.Application;
using TelemetryPoc.Infrastructure;

namespace TelemetryPoc.App;

public partial class App
{
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var builder = Host.CreateApplicationBuilder();

        // Config source is appsettings.json (the "Ride" section). Env vars (RIDE_DB /
        // RIDE_MBTILES / RIDE_SPEED) are optional OVERRIDES — only applied when actually
        // set, so an unset env never clobbers the appsettings value with null.
        var overrides = new Dictionary<string, string?>();
        var dbEnv = Environment.GetEnvironmentVariable("RIDE_DB");
        if (!string.IsNullOrWhiteSpace(dbEnv))
        {
            overrides["Ride:DbPath"] = dbEnv;
        }

        var mbEnv = Environment.GetEnvironmentVariable("RIDE_MBTILES");
        if (!string.IsNullOrWhiteSpace(mbEnv))
        {
            overrides["Ride:MbTilesPath"] = mbEnv;
        }

        var speedEnv = Environment.GetEnvironmentVariable("RIDE_SPEED");
        if (double.TryParse(speedEnv, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var sp))
        {
            overrides["Ride:Speed"] = sp.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        if (overrides.Count > 0)
        {
            builder.Configuration.AddInMemoryCollection(overrides);
        }

        builder.Services.Configure<RideOptions>(builder.Configuration.GetSection("Ride"));
        builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<RideOptions>>().Value);

        builder.Logging.AddDebug();

        // Ports → adapters
        builder.Services.AddSingleton<IRidePathResolver>(sp =>
            new RidePathResolver(sp.GetRequiredService<RideOptions>(), AppContext.BaseDirectory, File.Exists));
        builder.Services.AddSingleton<IRideSource, SqliteRideSource>();
        builder.Services.AddSingleton<IMetricsSampler, SysInfoMetricsSampler>();
        builder.Services.AddSingleton<ISystemClock, SystemClock>();
        builder.Services.AddSingleton<ITileSource>(sp =>
        {
            var path = sp.GetRequiredService<IRidePathResolver>().ResolveMbTiles();
            return new MbTilesTileSource(path); // null path → empty tile source (map shows background)
        });

        // App graph
        builder.Services.AddSingleton<RideSession>();
        builder.Services.AddSingleton<TopBarViewModel>();
        builder.Services.AddSingleton<OverviewViewModel>();
        builder.Services.AddSingleton<TransportViewModel>();
        builder.Services.AddSingleton<HudViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        _host = builder.Build();
        _host.Start();

        var window = _host.Services.GetRequiredService<MainWindow>();
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        base.OnExit(e);
    }
}
