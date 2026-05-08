using System;
using System.IO;
using System.Threading.Tasks;
using GnomeSurfaceSession.DBus;
using Microsoft.Extensions.Logging;
using Tmds.DBus.Protocol;

namespace GnomeSurfaceSession.Services;

/// <summary>
/// Service responsible for discovering the logind session.
/// Mutter's MetaContext.Setup() tries three methods to find a logind session;
/// this service mirrors that logic on the C# side so that XDG_SESSION_ID
/// can be exported into the systemd user environment before Mutter starts.
///
/// Discovery order (aligned with meta-launcher.c):
///   1. XDG_SESSION_ID environment variable (if already set)
///   2. GetSessionByPID(0) → logind resolves the session for the calling process
/// </summary>
sealed class LogindSessionService
{
	readonly SystemBusConnection _systemBus;
	readonly ILogger<LogindSessionService> _logger;

	/// <summary>
	/// The discovered session identifier (e.g. "c1"), or null if discovery failed.
	/// This value is exported as XDG_SESSION_ID into the systemd user environment.
	/// </summary>
	public string? SessionId { get; private set; }

	public LogindSessionService(
		SystemBusConnection systemBus,
		ILogger<LogindSessionService> logger)
	{
		_systemBus = systemBus;
		_logger = logger;
	}

	/// <summary>
	/// Attempts to discover the logind session.
	/// Logs a warning and returns null if discovery fails.
	/// </summary>
	public async Task<string?> DiscoverAsync()
	{
		var login1 = new Login1Manager(_systemBus.Connection);

		// Step 1: XDG_SESSION_ID environment variable
		var envId = Environment.GetEnvironmentVariable("XDG_SESSION_ID");
		if (!string.IsNullOrEmpty(envId))
		{
			_logger.LogInformation(
				"Logind session ID from environment variable: XDG_SESSION_ID={SessionId}", envId);
			SessionId = envId;
			return envId;
		}

		// Step 2: GetSessionByPID(0) — logind resolves the session for the calling process
		try
		{
			var sessionPath = await login1.GetSessionByPidAsync(pid: 0);

			_logger.LogInformation(
				"GetSessionByPID(0) succeeded: session path={Path}", sessionPath);

			var session = new Login1Session(_systemBus.Connection, sessionPath);
			var id = await session.GetIdAsync();

			_logger.LogInformation(
				"Logind session ID discovered: {SessionId} (path={Path})", id, sessionPath);

			SessionId = id;
			return id;
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex,
				"GetSessionByPID(0) failed, attempting parent PID walk to locate a usable session.");
		}

		// Step 3: walk parent PIDs (self -> parent -> ...), then ask logind for each PID.
		// This is needed in linger/systemd-user-manager setups where child processes may not
		// be directly mapped by GetSessionByPID(0), while an ancestor PID still maps to c1.
		var currentPid = Environment.ProcessId;
		while (currentPid > 1)
		{
			try
			{
				var sessionPath = await login1.GetSessionByPidAsync((uint)currentPid);
				var session = new Login1Session(_systemBus.Connection, sessionPath);
				var id = await session.GetIdAsync();

				_logger.LogInformation(
					"Logind session discovered from PID chain: pid={Pid}, sessionId={SessionId}, path={Path}",
					currentPid,
					id,
					sessionPath);

				SessionId = id;
				return id;
			}
			catch (Exception)
			{
				// Ignore and continue walking up the process tree.
			}

			var next = TryGetParentPid(currentPid);
			if (next <= 0 || next == currentPid)
			{
				break;
			}

			currentPid = next;
		}

		_logger.LogWarning(
			"Could not discover a logind session using environment, GetSessionByPID(0), or parent PID walk. " +
			"Ensure pam_systemd is active and the user services run in a real login session. Proceeding without XDG_SESSION_ID.");

		return null;
	}

	static int TryGetParentPid(int pid)
	{
		try
		{
			var stat = File.ReadAllText($"/proc/{pid}/stat");
			var endOfCommand = stat.LastIndexOf(')');
			if (endOfCommand < 0 || endOfCommand + 4 >= stat.Length)
			{
				return -1;
			}

			var after = stat[(endOfCommand + 2)..];
			var parts = after.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length < 3)
			{
				return -1;
			}

			return int.TryParse(parts[1], out var ppid) ? ppid : -1;
		}
		catch
		{
			return -1;
		}
	}
}
