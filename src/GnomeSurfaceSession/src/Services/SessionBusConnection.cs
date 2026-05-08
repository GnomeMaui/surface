using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tmds.DBus.Protocol;

namespace GnomeSurfaceSession.Services;

/// <summary>
/// Singleton hosted service that owns the session DBus connection.
/// Started before any other hosted service; safe to use from ExecuteAsync of BackgroundService instances.
/// </summary>
sealed class SessionBusConnection : IHostedService, IAsyncDisposable
{
    DBusConnection? _connection;
    readonly ILogger<SessionBusConnection> _logger;

    public SessionBusConnection(ILogger<SessionBusConnection> logger)
    {
        _logger = logger;
    }

    public DBusConnection Connection =>
        _connection ?? throw new InvalidOperationException("Session bus connection is not yet established.");

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting session bus connection.");
        var address = DBusAddress.Session
            ?? throw new InvalidOperationException("Session bus address is not available (DBUS_SESSION_BUS_ADDRESS not set?).");

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
        if (_connection is not null)
        {
            _logger.LogDebug("Disposing session bus connection.");
            _connection?.Dispose();
            _connection = null;
            await Task.CompletedTask;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore();
        GC.SuppressFinalize(this);
    }
}
