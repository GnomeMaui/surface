using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tmds.DBus.Protocol;

namespace GnomeSurfaceSession.Services;

/// <summary>
/// Singleton hosted service that owns the system DBus connection.
/// </summary>
sealed class SystemBusConnection : IHostedService, IAsyncDisposable
{
    DBusConnection? _connection;
    readonly ILogger<SystemBusConnection> _logger;

    public SystemBusConnection(ILogger<SystemBusConnection> logger)
    {
        _logger = logger;
    }

    public DBusConnection Connection =>
        _connection ?? throw new InvalidOperationException("System bus connection is not yet established.");

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting system bus connection.");
        var address = DBusAddress.System
            ?? throw new InvalidOperationException("System bus address is not available.");

        _connection = new DBusConnection(address);
        await _connection.ConnectAsync();
        _logger.LogInformation("System bus connected.");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping system bus connection.");
        await DisposeAsyncCore();
    }

    async ValueTask DisposeAsyncCore()
    {
        if (_connection is not null)
        {
            _logger.LogDebug("Disposing system bus connection.");
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
