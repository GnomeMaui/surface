using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using GObject.Internal;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace GnomeSurfaceCanvas.Services;

[UnsupportedOSPlatform("Windows")]
[UnsupportedOSPlatform("macOS")]
public sealed class GnomeSurfaceCanvasCatalog
{
	readonly ILogger<GnomeSurfaceCanvasCatalog> _logger;
	readonly GnomeSurfaceCanvasIconResolver _iconResolver;
	readonly object _sync = new();
	ShellAppEntry[] _apps = [];
	DateTimeOffset _lastScan = DateTimeOffset.MinValue;

	public GnomeSurfaceCanvasCatalog(ILogger<GnomeSurfaceCanvasCatalog> logger)
	{
		_logger = logger;
		_iconResolver = new GnomeSurfaceCanvasIconResolver(logger);
		GioUnix.DesktopAppInfo.SetDesktopEnv("GNOME");
	}

	public ShellAppEntry[] GetApps()
	{
		lock (_sync)
		{
			if (_apps.Length == 0 || DateTimeOffset.UtcNow - _lastScan > TimeSpan.FromSeconds(30))
				RescanLocked();

			return [.. _apps];
		}
	}

	public ShellAppEntry[] Refresh()
	{
		lock (_sync)
		{
			_iconResolver.ClearCache();
			RescanLocked();
			return [.. _apps];
		}
	}

	public bool Launch(string id)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(id);

		ShellAppEntry? app;
		lock (_sync)
		{
			if (_apps.Length == 0)
				RescanLocked();

			app = _apps.FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal));
		}

		if (app is null)
		{
			_logger.LogWarning("[AppCatalog.Launch] App id={AppId} was not found in current catalog.", id);
			return false;
		}

		try
		{
			_logger.LogInformation(
				"[AppCatalog.Launch] Launch requested. id={AppId} name={Name} file={DesktopFile}",
				app.Id,
				app.Name,
				app.DesktopFile);

			var info = GetLaunchAppInfo(app);
			if (info is null)
			{
				_logger.LogWarning("[AppCatalog.Launch] AppInfo was not found. id={AppId} file={DesktopFile}", app.Id, app.DesktopFile);
				return false;
			}

			var launched = info.Launch(files: null, context: null);
			_logger.LogInformation("[AppCatalog.Launch] Launch result id={AppId} launched={Launched}.", app.Id, launched);
			return launched;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "[AppCatalog.Launch] Launch failed. id={AppId} file={DesktopFile}", app.Id, app.DesktopFile);
			return false;
		}
	}

	Gio.AppInfo? GetLaunchAppInfo(ShellAppEntry app)
	{
		if (!string.IsNullOrWhiteSpace(app.DesktopFile))
		{
			var desktopInfo = GioUnix.DesktopAppInfo.NewFromFilename(app.DesktopFile);
			if (desktopInfo is not null)
				return desktopInfo;
		}

		var appInfos = Gio.AppInfoHelper.GetAll();
		var appInfoCount = GLib.List.Length(appInfos);
		for (uint i = 0; i < appInfoCount; i++)
		{
			var appInfoPtr = GLib.List.NthData(appInfos, i);
			if (appInfoPtr == IntPtr.Zero)
				continue;

			var info = (Gio.AppInfo)InstanceWrapper.WrapHandle<Gio.AppInfoHelper>(appInfoPtr, ownedRef: false);
			if (string.Equals(info.GetId(), app.Id, StringComparison.Ordinal))
				return info;
		}

		return null;
	}

	void RescanLocked()
	{
		var byId = new Dictionary<string, ShellAppEntry>(StringComparer.Ordinal);
		var dataDirs = GLib.Functions.GetSystemDataDirs();
		var appInfos = Gio.AppInfoHelper.GetAll();
		var appInfoCount = GLib.List.Length(appInfos);

		_logger.LogInformation(
			"[AppCatalog.Scan] Starting Gio.AppInfo scan. app_info_count={AppInfoCount} system_data_dirs={DataDirs}",
			appInfoCount,
			string.Join(':', dataDirs));

		for (uint i = 0; i < appInfoCount; i++)
			TryAddAppInfo(byId, GLib.List.NthData(appInfos, i));

		_apps = byId.Values
			.OrderBy(static app => app.Name, StringComparer.CurrentCultureIgnoreCase)
			.ThenBy(static app => app.Id, StringComparer.Ordinal)
			.ToArray();
		_lastScan = DateTimeOffset.UtcNow;
		_logger.LogInformation("[AppCatalog.Scan] Completed. visible_apps={Count}", _apps.Length);
	}

	void TryAddAppInfo(Dictionary<string, ShellAppEntry> byId, IntPtr appInfoPtr)
	{
		if (appInfoPtr == IntPtr.Zero)
			return;

		try
		{
			var info = (Gio.AppInfo)InstanceWrapper.WrapHandle<Gio.AppInfoHelper>(appInfoPtr, ownedRef: false);

			if (!info.ShouldShow())
			{
				_logger.LogInformation("[AppCatalog.Scan] AppInfo hidden/not shown. id={AppId} name={Name}", info.GetId(), info.GetName());
				return;
			}

			var id = info.GetId();
			if (string.IsNullOrWhiteSpace(id))
				id = info.GetName();

			var name = info.GetDisplayName();
			if (string.IsNullOrWhiteSpace(name))
				name = info.GetName();

			if (string.IsNullOrWhiteSpace(name))
			{
				_logger.LogInformation("[AppCatalog.Scan] AppInfo skipped, missing display name. id={AppId}", id);
				return;
			}

			if (!byId.ContainsKey(id))
			{
				var desktopFile = GetDesktopFile(info);
				var icon = _iconResolver.ResolveIcon(info.GetIcon());
				byId.Add(id, new ShellAppEntry(id, name, info.GetDescription(), desktopFile, icon));
				_logger.LogInformation("[AppCatalog.Scan] App added. id={AppId} name={Name} file={DesktopFile} icon={IconPath}", id, name, desktopFile, icon);
			}
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "[AppCatalog.Scan] AppInfo skipped after error. ptr={AppInfoPtr}", appInfoPtr);
		}
	}

	static string? GetDesktopFile(Gio.AppInfo appInfo)
	{
		return appInfo is GioUnix.DesktopAppInfo desktopAppInfo
			? desktopAppInfo.GetFilename()
			: null;
	}
}

public sealed record ShellAppEntry(
	string Id,
	string Name,
	string? Description,
	string? DesktopFile,
	SKImage? Icon);
