using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace GnomeSurfaceSession.Services;

/// <summary>
/// Resolves session files and autostart directories using XDG search rules.
/// </summary>
sealed class GsmSessionFillService
{
	const string SessionGroup = "GNOME Session";
	readonly ILogger<GsmSessionFillService> _logger;

	public GsmSessionFillService(ILogger<GsmSessionFillService> logger)
	{
		_logger = logger;
	}

	public SessionFillResult Load(string sessionName)
	{
		_logger.LogInformation("Loading session definition for session={SessionName}", sessionName);
		var sessionFile = FindSessionFile(sessionName);
		if (sessionFile is null)
		{
			_logger.LogWarning("Session file not found: {Session}.session", sessionName);
			return SessionFillResult.Failure($"Session file not found: {sessionName}.session");
		}

		_logger.LogInformation("Session file resolved to {SessionFile}", sessionFile);

		if (!TryReadKiosk(sessionFile, out var isKiosk, out var parseError))
		{
			_logger.LogWarning("Session file parse failed for {SessionFile}: {Error}", sessionFile, parseError);
			return SessionFillResult.Failure(parseError ?? "Failed to parse session file.");
		}

		var autostartDirs = isKiosk
			? []
			: GetAutostartDirectories().Distinct(StringComparer.Ordinal).ToArray();

		_logger.LogInformation(
			"Session fill completed: kiosk={IsKiosk}, autostartDirCount={Count}",
			isKiosk,
			autostartDirs.Length);

		return SessionFillResult.Success(sessionFile, isKiosk, autostartDirs);
	}

	static string? FindSessionFile(string sessionName)
	{
		var fileName = sessionName + ".session";

		foreach (var dir in EnumerateSessionSearchRoots())
		{
			var path = Path.Combine(dir, "gnome-session", "sessions", fileName);
			if (File.Exists(path))
			{
				return path;
			}
		}

		return null;
	}

	static IEnumerable<string> EnumerateSessionSearchRoots()
	{
		yield return GetUserConfigDir();

		foreach (var d in GetXdgList("XDG_CONFIG_DIRS", "/etc/xdg"))
			yield return d;

		foreach (var d in GetXdgList("XDG_DATA_DIRS", "/usr/local/share:/usr/share"))
			yield return d;
	}

	static bool TryReadKiosk(string sessionFile, out bool kiosk, out string? error)
	{
		kiosk = false;
		error = null;

		string? currentSection = null;

		try
		{
			foreach (var raw in File.ReadLines(sessionFile))
			{
				var line = raw.Trim();
				if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
					continue;

				if (line.StartsWith('[') && line.EndsWith(']'))
				{
					currentSection = line[1..^1].Trim();
					continue;
				}

				if (!string.Equals(currentSection, SessionGroup, StringComparison.Ordinal))
					continue;

				var eq = line.IndexOf('=', StringComparison.Ordinal);
				if (eq <= 0)
					continue;

				var key = line[..eq].Trim();
				var value = line[(eq + 1)..].Trim();
				if (!string.Equals(key, "Kiosk", StringComparison.Ordinal))
					continue;

				kiosk = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
					|| string.Equals(value, "1", StringComparison.Ordinal);
				return true;
			}

			// Missing key defaults to false in gnome-session.
			return true;
		}
		catch (Exception ex)
		{
			error = $"Failed to read session file '{sessionFile}': {ex.Message}";
			return false;
		}
	}

	static string[] GetAutostartDirectories()
	{
		var list = new List<string>();

		list.Add(Path.Combine(GetUserConfigDir(), "autostart"));

		foreach (var dir in GetXdgList("XDG_DATA_DIRS", "/usr/local/share:/usr/share"))
			list.Add(Path.Combine(dir, "gnome", "autostart"));

		foreach (var dir in GetXdgList("XDG_CONFIG_DIRS", "/etc/xdg"))
			list.Add(Path.Combine(dir, "autostart"));

		return list.ToArray();
	}

	static string GetUserConfigDir()
	{
		var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
		if (!string.IsNullOrWhiteSpace(configHome))
			return configHome;

		var home = Environment.GetEnvironmentVariable("HOME");
		if (string.IsNullOrWhiteSpace(home))
			return ".config";

		return Path.Combine(home, ".config");
	}

	static IEnumerable<string> GetXdgList(string variable, string defaultValue)
	{
		var value = Environment.GetEnvironmentVariable(variable);
		if (string.IsNullOrWhiteSpace(value))
			value = defaultValue;

		return value
			.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Where(static path => !string.IsNullOrWhiteSpace(path));
	}
}

/// <summary>
/// Result of session file discovery and basic parsing.
/// </summary>
sealed record SessionFillResult(
	bool IsSuccess,
	string? Error,
	string? SessionFilePath,
	bool IsKiosk,
	IReadOnlyList<string> AutostartDirectories)
{
	public static SessionFillResult Success(string sessionFilePath, bool isKiosk, IReadOnlyList<string> autostartDirectories) =>
		new(true, null, sessionFilePath, isKiosk, autostartDirectories);

	public static SessionFillResult Failure(string error) =>
		new(false, error, null, false, Array.Empty<string>());
}
