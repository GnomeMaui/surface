using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GnomeSurfaceShell.DBus;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tmds.DBus.Protocol;

namespace GnomeSurfaceShell.Services;

/// <summary>
/// Hosts org.gnome.Shell DBus name and registers the method handler.
/// </summary>
sealed class ShellDbusHostService : IHostedService
{
    readonly SessionBusConnection _bus;
    readonly GnomeShellServer _server;
    readonly GnomeShellBrightnessServer _brightnessServer;
    readonly GnomeMutterDisplayConfigServer _displayConfigServer;
    readonly GnomeMutterServiceChannelServer _serviceChannelServer;
    readonly GnomeShellIntrospectServer _introspectServer;
    readonly GnomeScreenSaverServer _screenSaverServer;
    readonly ShellRuntimeState _runtimeState;
    readonly ILogger<ShellDbusHostService> _logger;

    bool _started;

    public ShellDbusHostService(
        SessionBusConnection bus,
        GnomeShellServer server,
        GnomeShellBrightnessServer brightnessServer,
        GnomeMutterDisplayConfigServer displayConfigServer,
        GnomeMutterServiceChannelServer serviceChannelServer,
        GnomeShellIntrospectServer introspectServer,
        GnomeScreenSaverServer screenSaverServer,
        ShellRuntimeState runtimeState,
        ILogger<ShellDbusHostService> logger)
    {
        _bus = bus;
        _server = server;
        _brightnessServer = brightnessServer;
        _displayConfigServer = displayConfigServer;
        _serviceChannelServer = serviceChannelServer;
        _introspectServer = introspectServer;
        _screenSaverServer = screenSaverServer;
        _runtimeState = runtimeState;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var connection = _bus.Connection;

        connection.AddMethodHandler(_server);
        connection.AddMethodHandler(_brightnessServer);
        connection.AddMethodHandler(_displayConfigServer);
        connection.AddMethodHandler(_serviceChannelServer);
        connection.AddMethodHandler(_introspectServer);
        connection.AddMethodHandler(_screenSaverServer);

        var acquired = await connection.TryRequestNameAsync(
            GnomeShellServer.ServiceName,
            RequestNameOptions.None);

        if (!acquired)
        {
            throw new InvalidOperationException($"Failed to acquire DBus name '{GnomeShellServer.ServiceName}'.");
        }

        var brightnessAcquired = await connection.TryRequestNameAsync(
            GnomeShellBrightnessServer.ServiceName,
            RequestNameOptions.None);

        if (!brightnessAcquired)
        {
            throw new InvalidOperationException($"Failed to acquire DBus name '{GnomeShellBrightnessServer.ServiceName}'.");
        }

        var displayConfigAcquired = await connection.TryRequestNameAsync(
            GnomeMutterDisplayConfigServer.ServiceName,
            RequestNameOptions.None);

        if (!displayConfigAcquired)
        {
            throw new InvalidOperationException($"Failed to acquire DBus name '{GnomeMutterDisplayConfigServer.ServiceName}'.");
        }

        var serviceChannelAcquired = await connection.TryRequestNameAsync(
            GnomeMutterServiceChannelServer.ServiceName,
            RequestNameOptions.None);

        if (!serviceChannelAcquired)
        {
            throw new InvalidOperationException($"Failed to acquire DBus name '{GnomeMutterServiceChannelServer.ServiceName}'.");
        }

        var screenSaverAcquired = await connection.TryRequestNameAsync(
            GnomeScreenSaverServer.ServiceName,
            RequestNameOptions.None);

        if (!screenSaverAcquired)
        {
            throw new InvalidOperationException($"Failed to acquire DBus name '{GnomeScreenSaverServer.ServiceName}'.");
        }

        var introspectAcquired = await connection.TryRequestNameAsync(
            GnomeShellIntrospectServer.ServiceName,
            RequestNameOptions.None);

        if (!introspectAcquired)
        {
            throw new InvalidOperationException($"Failed to acquire DBus name '{GnomeShellIntrospectServer.ServiceName}'.");
        }

        _started = true;
        _runtimeState.ShellReadyChanged += OnShellReadyChanged;
        _runtimeState.DisplayConfigurationChanged += OnDisplayConfigurationChanged;
        _runtimeState.WindowsChanged += OnWindowsChanged;
        _logger.LogInformation("Acquired DBus name: {ServiceName}", GnomeShellServer.ServiceName);
        _logger.LogInformation("Acquired DBus name: {ServiceName}", GnomeShellBrightnessServer.ServiceName);
        _logger.LogInformation("Acquired DBus name: {ServiceName}", GnomeMutterDisplayConfigServer.ServiceName);
        _logger.LogInformation("Acquired DBus name: {ServiceName}", GnomeMutterServiceChannelServer.ServiceName);
        _logger.LogInformation("Acquired DBus name: {ServiceName}", GnomeShellIntrospectServer.ServiceName);
        _logger.LogInformation("Acquired DBus name: {ServiceName}", GnomeScreenSaverServer.ServiceName);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!_started)
            return;

        _started = false;
        _runtimeState.ShellReadyChanged -= OnShellReadyChanged;
        _runtimeState.DisplayConfigurationChanged -= OnDisplayConfigurationChanged;
        _runtimeState.WindowsChanged -= OnWindowsChanged;

        var introspectReleased = await _bus.Connection.ReleaseNameAsync(GnomeShellIntrospectServer.ServiceName);
        if (!introspectReleased)
        {
            _logger.LogWarning("DBus name was not released because it is no longer owned: {ServiceName}", GnomeShellIntrospectServer.ServiceName);
        }
        else
        {
            _logger.LogInformation("Released DBus name: {ServiceName}", GnomeShellIntrospectServer.ServiceName);
        }

        var screenSaverReleased = await _bus.Connection.ReleaseNameAsync(GnomeScreenSaverServer.ServiceName);
        if (!screenSaverReleased)
        {
            _logger.LogWarning("DBus name was not released because it is no longer owned: {ServiceName}", GnomeScreenSaverServer.ServiceName);
        }
        else
        {
            _logger.LogInformation("Released DBus name: {ServiceName}", GnomeScreenSaverServer.ServiceName);
        }

        var serviceChannelReleased = await _bus.Connection.ReleaseNameAsync(GnomeMutterServiceChannelServer.ServiceName);
        if (!serviceChannelReleased)
        {
            _logger.LogWarning("DBus name was not released because it is no longer owned: {ServiceName}", GnomeMutterServiceChannelServer.ServiceName);
        }
        else
        {
            _logger.LogInformation("Released DBus name: {ServiceName}", GnomeMutterServiceChannelServer.ServiceName);
        }

        var displayConfigReleased = await _bus.Connection.ReleaseNameAsync(GnomeMutterDisplayConfigServer.ServiceName);
        if (!displayConfigReleased)
        {
            _logger.LogWarning("DBus name was not released because it is no longer owned: {ServiceName}", GnomeMutterDisplayConfigServer.ServiceName);
        }
        else
        {
            _logger.LogInformation("Released DBus name: {ServiceName}", GnomeMutterDisplayConfigServer.ServiceName);
        }

        var brightnessReleased = await _bus.Connection.ReleaseNameAsync(GnomeShellBrightnessServer.ServiceName);
        if (!brightnessReleased)
        {
            _logger.LogWarning("DBus name was not released because it is no longer owned: {ServiceName}", GnomeShellBrightnessServer.ServiceName);
        }
        else
        {
            _logger.LogInformation("Released DBus name: {ServiceName}", GnomeShellBrightnessServer.ServiceName);
        }

        var released = await _bus.Connection.ReleaseNameAsync(GnomeShellServer.ServiceName);
        if (!released)
        {
            _logger.LogWarning("DBus name was not released because it is no longer owned: {ServiceName}", GnomeShellServer.ServiceName);
            return;
        }

        _logger.LogInformation("Released DBus name: {ServiceName}", GnomeShellServer.ServiceName);
    }

    void OnShellReadyChanged(bool ready)
    {
        if (!_started)
            return;

        using var writer = _bus.Connection.GetMessageWriter();
        writer.WriteSignalHeader(
            destination: null,
            path: GnomeShellServer.ObjectPath,
            @interface: "org.freedesktop.DBus.Properties",
            member: "PropertiesChanged",
            signature: "sa{sv}as");

        writer.WriteString(GnomeShellServer.InterfaceName);
        writer.WriteDictionary([
            new KeyValuePair<string, VariantValue>("ShellReady", VariantValue.Bool(ready))
        ]);
        writer.WriteArray(Array.Empty<string>());

        var sent = _bus.TrySendMessage(writer.CreateMessage());
        if (!sent)
        {
            _logger.LogWarning("Failed to emit PropertiesChanged for ShellReady={ShellReady}.", ready);
            return;
        }

        _logger.LogInformation("Emitted PropertiesChanged: ShellReady={ShellReady}", ready);
    }

    void OnWindowsChanged()
    {
        if (!_started)
            return;

        _introspectServer.EmitWindowsChanged();
        _introspectServer.EmitRunningApplicationsChanged();
    }

    void OnDisplayConfigurationChanged()
    {
        if (!_started)
            return;

        _displayConfigServer.EmitMonitorsChanged();
    }
}
