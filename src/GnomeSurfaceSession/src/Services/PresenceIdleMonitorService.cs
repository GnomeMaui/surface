using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GnomeSurfaceSession.Services;

/// <summary>
/// Polls logind IdleHint and maps it to Presence idle status updates.
/// </summary>
sealed class PresenceIdleMonitorService : BackgroundService
{
    static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    readonly GsmPresenceService _presence;
    readonly ILogger<PresenceIdleMonitorService> _logger;

    public PresenceIdleMonitorService(GsmPresenceService presence, ILogger<PresenceIdleMonitorService> logger)
    {
        _presence = presence;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PresenceIdleMonitorService started. pollInterval={PollIntervalMs}ms", PollInterval.TotalMilliseconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (TryReadIdleHint(out var isIdle))
                {
                    _logger.LogDebug("IdleHint poll succeeded. idle={IsIdle}", isIdle);
                    _presence.SetIdle(isIdle);
                }
                else
                {
                    _logger.LogDebug("IdleHint poll returned no value.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "IdleHint poll failed.");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("PresenceIdleMonitorService stopped.");
    }

    static bool TryReadIdleHint(out bool isIdle)
    {
        isIdle = false;

        var sessionId = Environment.GetEnvironmentVariable("XDG_SESSION_ID");
        if (string.IsNullOrWhiteSpace(sessionId))
            return false;

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "loginctl",
                ArgumentList = { "show-session", sessionId, "--property=IdleHint", "--value" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };

        if (!process.Start())
            return false;

        if (!process.WaitForExit(1500) || process.ExitCode != 0)
            return false;

        var output = process.StandardOutput.ReadToEnd().Trim();
        if (output.Length == 0)
            return false;

        isIdle = string.Equals(output, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(output, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(output, "1", StringComparison.Ordinal);
        return true;
    }
}
