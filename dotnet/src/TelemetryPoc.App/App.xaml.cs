// dotnet/src/TelemetryPoc.App/App.xaml.cs
using System;
using System.IO;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TelemetryPoc.App.ViewModels;
using TelemetryPoc.Application;
using TelemetryPoc.Domain;
using TelemetryPoc.Infrastructure;

namespace TelemetryPoc.App;

public partial class App
{
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var builder = Host.CreateApplicationBuilder();

        // Config: appsettings + env (RIDE_DB / RIDE_SPEED / RIDE_MBTILES → RideOptions)
        var speedEnv = Environment.GetEnvironmentVariable("RIDE_SPEED");
        var speed = double.TryParse(speedEnv, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var sp) ? sp.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
        builder.Configuration.AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?>
        {
            ["Ride:DbPath"] = Environment.GetEnvironmentVariable("RIDE_DB"),
            ["Ride:MbTilesPath"] = Environment.GetEnvironmentVariable("RIDE_MBTILES"),
            ["Ride:Speed"] = speed,
        });
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
