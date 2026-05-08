using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GnomeSurfaceSession.DBus;
using Microsoft.Extensions.Logging;
using Tmds.DBus.Protocol;

namespace GnomeSurfaceSession.Services;

/// <summary>
/// Singleton service wrapping the org.freedesktop.systemd1.Manager DBus proxy.
/// The Lazy ensures the proxy is created only after SessionBusConnection.StartAsync completes.
/// </summary>
sealed class SystemdManagerService
{
    const string SystemdServiceName = "org.freedesktop.systemd1";
    const string SystemdPath = "/org/freedesktop/systemd1";

    readonly Lazy<Manager> _manager;
    readonly Lazy<Login1Manager> _login1;
    readonly ILogger<SystemdManagerService> _logger;

    public SystemdManagerService(
        SessionBusConnection sessionBus,
        SystemBusConnection systemBus,
        ILogger<SystemdManagerService> logger)
    {
        _logger = logger;
        _manager = new Lazy<Manager>(() =>
        {
            _logger.LogInformation("Initializing org.freedesktop.systemd1.Manager proxy on session bus.");
            var service = new DBusService(sessionBus.Connection, SystemdServiceName);
            return service.CreateManager(SystemdPath);
        });

        _login1 = new Lazy<Login1Manager>(() =>
        {
            _logger.LogInformation("Initializing org.freedesktop.login1.Manager proxy on system bus.");
            return new Login1Manager(systemBus.Connection);
        });
    }

    public Task ResetFailedAsync()
    {
        _logger.LogInformation("DBus call -> systemd1.ResetFailed");
        return _manager.Value.ResetFailedAsync();
    }

    public Task StartUnitAsync(string name, string mode)
    {
        _logger.LogInformation("DBus call -> systemd1.StartUnit(name={Name}, mode={Mode})", name, mode);
        return _manager.Value.StartUnitAsync(name, mode);
    }

    public async Task SetEnvironmentAsync(IEnumerable<string?> assignments)
    {
        var valid = assignments
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        if (valid.Length > 0)
        {
            _logger.LogInformation("DBus call -> systemd1.SetEnvironment(count={Count})", valid.Length);
            await _manager.Value.SetEnvironmentAsync(valid!);
        }
        else
        {
            _logger.LogInformation("systemd1.SetEnvironment skipped (no valid assignments).");
        }
    }

    public Task PowerOffAsync()
    {
        _logger.LogInformation("DBus call -> login1.PowerOff(interactive=true)");
        return _login1.Value.PowerOffAsync(interactive: true);
    }

    public Task RebootAsync()
    {
        _logger.LogInformation("DBus call -> login1.Reboot(interactive=true)");
        return _login1.Value.RebootAsync(interactive: true);
    }

    public Task SuspendAsync()
    {
        _logger.LogInformation("DBus call -> login1.Suspend(interactive=true)");
        return _login1.Value.SuspendAsync(interactive: true);
    }

    public async Task<uint> CanPowerOffAsync()
    {
        var raw = await _login1.Value.CanPowerOffAsync();
        var mapped = ToActionAvailability(raw);
        _logger.LogInformation("DBus call -> login1.CanPowerOff raw={Raw} mapped={Mapped}", raw, mapped);
        return mapped;
    }

    public async Task<uint> CanRebootAsync()
    {
        var raw = await _login1.Value.CanRebootAsync();
        var mapped = ToActionAvailability(raw);
        _logger.LogInformation("DBus call -> login1.CanReboot raw={Raw} mapped={Mapped}", raw, mapped);
        return mapped;
    }

    public async Task<uint> CanSuspendAsync()
    {
        var raw = await _login1.Value.CanSuspendAsync();
        var mapped = ToActionAvailability(raw);
        _logger.LogInformation("DBus call -> login1.CanSuspend raw={Raw} mapped={Mapped}", raw, mapped);
        return mapped;
    }

    static uint ToActionAvailability(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            "yes" => 3u,
            "challenge" => 2u,
            "inhibited" => 2u,
            "inhibitor-blocked" => 1u,
            "challenge-inhibitor-blocked" => 1u,
            "no" => 0u,
            "na" => 0u,
            _ => 0u,
        };
    }
}
