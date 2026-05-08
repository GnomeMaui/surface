using System;
using Microsoft.Extensions.Logging;

namespace GnomeSurfaceSession.Services;

/// <summary>
/// Presence status values used by org.gnome.SessionManager.Presence.
/// </summary>
static class GsmPresenceStatus
{
    public const uint Available = 0;
    public const uint Invisible = 1;
    public const uint Busy = 2;
    public const uint Idle = 3;
}

/// <summary>
/// Stores presence state and emits change events for DBus signal forwarding.
/// </summary>
sealed class GsmPresenceService
{
    readonly object _gate = new();
    uint _status = GsmPresenceStatus.Available;
    uint _savedStatus = GsmPresenceStatus.Available;
    string _statusText = string.Empty;
    readonly ILogger<GsmPresenceService> _logger;

    public GsmPresenceService(ILogger<GsmPresenceService> logger)
    {
        _logger = logger;
    }

    public event Action<uint>? StatusChanged;

    public event Action<string>? StatusTextChanged;

    public uint Status
    {
        get
        {
            lock (_gate)
            {
                return _status;
            }
        }
    }

    public string StatusText
    {
        get
        {
            lock (_gate)
            {
                return _statusText;
            }
        }
    }

    public void SetStatus(uint status)
    {
        if (status > GsmPresenceStatus.Idle)
            status = GsmPresenceStatus.Available;

        var oldStatus = Status;

        lock (_gate)
        {
            if (_status == status)
                return;

            _status = status;
            if (status != GsmPresenceStatus.Idle)
                _savedStatus = status;
        }

        _logger.LogInformation("Presence status changed: {OldStatus} -> {NewStatus}", oldStatus, status);

        StatusChanged?.Invoke(status);
    }

    public void SetStatusText(string statusText)
    {
        statusText ??= string.Empty;
        string oldText;

        lock (_gate)
        {
            if (string.Equals(_statusText, statusText, StringComparison.Ordinal))
                return;

            oldText = _statusText;
            _statusText = statusText;
        }

        _logger.LogInformation("Presence status-text changed: '{OldText}' -> '{NewText}'", oldText, statusText);

        StatusTextChanged?.Invoke(statusText);
    }

    public void SetIdle(bool isIdle)
    {
        uint? emitStatus = null;

        lock (_gate)
        {
            if (isIdle)
            {
                if (_status == GsmPresenceStatus.Idle)
                    return;

                _savedStatus = _status;
                _status = GsmPresenceStatus.Idle;
                emitStatus = _status;
            }
            else
            {
                if (_status != GsmPresenceStatus.Idle)
                    return;

                _status = _savedStatus;
                emitStatus = _status;
            }
        }

        if (emitStatus.HasValue)
        {
            _logger.LogInformation("Presence idle transition applied. idle={IsIdle}, status={Status}", isIdle, emitStatus.Value);
            StatusChanged?.Invoke(emitStatus.Value);
        }
    }
}
