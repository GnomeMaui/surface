using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tmds.DBus.Protocol;
using Tmds.Systemd;

namespace GnomeSurfaceShell.Services;

sealed record ShellCommandLine(string[] Args);

/// <summary>
/// Owns MetaContext lifecycle for the shell process.
/// This service is intentionally DI-first and keeps all runtime wiring in one place.
/// </summary>
sealed partial class ShellRuntimeService : IHostedService
{
	readonly ShellCommandLine _commandLine;
	readonly IServiceProvider _services;
	readonly ShellRuntimeState _runtimeState;
	readonly IHostApplicationLifetime _lifetime;
	readonly ILogger<ShellRuntimeService> _logger;

	Meta.Context? _context;
	Thread? _runtimeThread;
	TaskCompletionSource? _startedTcs;

	public ShellRuntimeService(
		ShellCommandLine commandLine,
		IServiceProvider services,
		ShellRuntimeState runtimeState,
		IHostApplicationLifetime lifetime,
		ILogger<ShellRuntimeService> logger)
	{
		_commandLine = commandLine;
		_services = services;
		_runtimeState = runtimeState;
		_lifetime = lifetime;
		_logger = logger;
	}

	public Task StartAsync(CancellationToken cancellationToken)
	{
		_logger.LogInformation("Starting MetaContext runtime.");
		_runtimeState.SetShellReady(false);

		_startedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		_runtimeThread = new Thread(RunRuntimeThread)
		{
			IsBackground = false,
			Name = "GnomeSurfaceShell MetaContext"
		};
		_runtimeThread.Start();

		return _startedTcs.Task.WaitAsync(cancellationToken);
	}

	void RunRuntimeThread()
	{
		try
		{
			EnsureXdgSessionIdForMutterAsync().GetAwaiter().GetResult();

			_logger.LogInformation(
				"MetaContext runtime thread started. managed_thread_id={ThreadId}",
				Environment.CurrentManagedThreadId);

			_context = Meta.Functions.CreateContext("G");
			_logger.LogInformation("MetaContext created on runtime thread.");
			_runtimeState.SetMetaContext(_context, Environment.CurrentManagedThreadId);
			_logger.LogInformation(
				"MetaContext published to runtime state. managed_thread_id={ThreadId}",
				Environment.CurrentManagedThreadId);

			string[]? argv = BuildMetaArgv(_commandLine.Args);
			_logger.LogInformation("Calling MetaContext.Configure on runtime thread. argc={ArgCount}", argv?.Length ?? 0);
			if (!_context.Configure(ref argv))
				throw new InvalidOperationException("MetaContext.Configure failed.");
			_logger.LogInformation("MetaContext.Configure completed.");

			_context.SetPluginGtype(GnomeShellPlugin.GetGType(_services));
			_logger.LogInformation("MetaContext plugin GType configured.");

			_logger.LogInformation("Calling MetaContext.Setup on runtime thread.");
			if (!_context.Setup())
				throw new InvalidOperationException("MetaContext.Setup failed.");
			_logger.LogInformation("MetaContext.Setup completed.");

			_logger.LogInformation("Calling MetaContext.Start on runtime thread.");
			if (!_context.Start())
				throw new InvalidOperationException("MetaContext.Start failed.");
			_logger.LogInformation("MetaContext.Start completed.");

			_context.NotifyReady();
			_logger.LogInformation("MetaContext.NotifyReady completed.");

			// Notify systemd that the service is ready (Type=notify).
			ServiceManager.Notify(ServiceState.Ready);
			_logger.LogInformation("Notified systemd: READY=1");

			_startedTcs?.TrySetResult();

			_logger.LogInformation(
				"Entering MetaContext main loop on runtime thread. managed_thread_id={ThreadId}",
				Environment.CurrentManagedThreadId);

			var ok = _context.RunMainLoop();
			if (!ok)
			{
				_logger.LogError("MetaContext.RunMainLoop exited with error.");
			}
			else
			{
				_logger.LogInformation("MetaContext main loop exited.");
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Unhandled exception in MetaContext runtime thread.");
			_startedTcs?.TrySetException(ex);
		}
		finally
		{
			_runtimeState.SetMetaContext(null, null);
			_logger.LogInformation("MetaContext removed from runtime state.");
			_lifetime.StopApplication();
		}
	}

	static string[] BuildMetaArgv(string[] args)
	{
		var argv = new string[args.Length + 1];
		argv[0] = Environment.ProcessPath ?? AppDomain.CurrentDomain.FriendlyName;
		Array.Copy(args, 0, argv, 1, args.Length);
		return argv;
	}

	public async Task StopAsync(CancellationToken cancellationToken)
	{
		_logger.LogInformation("Stopping shell runtime service.");
		_runtimeState.SetShellReady(false);

		if (_runtimeThread is not null && _runtimeThread.IsAlive)
		{
			// We currently rely on MetaContext loop shutdown path; if it does not exit,
			// host shutdown timeout will enforce process termination.
			while (_runtimeThread.IsAlive && !cancellationToken.IsCancellationRequested)
			{
				await Task.Delay(100, cancellationToken);
			}
		}
	}

	async Task EnsureXdgSessionIdForMutterAsync()
	{
		var existing = Environment.GetEnvironmentVariable("XDG_SESSION_ID");
		if (!string.IsNullOrWhiteSpace(existing))
		{
			SetProcessEnvironmentVariable("XDG_SESSION_ID", existing);
			_logger.LogInformation("Shell startup: XDG_SESSION_ID is already set to {SessionId}.", existing);
			return;
		}

		try
		{
			var address = DBusAddress.System
				?? throw new InvalidOperationException("System bus address is not available.");

			using var connection = new DBusConnection(address);
			await connection.ConnectAsync();

			var sessionPath = await TryGetSessionPathByPidAsync(connection, (uint)Environment.ProcessId)
				?? await TryDiscoverSessionPathFromParentChainAsync(connection, Environment.ProcessId);

			if (sessionPath is null)
			{
				_logger.LogWarning("Shell startup: logind session could not be discovered. MetaContext.Setup may fail.");
				return;
			}

			var sessionId = await GetSessionStringPropertyAsync(connection, sessionPath.Value, "Id");
			if (string.IsNullOrWhiteSpace(sessionId))
			{
				_logger.LogWarning("Shell startup: logind session path resolved but session Id is empty. path={Path}", sessionPath.Value);
				return;
			}

			var sessionType = await GetSessionStringPropertyAsync(connection, sessionPath.Value, "Type");
			var sessionClass = await GetSessionStringPropertyAsync(connection, sessionPath.Value, "Class");
			var sessionState = await GetSessionStringPropertyAsync(connection, sessionPath.Value, "State");

			SetProcessEnvironmentVariable("XDG_SESSION_ID", sessionId);

			// Keep shell-local environment aligned with the discovered logind session.
			if (!string.IsNullOrWhiteSpace(sessionType))
			{
				SetProcessEnvironmentVariable("XDG_SESSION_TYPE", sessionType);
			}

			if (!string.IsNullOrWhiteSpace(sessionClass))
			{
				SetProcessEnvironmentVariable("XDG_SESSION_CLASS", sessionClass);
			}

			var nativeValue = GetProcessEnvironmentVariableNative("XDG_SESSION_ID") ?? "(null)";
			_logger.LogInformation(
				"Shell startup: set XDG_SESSION_ID={SessionId} from logind. path={Path}, class={Class}, type={Type}, state={State}, native_getenv={NativeValue}",
				sessionId,
				sessionPath.Value,
				sessionClass,
				sessionType,
				sessionState,
				nativeValue);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Shell startup: failed to initialize XDG_SESSION_ID from logind.");
		}
	}

	static async Task<ObjectPath?> TryDiscoverSessionPathFromParentChainAsync(DBusConnection connection, int startingPid)
	{
		var current = startingPid;
		while (current > 1)
		{
			var path = await TryGetSessionPathByPidAsync(connection, (uint)current);
			if (path is not null)
			{
				return path;
			}

			var parent = TryGetParentPid(current);
			if (parent <= 0 || parent == current)
			{
				break;
			}

			current = parent;
		}

		return null;
	}

	static async Task<ObjectPath?> TryGetSessionPathByPidAsync(DBusConnection connection, uint pid)
	{
		try
		{
			return await connection.CallMethodAsync(
				CreateGetSessionByPidMessage(connection, pid),
				static (Message message, object? _) => ReadObjectPath(message),
				null);
		}
		catch
		{
			return null;
		}
	}

	static async Task<string> GetSessionStringPropertyAsync(DBusConnection connection, ObjectPath sessionPath, string property)
	{
		return await connection.CallMethodAsync(
			CreateGetSessionPropertyMessage(connection, sessionPath, property),
			static (Message message, object? _) => ReadStringVariant(message),
			null);
	}

	static MessageBuffer CreateGetSessionByPidMessage(DBusConnection connection, uint pid)
	{
		var writer = connection.GetMessageWriter();
		writer.WriteMethodCallHeader(
			destination: "org.freedesktop.login1",
			path: "/org/freedesktop/login1",
			@interface: "org.freedesktop.login1.Manager",
			signature: "u",
			member: "GetSessionByPID");
		writer.WriteUInt32(pid);
		return writer.CreateMessage();
	}

	static MessageBuffer CreateGetSessionPropertyMessage(DBusConnection connection, ObjectPath sessionPath, string property)
	{
		var writer = connection.GetMessageWriter();
		writer.WriteMethodCallHeader(
			destination: "org.freedesktop.login1",
			path: sessionPath,
			@interface: "org.freedesktop.DBus.Properties",
			signature: "ss",
			member: "Get");
		writer.WriteString("org.freedesktop.login1.Session");
		writer.WriteString(property);
		return writer.CreateMessage();
	}

	static ObjectPath ReadObjectPath(Message message)
	{
		var reader = message.GetBodyReader();
		return reader.ReadObjectPath();
	}

	static string ReadStringVariant(Message message)
	{
		var reader = message.GetBodyReader();
		var value = reader.ReadVariantValue();
		return value.GetString();
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

	static void SetProcessEnvironmentVariable(string name, string value)
	{
		Environment.SetEnvironmentVariable(name, value);
		SetEnv(name, value, overwrite: 1);
	}

	static string? GetProcessEnvironmentVariableNative(string name)
	{
		var ptr = GetEnv(name);
		if (ptr == IntPtr.Zero)
		{
			return null;
		}

		return Marshal.PtrToStringUTF8(ptr);
	}

	[LibraryImport("libc", EntryPoint = "setenv", StringMarshalling = StringMarshalling.Utf8)]
	private static partial int SetEnv(string name, string value, int overwrite);

	[LibraryImport("libc", EntryPoint = "getenv", StringMarshalling = StringMarshalling.Utf8)]
	private static partial IntPtr GetEnv(string name);
}
