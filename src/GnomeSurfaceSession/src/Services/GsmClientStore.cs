using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace GnomeSurfaceSession.Services;

/// <summary>
/// Represents a registered session client and its DBus identity metadata.
/// </summary>
sealed record RegisteredClient(string ObjectPath, string AppId, string StartupId, string Sender);

/// <summary>
/// Stores session clients and tracks end-session response synchronization.
/// </summary>
sealed class GsmClientStore
{
    static readonly TimeSpan RegistrationQuietPeriod = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Captures an active query-end-session round and pending client replies.
    /// </summary>
    sealed class QueryState
    {
        public QueryState(HashSet<string> pending)
        {
            Pending = pending;
            Completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public HashSet<string> Pending { get; }

        public TaskCompletionSource Completion { get; }
    }

    readonly object _gate = new();
    readonly Dictionary<string, RegisteredClient> _clientsByPath = new(StringComparer.Ordinal);
    readonly ILogger<GsmClientStore> _logger;
    QueryState? _activeQuery;
    int _nextId = 1;

    public GsmClientStore(ILogger<GsmClientStore> logger)
    {
        _logger = logger;
    }

    public event Action<string>? ClientAdded;

    public event Action<string>? ClientRemoved;

    /// <summary>
    /// Raised when query-end-session phase begins (before waiting for client responses).
    /// </summary>
    public event Action? QueryEndSessionStarting;

    /// <summary>
    /// Raised when end-session phase begins.
    /// </summary>
    public event Action? EndSessionStarting;

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _clientsByPath.Count;
            }
        }
    }

    /// <summary>
    /// Returns a snapshot of all currently registered client object paths.
    /// </summary>
    public IReadOnlyList<string> GetAllClientPaths()
    {
        lock (_gate)
        {
            return new List<string>(_clientsByPath.Keys);
        }
    }

    public string RegisterClient(string appId, string startupId, string sender)
    {
        string path;
        lock (_gate)
        {
            path = $"/org/gnome/SessionManager/Client{_nextId}";
            _nextId++;

            _clientsByPath[path] = new RegisteredClient(path, appId, startupId, sender);
            _logger.LogInformation(
                "Client registered: path={ClientPath}, appId={AppId}, startupId={StartupId}, sender={Sender}, totalClients={Count}",
                path,
                appId,
                startupId,
                sender,
                _clientsByPath.Count);
        }

        // Emit after releasing lock to avoid lock-held callback side effects.
        ClientAdded?.Invoke(path);
        return path;
    }

    public bool UnregisterClient(string objectPath)
    {
        var shouldEmit = false;
        var removed = false;
        lock (_gate)
        {
            removed = _clientsByPath.Remove(objectPath);
            if (removed)
            {
                _logger.LogInformation("Client unregistered: path={ClientPath}, remainingClients={Count}", objectPath, _clientsByPath.Count);
                if (_activeQuery is not null && _activeQuery.Pending.Remove(objectPath) && _activeQuery.Pending.Count == 0)
                {
                    _logger.LogInformation("QueryEndSession pending set became empty after client removal.");
                    _activeQuery.Completion.TrySetResult();
                }

                shouldEmit = true;
            }

            if (!removed)
            {
                _logger.LogWarning("UnregisterClient called for unknown path: {ClientPath}", objectPath);
            }
        }

        if (shouldEmit)
            ClientRemoved?.Invoke(objectPath);

        return removed;
    }

    public bool HandleEndSessionResponse(string objectPath, bool isOk, string reason)
    {
        _ = isOk;
        _ = reason;

        lock (_gate)
        {
            if (!_clientsByPath.ContainsKey(objectPath))
            {
                _logger.LogWarning("EndSessionResponse from unknown client path={ClientPath}", objectPath);
                return false;
            }

            if (_activeQuery is null)
            {
                _logger.LogInformation("EndSessionResponse accepted outside active query round: path={ClientPath}", objectPath);
                return true;
            }

            if (_activeQuery.Pending.Remove(objectPath) && _activeQuery.Pending.Count == 0)
            {
                _logger.LogInformation("EndSessionResponse completed query round: path={ClientPath}", objectPath);
                _activeQuery.Completion.TrySetResult();
            }

            return true;
        }
    }

    public async Task RunQueryEndSessionAsync(CancellationToken cancellationToken)
    {
        QueryState? query;

        lock (_gate)
        {
            var pending = new HashSet<string>(_clientsByPath.Keys, StringComparer.Ordinal);
            if (pending.Count == 0)
            {
                _logger.LogInformation("RunQueryEndSessionAsync: no clients registered.");
                return;
            }

            query = _activeQuery = new QueryState(pending);
            _logger.LogInformation("RunQueryEndSessionAsync started with pendingClients={PendingCount}", pending.Count);
        }

        // Signal that query-end-session is starting (for broadcasting to clients).
        QueryEndSessionStarting?.Invoke();

        try
        {
            await query.Completion.Task.WaitAsync(cancellationToken);
            _logger.LogInformation("RunQueryEndSessionAsync completed.");
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_activeQuery, query))
                {
                    _activeQuery = null;
                    _logger.LogInformation("RunQueryEndSessionAsync cleanup done.");
                }
            }
        }
    }

    public Task RunEndSessionAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        _logger.LogInformation("RunEndSessionAsync invoked.");
        EndSessionStarting?.Invoke();
        _logger.LogInformation("RunEndSessionAsync completed (no-op placeholder).");
        return Task.CompletedTask;
    }

    public async Task WaitForInitialRegistrationStabilityAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Waiting for initial client registration stability.");
        var start = DateTime.UtcNow;
        var stableSince = start;
        var lastCount = -1;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var count = Count;
            if (count != lastCount)
            {
                lastCount = count;
                stableSince = DateTime.UtcNow;
            }

            var stableFor = DateTime.UtcNow - stableSince;
            if (stableFor >= RegistrationQuietPeriod)
            {
                _logger.LogInformation("Initial registration became stable. clientCount={Count}, stableForMs={StableForMs}", count, stableFor.TotalMilliseconds);
                return;
            }

            await Task.Delay(200, cancellationToken);
        }
    }
}
