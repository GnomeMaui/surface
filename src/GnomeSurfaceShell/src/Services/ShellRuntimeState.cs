using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace GnomeSurfaceShell.Services;

/// <summary>
/// Shared runtime state used by DI services and DBus surface.
/// </summary>
sealed class ShellRuntimeState
{
	readonly object _sync = new();
	bool _shellReady;
	Meta.Context? _metaContext;
	int? _metaContextThreadId;
	uint _displaySerial = 1;
	DisplayMonitorState[] _displayMonitors = [DisplayMonitorState.Fallback];
	readonly Dictionary<ulong, WindowIntrospectionState> _windows = [];

	public ShellRuntimeState(IConfiguration configuration)
	{
		Mode = configuration["shell:mode"] ?? "user";
		ShellVersion = configuration["shell:version"] ?? "GnomeSurfaceShell/0.1.0";
	}

	public string Mode { get; }

	public string ShellVersion { get; }

	public bool ShellReady
	{
		get
		{
			lock (_sync)
			{
				return _shellReady;
			}
		}
	}

	public event Action<bool>? ShellReadyChanged;

	public event Action? WindowsChanged;

	public event Action? DisplayConfigurationChanged;

	public (Meta.Context? Context, int? ThreadId) GetMetaContext()
	{
		lock (_sync)
		{
			return (_metaContext, _metaContextThreadId);
		}
	}

	public void SetMetaContext(Meta.Context? context, int? threadId)
	{
		lock (_sync)
		{
			_metaContext = context;
			_metaContextThreadId = threadId;
		}
	}

	public DisplayConfigurationState GetDisplayConfiguration()
	{
		lock (_sync)
		{
			return new DisplayConfigurationState(_displaySerial, [.. _displayMonitors]);
		}
	}

	public DisplayConfigurationState UpdateDisplayConfiguration(DisplayMonitorState[] monitors)
	{
		ArgumentNullException.ThrowIfNull(monitors);

		DisplayConfigurationState snapshot;
		lock (_sync)
		{
			_displaySerial++;
			_displayMonitors = monitors.Length == 0 ? [DisplayMonitorState.Fallback] : [.. monitors];
			snapshot = new DisplayConfigurationState(_displaySerial, [.. _displayMonitors]);
		}

		DisplayConfigurationChanged?.Invoke();
		return snapshot;
	}

	public void SetShellReady(bool ready)
	{
		bool changed;
		lock (_sync)
		{
			changed = _shellReady != ready;
			_shellReady = ready;
		}

		if (changed)
		{
			ShellReadyChanged?.Invoke(ready);
		}
	}

	public WindowIntrospectionState[] GetWindows()
	{
		lock (_sync)
		{
			return [.. _windows.Values];
		}
	}

	public void UpsertWindow(WindowIntrospectionState window)
	{
		ArgumentNullException.ThrowIfNull(window);

		lock (_sync)
		{
			_windows[window.Id] = window;
		}

		WindowsChanged?.Invoke();
	}

	public void RemoveWindow(ulong id)
	{
		var removed = false;
		lock (_sync)
		{
			removed = _windows.Remove(id);
		}

		if (removed)
			WindowsChanged?.Invoke();
	}
}

sealed record DisplayConfigurationState(
	uint Serial,
	DisplayMonitorState[] Monitors);

sealed record DisplayMonitorState(
	int Index,
	string Connector,
	string Vendor,
	string Product,
	string Serial,
	int X,
	int Y,
	int Width,
	int Height,
	double Scale,
	double RefreshRate,
	bool IsPrimary)
{
	public static DisplayMonitorState Fallback { get; } = new(
		Index: 0,
		Connector: "UNKNOWN-0",
		Vendor: "unknown",
		Product: "unknown",
		Serial: "unknown",
		X: 0,
		Y: 0,
		Width: 1024,
		Height: 768,
		Scale: 1.0,
		RefreshRate: 60.0,
		IsPrimary: true);
}

sealed record WindowIntrospectionState(
	ulong Id,
	string AppId,
	string? Title,
	string? WmClass,
	uint ClientType,
	bool IsHidden,
	bool HasFocus,
	uint Width,
	uint Height,
	string? SandboxedAppId);
