using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tmds.DBus.Protocol;

namespace GnomeSurfaceShell.Services;

/// <summary>
/// Singleton hosted service that owns the session DBus connection.
/// </summary>
sealed class SessionBusConnection : IHostedService, IAsyncDisposable
{
    readonly object _sendLock = new();
    DBusConnection? _connection;
    readonly ILogger<SessionBusConnection> _logger;

    public SessionBusConnection(ILogger<SessionBusConnection> logger)
    {
        _logger = logger;
    }

    public DBusConnection Connection =>
        _connection ?? throw new InvalidOperationException("Session bus connection is not established yet.");

    public bool TrySendMessage(MessageBuffer message)
    {
        lock (_sendLock)
        {
            return Connection.TrySendMessage(message);
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting session bus connection.");

        var address = DBusAddress.Session
            ?? throw new InvalidOperationException("Session bus address is unavailable (DBUS_SESSION_BUS_ADDRESS is not set).");

        _connection = new DBusConnection(address);
        await _connection.ConnectAsync();

        _logger.LogInformation("Session bus connected.");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping session bus connection.");
        await DisposeAsyncCore();
    }

    async ValueTask DisposeAsyncCore()
    {
        _connection?.Dispose();
        _connection = null;

        await Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore();
        GC.SuppressFinalize(this);
    }
}
