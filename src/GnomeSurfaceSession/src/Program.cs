using System;
using System.Runtime.Versioning;
using GnomeSurfaceSession.DBus;
using GnomeSurfaceSession.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

[assembly: UnsupportedOSPlatform("Windows")]
[assembly: UnsupportedOSPlatform("macOS")]

GLib.Module.Initialize();

var host = Host.CreateDefaultBuilder(args)
	.ConfigureLogging(static logging => logging.AddJournal())
	.ConfigureServices((context, services) =>
	{
		var runAs = (context.Configuration["runAs"] ?? "combined").Trim().ToLowerInvariant();
		var defaultEnableSessionManagerServer = runAs is "combined" or "manager";
		var defaultEnableLeaderService = runAs is "combined" or "leader";

		var enableSessionManagerServer =
			!bool.TryParse(context.Configuration["enableSessionManagerServer"], out var enabled)
				? defaultEnableSessionManagerServer
				: enabled;

		var enableLeaderService =
			!bool.TryParse(context.Configuration["enableLeaderService"], out var leaderEnabled)
				? defaultEnableLeaderService
				: leaderEnabled;

		// SessionBusConnection must be registered and started before LeaderService runs.
		services.AddSingleton<SessionBusConnection>();
		services.AddHostedService(static sp => sp.GetRequiredService<SessionBusConnection>());

		services.AddSingleton<SystemBusConnection>();
		services.AddHostedService(static sp => sp.GetRequiredService<SystemBusConnection>());

		services.AddSingleton<SystemdManagerService>();
		services.AddSingleton<LogindSessionService>();
		services.AddSingleton<GsmSessionFillService>();
		services.AddSingleton<GsmAutostartService>();
		services.AddSingleton<GsmClientStore>();
		services.AddSingleton<GsmInhibitorStore>();
		services.AddSingleton<GsmSessionRestoreStore>();
		services.AddSingleton<GsmPresenceService>();
		services.AddHostedService<PresenceIdleMonitorService>();
		services.AddSingleton<GsmPhaseManager>(sp =>
		{
			var manager = ActivatorUtilities.CreateInstance<GsmPhaseManager>(sp);
			manager.SessionName = context.Configuration["session"] ?? "gnome";
			manager.RestoreSupported = true;

			return manager;
		});
		services.AddSingleton<SessionManagerServer>();
		services.AddSingleton<SessionManagerHostService>();

		if (enableSessionManagerServer)
		{
			services.AddHostedService(static sp =>
			{
				var hostedService = sp.GetRequiredService<SessionManagerHostService>();
				hostedService.Enable();
				return hostedService;
			});
		}

		if (enableLeaderService)
		{
			services.AddHostedService<LeaderService>();
		}
	})
	.Build();

var startupLogger = host.Services
	.GetRequiredService<ILoggerFactory>()
	.CreateLogger("GnomeSurfaceSession.Startup");

var runAsEffective = (host.Services.GetRequiredService<IConfiguration>()["runAs"] ?? "combined").Trim().ToLowerInvariant();
startupLogger.LogInformation("Host built. runAs={RunAs}", runAsEffective);

await host.RunAsync();
