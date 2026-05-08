using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace GnomeSurfaceSession.Services;

/// <summary>
/// Launches autostart desktop entries discovered by session fill.
/// </summary>
sealed class GsmAutostartService
{
    readonly GsmPhaseManager _phaseManager;
    readonly ILogger<GsmAutostartService> _logger;

    public GsmAutostartService(GsmPhaseManager phaseManager, ILogger<GsmAutostartService> logger)
    {
        _phaseManager = phaseManager;
        _logger = logger;
    }

    public async Task RunApplicationAutostartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Autostart phase started. session={SessionName}", _phaseManager.SessionName);
        if (_phaseManager.IsKioskSession)
        {
            _logger.LogInformation("Skipping autostart launch for kiosk session.");
            return;
        }

        var desktopFiles = DiscoverDesktopFiles(_phaseManager.AutostartDirectories);
        _logger.LogInformation("Autostart desktop files discovered: {Count}", desktopFiles.Length);
        foreach (var desktopFile in desktopFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogInformation("Launching autostart desktop file: {DesktopFile}", desktopFile);

            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "gio",
                        ArgumentList = { "launch", desktopFile },
                        RedirectStandardError = true,
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    }
                };

                if (!process.Start())
                {
                    _logger.LogWarning("Failed to start autostart entry: {DesktopFile}", desktopFile);
                    continue;
                }

                await process.WaitForExitAsync(cancellationToken);
                if (process.ExitCode != 0)
                {
                    _logger.LogWarning("Autostart launch returned non-zero exit code {ExitCode}: {DesktopFile}", process.ExitCode, desktopFile);
                }
                else
                {
                    _logger.LogInformation("Autostart launch succeeded: {DesktopFile}", desktopFile);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Autostart launch failed: {DesktopFile}", desktopFile);
            }
        }

        _logger.LogInformation("Autostart phase finished.");
    }

    static string[] DiscoverDesktopFiles(IReadOnlyList<string> directories)
    {
        var byBaseName = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var dir in directories)
        {
            if (!Directory.Exists(dir))
                continue;

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(dir, "*.desktop", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                var baseName = Path.GetFileName(file);
                if (baseName is null)
                    continue;

                // First occurrence wins to preserve directory priority order.
                byBaseName.TryAdd(baseName, file);
            }
        }

        return byBaseName.Values.OrderBy(static p => p, StringComparer.Ordinal).ToArray();
    }
}
