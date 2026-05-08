using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace GnomeSurfaceSession.Services;

/// <summary>
/// High-level session manager phase states.
/// </summary>
enum GsmManagerPhase
{
    Initialization = 0,
    Application = 1,
    Running = 2,
    QueryEndSession = 3,
    EndSession = 4,
    Exit = 5,
}

enum GsmLogoutMode : uint
{
    Normal = 0,
    NoConfirmation = 1,
    Force = 2,
}

enum GsmLogoutType
{
    None = 0,
    Logout = 1,
    Reboot = 2,
    Shutdown = 3,
}

/// <summary>
/// Coordinates phase transitions and logout pipeline hook execution.
/// </summary>
sealed class GsmPhaseManager
{
    static readonly TimeSpan ApplicationPhaseTimeout = TimeSpan.FromSeconds(20);
    static readonly TimeSpan QueryEndSessionTimeout = TimeSpan.FromSeconds(10);
    static readonly TimeSpan EndSessionTimeout = TimeSpan.FromSeconds(10);

    readonly object _gate = new();
    readonly List<Func<CancellationToken, Task>> _applicationHooks = [];
    readonly List<Func<CancellationToken, Task>> _queryEndSessionHooks = [];
    readonly List<Func<CancellationToken, Task>> _endSessionHooks = [];
    readonly GsmSessionFillService _sessionFill;
    readonly SystemdManagerService _systemd;
    readonly ILogger<GsmPhaseManager> _logger;
    bool _logoutInProgress;
    GsmLogoutMode _logoutMode = GsmLogoutMode.Normal;
    GsmLogoutType _logoutType = GsmLogoutType.None;

    public event Action? SessionRunning;

    public event Action? SessionOver;

    public event Action<GsmManagerPhase>? PhaseChanged;

    public string SessionName { get; set; } = "gnome";

    public string? ActiveSessionFilePath { get; private set; }

    public bool IsKioskSession { get; private set; }

    public IReadOnlyList<string> AutostartDirectories { get; private set; } = Array.Empty<string>();

    public string? LastInitializationError { get; private set; }

    public string Renderer { get; set; } = "unknown";

    public bool SessionIsActive { get; set; } = true;

    public uint InhibitedActions { get; set; }

    public bool RestoreSupported { get; set; }

    public GsmManagerPhase Phase { get; private set; } = GsmManagerPhase.Initialization;

    public bool IsSessionRunning => Phase == GsmManagerPhase.Running;

    public bool CanSetEnvironment => Phase == GsmManagerPhase.Initialization;

    public bool CanRequestLogout => Phase == GsmManagerPhase.Running;

    public bool CanPerformPowerActions => Phase == GsmManagerPhase.Running;

    public GsmPhaseManager(
        GsmSessionFillService sessionFill,
        SystemdManagerService systemd,
        ILogger<GsmPhaseManager> logger)
    {
        _sessionFill = sessionFill;
        _systemd = systemd;
        _logger = logger;
    }

    public bool HandleInitialized()
    {
        if (Phase != GsmManagerPhase.Initialization)
            return false;

        var fill = _sessionFill.Load(SessionName);
        if (!fill.IsSuccess)
        {
            LastInitializationError = fill.Error;
            _logger.LogError("Session fill failed for '{SessionName}': {Error}", SessionName, fill.Error);
            return false;
        }

        ActiveSessionFilePath = fill.SessionFilePath;
        IsKioskSession = fill.IsKiosk;
        AutostartDirectories = fill.AutostartDirectories;
        LastInitializationError = null;

        _logger.LogInformation(
            "Loaded session '{SessionName}' from {SessionFile}; kiosk={Kiosk}; autostartDirs={AutostartDirCount}",
            SessionName,
            ActiveSessionFilePath,
            IsKioskSession,
            AutostartDirectories.Count);

        SetPhase(GsmManagerPhase.Application);
        _ = RunApplicationPhaseAsync();
        return true;
    }

    public IDisposable RegisterApplicationHook(Func<CancellationToken, Task> hook)
    {
        ArgumentNullException.ThrowIfNull(hook);

        lock (_gate)
        {
            _applicationHooks.Add(hook);
        }

        return new HookRegistration(this, hook, HookKind.Application);
    }

    public bool HandleInitializationError(bool fatal)
    {
        _logger.LogInformation("HandleInitializationError called: fatal={Fatal}, phase={Phase}", fatal, Phase);
        if (!fatal)
            return false;

        if (Phase == GsmManagerPhase.Exit)
            return false;

        SetPhase(GsmManagerPhase.Exit);
        SessionOver?.Invoke();
        return true;
    }

    public bool HandleLogout(GsmLogoutMode mode)
    {
        _logger.LogInformation("HandleLogout called: mode={Mode}, phase={Phase}", mode, Phase);
        lock (_gate)
        {
            return BeginLogoutLocked(mode, GsmLogoutType.Logout);
        }
    }

    public bool HandleShutdown()
    {
        _logger.LogInformation("HandleShutdown called: phase={Phase}", Phase);
        lock (_gate)
        {
            return BeginLogoutLocked(GsmLogoutMode.NoConfirmation, GsmLogoutType.Shutdown);
        }
    }

    public bool HandleReboot()
    {
        _logger.LogInformation("HandleReboot called: phase={Phase}", Phase);
        lock (_gate)
        {
            return BeginLogoutLocked(GsmLogoutMode.NoConfirmation, GsmLogoutType.Reboot);
        }
    }

    bool BeginLogoutLocked(GsmLogoutMode mode, GsmLogoutType logoutType)
    {
        if (!CanRequestLogout || _logoutInProgress)
        {
            _logger.LogWarning(
                "BeginLogoutLocked rejected: mode={Mode}, type={LogoutType}, canRequestLogout={CanRequestLogout}, logoutInProgress={LogoutInProgress}, phase={Phase}",
                mode,
                logoutType,
                CanRequestLogout,
                _logoutInProgress,
                Phase);
            return false;
        }

        _logoutInProgress = true;
        _logoutMode = mode;
        _logoutType = logoutType;
        _logger.LogInformation("BeginLogoutLocked accepted: mode={Mode}, type={LogoutType}", mode, logoutType);
        SetPhase(GsmManagerPhase.QueryEndSession);
        _ = RunLogoutPipelineAsync();
        return true;
    }

    public IDisposable RegisterQueryEndSessionHook(Func<CancellationToken, Task> hook)
    {
        ArgumentNullException.ThrowIfNull(hook);

        lock (_gate)
        {
            _queryEndSessionHooks.Add(hook);
        }

        return new HookRegistration(this, hook, HookKind.QueryEndSession);
    }

    public IDisposable RegisterEndSessionHook(Func<CancellationToken, Task> hook)
    {
        ArgumentNullException.ThrowIfNull(hook);

        lock (_gate)
        {
            _endSessionHooks.Add(hook);
        }

        return new HookRegistration(this, hook, HookKind.EndSession);
    }

    async Task RunApplicationPhaseAsync()
    {
        _logger.LogInformation("RunApplicationPhaseAsync started.");
        try
        {
            await InvokeHooksWithTimeoutAsync(_applicationHooks, ApplicationPhaseTimeout);
        }
        catch
        {
            // Keep transitioning to RUNNING even if startup hooks fail.
        }

        lock (_gate)
        {
            if (Phase != GsmManagerPhase.Application)
                return;

            SetPhase(GsmManagerPhase.Running);
        }

        SessionRunning?.Invoke();
        _logger.LogInformation("RunApplicationPhaseAsync finished. SessionRunning emitted.");
    }

    async Task RunLogoutPipelineAsync()
    {
        GsmLogoutMode logoutMode;
        GsmLogoutType logoutType;

        try
        {
            lock (_gate)
            {
                logoutMode = _logoutMode;
                logoutType = _logoutType;
            }

            if (logoutMode != GsmLogoutMode.Force)
            {
                _logger.LogInformation("RunLogoutPipelineAsync query phase started.");
                await InvokeHooksWithTimeoutAsync(_queryEndSessionHooks, QueryEndSessionTimeout);

                SetPhase(GsmManagerPhase.EndSession);
                _logger.LogInformation("RunLogoutPipelineAsync end-session phase started.");
                await InvokeHooksWithTimeoutAsync(_endSessionHooks, EndSessionTimeout);
            }
            else
            {
                _logger.LogInformation("RunLogoutPipelineAsync force mode: skipping query hooks.");
                SetPhase(GsmManagerPhase.EndSession);
            }

            switch (logoutType)
            {
                case GsmLogoutType.Shutdown:
                    _logger.LogInformation("RunLogoutPipelineAsync executing shutdown via login1.");
                    await _systemd.PowerOffAsync();
                    break;
                case GsmLogoutType.Reboot:
                    _logger.LogInformation("RunLogoutPipelineAsync executing reboot via login1.");
                    await _systemd.RebootAsync();
                    break;
                default:
                    _logger.LogInformation("RunLogoutPipelineAsync completed logout without power action.");
                    break;
            }
        }
        catch (Exception ex)
        {
            // The manager still exits even when hooks fail.
            _logger.LogWarning(ex, "Logout pipeline failed while processing action {LogoutType} in mode {LogoutMode}.", _logoutType, _logoutMode);
        }
        finally
        {
            lock (_gate)
            {
                SetPhase(GsmManagerPhase.Exit);
                _logoutInProgress = false;
                _logoutType = GsmLogoutType.None;
                _logoutMode = GsmLogoutMode.Normal;
            }

            SessionOver?.Invoke();
            _logger.LogInformation("RunLogoutPipelineAsync finished. SessionOver emitted.");
        }
    }

    static async Task InvokeHooksWithTimeoutAsync(
        IEnumerable<Func<CancellationToken, Task>> hooks,
        TimeSpan timeout)
    {
        var snapshot = hooks.ToArray();
        if (snapshot.Length == 0)
            return;

        using var timeoutCts = new CancellationTokenSource(timeout);
        var tasks = snapshot.Select(h => SafeInvokeHookAsync(h, timeoutCts.Token)).ToArray();
        await Task.WhenAll(tasks);
    }

    static async Task SafeInvokeHookAsync(Func<CancellationToken, Task> hook, CancellationToken cancellationToken)
    {
        try
        {
            await hook(cancellationToken);
        }
        catch
        {
            // Hook failures are intentionally ignored at this stage.
        }
    }

    void UnregisterHook(Func<CancellationToken, Task> hook, HookKind kind)
    {
        lock (_gate)
        {
            switch (kind)
            {
                case HookKind.Application:
                    _applicationHooks.Remove(hook);
                    break;
                case HookKind.QueryEndSession:
                    _queryEndSessionHooks.Remove(hook);
                    break;
                case HookKind.EndSession:
                    _endSessionHooks.Remove(hook);
                    break;
            }
        }
    }

    void SetPhase(GsmManagerPhase phase)
    {
        if (Phase == phase)
            return;

        Phase = phase;
        PhaseChanged?.Invoke(phase);
    }

    /// <summary>
    /// Disposable registration handle for phase hook subscriptions.
    /// </summary>
    sealed class HookRegistration : IDisposable
    {
        readonly GsmPhaseManager _owner;
        readonly Func<CancellationToken, Task> _hook;
        readonly HookKind _kind;
        bool _disposed;

        public HookRegistration(GsmPhaseManager owner, Func<CancellationToken, Task> hook, HookKind kind)
        {
            _owner = owner;
            _hook = hook;
            _kind = kind;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _owner.UnregisterHook(_hook, _kind);
        }
    }

    enum HookKind
    {
        Application,
        QueryEndSession,
        EndSession,
    }
}
