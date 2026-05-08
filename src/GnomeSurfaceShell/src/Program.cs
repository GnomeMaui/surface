using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Cogl;
using GnomeSurfaceCanvas;
using GnomeSurfaceCanvas.Services;
using GnomeSurfaceShell.DBus;
using GnomeSurfaceShell.PluginLoading;
using GnomeSurfaceShell.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tmds.Systemd;

[assembly: UnsupportedOSPlatform("Windows")]
[assembly: UnsupportedOSPlatform("macOS")]

foreach (var path in new[]
{
	// Arch Linux
	"/usr/lib/gnome-shell/libst-18.so",
	// Fedora 44
	"/usr/lib64/gnome-shell/libst-18.so",
	// "/usr/lib64/libEGL_mesa.so.0"
})
{
	if (File.Exists(path))
		NativeLibrary.Load(path);
}

EGL.Module.Initialize();
GLib.Module.Initialize();
GioUnix.Module.Initialize();
Gio.Module.Initialize();
Gdk.Module.Initialize();
Meta.Module.Initialize();
St.Module.Initialize();

var hostArgs = FilterHostArgs(args);

var host = Host.CreateDefaultBuilder(hostArgs)
	.ConfigureLogging(static logging => logging.AddJournal())
	.ConfigureServices((_, services) =>
	{
		services.AddSingleton(new ShellCommandLine(args));
		services.AddSingleton<ShellRuntimeState>();
		services.AddSingleton<ThemeDetector>();
		services.AddSingleton<GnomeSurfaceCanvasCatalog>();
		services.AddSingleton<GnomeSurfacePluginLoader>();

		// DBus infrastructure.
		services.AddSingleton<SessionBusConnection>();
		services.AddHostedService(static sp => sp.GetRequiredService<SessionBusConnection>());

		services.AddSingleton<GnomeShellServer>();
		services.AddSingleton<GnomeShellBrightnessServer>();
		services.AddSingleton<GnomeMutterDisplayConfigServer>();
		services.AddSingleton<GnomeMutterServiceChannelServer>();
		services.AddSingleton<GnomeShellIntrospectServer>();
		services.AddSingleton<GnomeScreenSaverServer>();
		services.AddHostedService<ShellDbusHostService>();

		// Register as singleton and hosted service to guarantee one shared runtime instance.
		services.AddSingleton<ShellRuntimeService>();
		services.AddHostedService(static sp => sp.GetRequiredService<ShellRuntimeService>());
	})
	.Build();

var startupLogger = host.Services
	.GetRequiredService<ILoggerFactory>()
	.CreateLogger("GnomeSurfaceShell.Startup");

startupLogger.LogInformation("Host built. Starting GnomeSurfaceShell runtime.");

await host.RunAsync();

static string[] FilterHostArgs(string[] args)
{
	var filtered = new List<string>();

	for (var i = 0; i < args.Length; i++)
	{
		var arg = args[i];

		if (IsMutterOptionWithValue(arg))
		{
			if (!arg.Contains('=', StringComparison.Ordinal) && i + 1 < args.Length)
				i++;

			continue;
		}

		if (IsMutterFlag(arg))
			continue;

		filtered.Add(arg);
	}

	return filtered.ToArray();
}

static bool IsMutterFlag(string arg)
{
	return arg is "--wayland" or "--headless" or "--no-x11" or "--display-server" or "--unsafe-mode" or "--debug-control";
}

static bool IsMutterOptionWithValue(string arg)
{
	return arg == "--wayland-display" ||
		   arg.StartsWith("--wayland-display=", StringComparison.Ordinal) ||
		   arg == "--virtual-monitor" ||
		   arg.StartsWith("--virtual-monitor=", StringComparison.Ordinal) ||
		   arg == "--profile" ||
		   arg.StartsWith("--profile=", StringComparison.Ordinal);
}
