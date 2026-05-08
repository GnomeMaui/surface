using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace GnomeSurfaceSession.Services;

/// <summary>
/// Represents one inhibitor registration entry.
/// </summary>
sealed record GsmInhibitor(uint Cookie, string ObjectPath, string AppId, string Reason, uint Flags, string Sender);

/// <summary>
/// Stores inhibitors and computes the effective inhibited action mask.
/// </summary>
sealed class GsmInhibitorStore
{
    readonly object _gate = new();
    readonly Dictionary<uint, GsmInhibitor> _byCookie = new();
    uint _nextCookie = 1;
    readonly ILogger<GsmInhibitorStore> _logger;

    public GsmInhibitorStore(ILogger<GsmInhibitorStore> logger)
    {
        _logger = logger;
    }

    public event Action<string>? InhibitorAdded;

    public event Action<string>? InhibitorRemoved;

    public uint Inhibit(string appId, string reason, uint flags, string sender)
    {
        lock (_gate)
        {
            var cookie = _nextCookie++;
            var objectPath = $"/org/gnome/SessionManager/Inhibitor{cookie}";
            var inhibitor = new GsmInhibitor(cookie, objectPath, appId, reason, flags, sender);
            _byCookie[cookie] = inhibitor;
            _logger.LogInformation(
                "Inhibitor added: cookie={Cookie}, path={ObjectPath}, appId={AppId}, flags={Flags}, sender={Sender}, total={Count}",
                cookie,
                objectPath,
                appId,
                flags,
                sender,
                _byCookie.Count);
            InhibitorAdded?.Invoke(objectPath);
            return cookie;
        }
    }

    public bool Uninhibit(uint cookie)
    {
        lock (_gate)
        {
            if (!_byCookie.Remove(cookie, out var inhibitor))
            {
                _logger.LogWarning("Uninhibit called for unknown cookie={Cookie}", cookie);
                return false;
            }

            _logger.LogInformation("Inhibitor removed: cookie={Cookie}, path={ObjectPath}, remaining={Count}", cookie, inhibitor.ObjectPath, _byCookie.Count);
            InhibitorRemoved?.Invoke(inhibitor.ObjectPath);
            return true;
        }
    }

    public bool IsInhibited(uint flags)
    {
        lock (_gate)
        {
            var inhibited = _byCookie.Values.Any(i => (i.Flags & flags) != 0);
            _logger.LogDebug("IsInhibited check: flags={Flags}, inhibited={Inhibited}, activeInhibitors={Count}", flags, inhibited, _byCookie.Count);
            return inhibited;
        }
    }

    public string[] GetInhibitorObjectPaths()
    {
        lock (_gate)
        {
            return _byCookie.Values.Select(i => i.ObjectPath).ToArray();
        }
    }

    public uint GetInhibitedActions()
    {
        lock (_gate)
        {
            uint union = 0;
            foreach (var inhibitor in _byCookie.Values)
                union |= inhibitor.Flags;
            _logger.LogDebug("GetInhibitedActions -> {Union}", union);
            return union;
        }
    }
}
