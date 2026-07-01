using System;
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

public partial class App : global::Avalonia.Application
{
    private IHost? _host;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
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
            if (double.TryParse(speedEnv, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var sp))
            {
                overrides["Ride:Speed"] = sp.ToString(CultureInfo.InvariantCulture);
            }

            var capEnv = Environment.GetEnvironmentVariable("RIDE_FPS_CAP");
            if (int.TryParse(capEnv, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cap))
            {
                overrides["Ride:FpsCap"] = cap.ToString(CultureInfo.InvariantCulture);
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

            // Push the resolved frame cap to the FrameClock (XAML-constructed, no DI) before the
            // window's visual tree is built. 0 = uncapped (default).
            Controls.FrameClock.CapHz = _host.Services.GetRequiredService<RideOptions>().FpsCap;

            desktop.MainWindow = _host.Services.GetRequiredService<MainWindow>();
            desktop.ShutdownRequested += (_, _) => _host?.Dispose();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
