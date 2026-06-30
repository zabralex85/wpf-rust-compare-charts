using TelemetryPoc.Infrastructure;

namespace TelemetryPoc.Tests;

public class RidePathsTests
{
	[Fact]
	public void Env_path_used_when_it_exists()
	{
		var saved = Environment.GetEnvironmentVariable("RIDE_DB");
		Environment.SetEnvironmentVariable("RIDE_DB", @"C:\rides\my.db");
		try
		{
			var r = new RidePathResolver(@"C:\app", _ => false).ResolveRideDb();
			Assert.Equal(@"C:\rides\my.db", r);
		}
		finally
		{
			Environment.SetEnvironmentVariable("RIDE_DB", saved);
		}
	}

	[Fact]
	public void Walks_up_for_data_ride_db()
	{
		// base dir C:\repo\dotnet\bin ; data\ride.db lives at C:\repo\data\ride.db
		var saved = Environment.GetEnvironmentVariable("RIDE_DB");
		Environment.SetEnvironmentVariable("RIDE_DB", null);
		try
		{
			var hit = @"C:\repo\data\ride.db";
			var r = new RidePathResolver(@"C:\repo\dotnet\bin", p => p == hit).ResolveRideDb();
			Assert.Equal(hit, r);
		}
		finally
		{
			if (saved is not null) Environment.SetEnvironmentVariable("RIDE_DB", saved);
		}
	}

	[Fact]
	public void Prefers_ride_db_over_ride_small_db_in_same_dir()
	{
		var saved = Environment.GetEnvironmentVariable("RIDE_DB");
		Environment.SetEnvironmentVariable("RIDE_DB", null);
		try
		{
			static bool Exists(string p)
			{
				return p == @"C:\repo\data\ride.db" || p == @"C:\repo\data\ride_small.db";
			}

			var r = new RidePathResolver(@"C:\repo\dotnet", Exists).ResolveRideDb();
			Assert.Equal(@"C:\repo\data\ride.db", r);
		}
		finally
		{
			if (saved is not null) Environment.SetEnvironmentVariable("RIDE_DB", saved);
		}
	}

	[Fact]
	public void Falls_back_to_default_when_nothing_found()
	{
		var saved = Environment.GetEnvironmentVariable("RIDE_DB");
		Environment.SetEnvironmentVariable("RIDE_DB", null);
		try
		{
			var r = new RidePathResolver(@"C:\nope", _ => false).ResolveRideDb();
			// New instance API returns absolute path (baseDir + "data/ride.db")
			Assert.Equal(Path.Combine(@"C:\nope", "data", "ride.db"), r);
		}
		finally
		{
			if (saved is not null) Environment.SetEnvironmentVariable("RIDE_DB", saved);
		}
	}
}