using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GnomeSurfaceSession.DBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tmds.DBus.Protocol;

namespace GnomeSurfaceSession.Services;

/// <summary>
/// BackgroundService implementing the gnome-session leader logic (leader-systemd.c equivalent).
/// Starts the systemd session target, waits for org.gnome.SessionManager to appear,
/// monitors SessionRunning/SessionOver signals, and triggers shutdown on exit.
/// </summary>
sealed class LeaderService : BackgroundService
{
	const string SessionManagerServiceName = "org.gnome.SessionManager";
	const string SessionManagerPath = "/org/gnome/SessionManager";

	const string DBusServiceName = "org.freedesktop.DBus";
	const string DBusPath = "/org/freedesktop/DBus";
	const string DBusInterface = "org.freedesktop.DBus";

	static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(60);

	readonly SystemdManagerService _systemd;
	readonly SessionBusConnection _bus;
	readonly LogindSessionService _logindSession;
	readonly IHostApplicationLifetime _lifetime;
	readonly ILogger<LeaderService> _logger;
	readonly string _sessionName;

	public LeaderService(
		SystemdManagerService systemd,
		SessionBusConnection bus,
		LogindSessionService logindSession,
		IHostApplicationLifetime lifetime,
		IConfiguration configuration,
		ILogger<LeaderService> logger)
	{
		_systemd = systemd;
		_bus = bus;
		_logindSession = logindSession;
		_lifetime = lifetime;
		_logger = logger;
		_sessionName = configuration["session"] ?? "gnome";
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		var target = $"gnome-session@{_sessionName}.target";
		_logger.LogInformation("Starting target: {Target}", target);
		_logger.LogInformation("LeaderService ExecuteAsync entered. session={Session}", _sessionName);

		var shouldTriggerShutdown = false;

		try
		{
			_logger.LogInformation("Calling systemd ResetFailed.");
			await _systemd.ResetFailedAsync();
			_logger.LogInformation("ResetFailed completed.");

			_logger.LogInformation("Logind session discovery...");
			await _logindSession.DiscoverAsync();
			_logger.LogInformation("Logind session discovery complete. SessionId={SessionId}", _logindSession.SessionId ?? "(none)");

			_logger.LogInformation("Exporting environment to systemd.");
			await ExportEnvironmentToSystemdAsync();
			_logger.LogInformation("Environment export completed.");

			var connection = _bus.Connection;
			_logger.LogInformation("Creating SessionManager proxy. destination={Destination}, path={Path}", SessionManagerServiceName, SessionManagerPath);
			var sessionService = new DBusService(connection, SessionManagerServiceName);
			var sessionManager = sessionService.CreateSessionManager(SessionManagerPath);

			var sessionRunningTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			var sessionOverTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

			_logger.LogInformation("Registering SessionRunning watcher.");
			using var sessionRunningWatch = await sessionManager.WatchSessionRunningAsync(
				handler: ex =>
				{
					if (ex is null)
					{
						_logger.LogInformation("SessionRunning signal received.");
						sessionRunningTcs.TrySetResult();
					}
					else
					{
						_logger.LogError(ex, "Error on SessionRunning signal.");
						sessionRunningTcs.TrySetException(ex);
					}
				},
				emitOnCapturedContext: false);

			_logger.LogInformation("Registering SessionOver watcher.");
			using var sessionOverWatch = await sessionManager.WatchSessionOverAsync(
				handler: ex =>
				{
					if (ex is null)
					{
						_logger.LogInformation("SessionOver signal received.");
						sessionOverTcs.TrySetResult();
					}
					else
					{
						_logger.LogError(ex, "Error on SessionOver signal.");
						sessionOverTcs.TrySetException(ex);
					}
				},
				emitOnCapturedContext: false);

			_logger.LogInformation("Starting session target via systemd: {Target}", target);
			await _systemd.StartUnitAsync(target, "fail");
			_logger.LogInformation("StartUnit returned for target: {Target}", target);

			await WaitForSessionManagerAsync(connection, stoppingToken);

			_logger.LogInformation("Waiting for session to become running...");
			using (var runningTimeout = new CancellationTokenSource(WaitTimeout))
			using (var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, runningTimeout.Token))
			{
				await sessionRunningTcs.Task.WaitAsync(linked.Token);
			}

			_logger.LogInformation("Session is running.");
			shouldTriggerShutdown = true;

			_logger.LogInformation("Waiting for SessionOver or cancellation.");
			await WaitForExitAsync(stoppingToken, sessionOverTcs.Task);
			_logger.LogInformation("WaitForExitAsync completed.");

			_logger.LogInformation("Stopping session...");
			await _systemd.StartUnitAsync("gnome-session-shutdown.target", "replace-irreversibly");
			_logger.LogInformation("Shutdown target started.");
			shouldTriggerShutdown = false;
		}
		catch (OperationCanceledException)
		{
			_logger.LogInformation("LeaderService cancellation observed.");
			if (shouldTriggerShutdown)
			{
				try
				{
					_logger.LogInformation("Cancellation path: triggering shutdown target.");
					await _systemd.StartUnitAsync("gnome-session-shutdown.target", "replace-irreversibly");
					_logger.LogInformation("Cancellation path: shutdown target started.");
				}
				catch
				{
					_logger.LogWarning("Cancellation requested, but shutdown target could not be started cleanly.");
				}
			}

			_logger.LogInformation("Leader stopped.");
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Fatal error in LeaderService.");
			_lifetime.StopApplication();
			throw;
		}
	}

	async Task ExportEnvironmentToSystemdAsync()
	{
		static (string Key, string? Assignment, bool Fallback) Env(string key, string? fallback = null)
		{
			var value = Environment.GetEnvironmentVariable(key);
			if (!string.IsNullOrEmpty(value))
				return (key, key + "=" + value, false);

			return string.IsNullOrEmpty(fallback)
				? (key, null, false)
				: (key, key + "=" + fallback, true);
		}

		// XDG_SESSION_ID: from logind discovery, or fall back to the environment variable
		string? sessionIdAssignment = null;
		if (!string.IsNullOrEmpty(_logindSession.SessionId))
		{
			sessionIdAssignment = "XDG_SESSION_ID=" + _logindSession.SessionId;
			_logger.LogInformation("Exporting XDG_SESSION_ID from logind discovery: {Value}", sessionIdAssignment);
		}
		else if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("XDG_SESSION_ID")))
		{
			sessionIdAssignment = "XDG_SESSION_ID=" + Environment.GetEnvironmentVariable("XDG_SESSION_ID");
			_logger.LogInformation("Exporting XDG_SESSION_ID from environment: {Value}", sessionIdAssignment);
		}
		else
		{
			_logger.LogWarning("XDG_SESSION_ID is not available. Mutter session discovery may fail.");
		}

		var candidates = new[]
		{
			Env("DISPLAY"),
			Env("WAYLAND_DISPLAY"),
			Env("GNOME_SETUP_DISPLAY"),
			Env("DBUS_SESSION_BUS_ADDRESS"),
			Env("XDG_RUNTIME_DIR"),
			Env("XDG_SESSION_TYPE", "wayland"),
			Env("XDG_SESSION_CLASS", "user"),
			Env("XDG_CURRENT_DESKTOP", "GNOME"),
			Env("XDG_DESKTOP_SESSION", _sessionName),
			Env("DESKTOP_SESSION", _sessionName),
			Env("GDMSESSION", _sessionName),
			Env("XAUTHORITY"),
			Env("LANG"),
			Env("LC_ALL"),
			Env("GIO_USE_VFS"),
			Env("GSETTINGS_SCHEMA_DIR"),
			Env("GTK_THEME"),
			Env("XCURSOR_THEME"),
			Env("XCURSOR_SIZE"),
		};

		// Append XDG_SESSION_ID to the exported list if available
		if (sessionIdAssignment is not null)
		{
			candidates = [.. candidates, ("XDG_SESSION_ID", sessionIdAssignment, false)];
		}

		var values = candidates
			.Where(static candidate => candidate.Assignment is not null)
			.Select(static candidate => candidate.Assignment)
			.ToArray();

		var exported = candidates
			.Where(static candidate => candidate.Assignment is not null)
			.Select(static candidate => candidate.Fallback ? candidate.Key + "(fallback)" : candidate.Key)
			.ToArray();

		var missing = candidates
			.Where(static candidate => candidate.Assignment is null)
			.Select(static candidate => candidate.Key)
			.ToArray();

		_logger.LogInformation(
			"ExportEnvironmentToSystemdAsync called. candidates={CandidateCount} exported={ExportedCount} missing={MissingCount}",
			candidates.Length,
			values.Length,
			missing.Length);
		_logger.LogInformation("ExportEnvironmentToSystemdAsync exporting: {Keys}", string.Join(", ", exported));
		if (missing.Length > 0)
		{
			_logger.LogInformation("ExportEnvironmentToSystemdAsync missing optional keys: {Keys}", string.Join(", ", missing));
		}

		await _systemd.SetEnvironmentAsync(values);
		_logger.LogInformation("ExportEnvironmentToSystemdAsync completed.");
	}

	async Task WaitForSessionManagerAsync(DBusConnection connection, CancellationToken cancellationToken)
	{
		_logger.LogInformation("WaitForSessionManagerAsync started.");
		using var timeoutCts = new CancellationTokenSource(WaitTimeout);
		using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

		if (await IsServicePresentAsync(connection))
		{
			_logger.LogInformation("org.gnome.SessionManager is already present on the bus.");
			return;
		}

		var serviceAvailableTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		_logger.LogInformation("Registering NameOwnerChanged watcher for org.gnome.SessionManager.");
		using var nameOwnerChangedWatch = await connection.WatchSignalAsync(
			DBusServiceName,
			DBusPath,
			DBusInterface,
			"NameOwnerChanged",
			static (Message m, object? _) => ReadNameOwnerChanged(m),
			(Exception? ex, (string Name, string OldOwner, string NewOwner) changed) =>
			{
				if (ex is not null)
				{
					serviceAvailableTcs.TrySetException(ex);
					return;
				}

				if (changed.Name == SessionManagerServiceName && !string.IsNullOrEmpty(changed.NewOwner))
				{
					_logger.LogInformation("org.gnome.SessionManager owner appeared: {Owner}", changed.NewOwner);
					serviceAvailableTcs.TrySetResult();
				}
			},
			connection,
			false,
			ObserverFlags.None);

		// Double-check to avoid missing the transition between pre-check and subscription.
		if (await IsServicePresentAsync(connection))
		{
			_logger.LogInformation("org.gnome.SessionManager became present after watcher registration.");
			return;
		}

		try
		{
			_logger.LogInformation("Waiting on NameOwnerChanged task completion.");
			await serviceAvailableTcs.Task.WaitAsync(linkedCts.Token);
			_logger.LogInformation("WaitForSessionManagerAsync completed via NameOwnerChanged.");
		}
		catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
		{
			_logger.LogError("Timed out waiting for org.gnome.SessionManager on the session bus.");
			throw new TimeoutException("Timed out waiting for org.gnome.SessionManager on the session bus.");
		}
	}

	static async Task<bool> IsServicePresentAsync(DBusConnection connection)
	{
		var services = await connection.ListServicesAsync();
		return Array.IndexOf(services, SessionManagerServiceName) >= 0;
	}

	static (string Name, string OldOwner, string NewOwner) ReadNameOwnerChanged(Message message)
	{
		var reader = message.GetBodyReader();
		return (reader.ReadString(), reader.ReadString(), reader.ReadString());
	}

	static async Task WaitForExitAsync(CancellationToken cancellationToken, Task sessionOverTask)
	{
		var cancellationTask = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
		var completed = await Task.WhenAny(cancellationTask, sessionOverTask);
		await completed;
	}
}
