using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using GLib;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GnomeSurfaceSession.Services;

enum GsmRestoreReason : uint
{
    Pristine = 0,
    Launch = 1,
    Recover = 2,
    Restore = 3,
}

sealed class GsmSessionRestoreStore
{
    const int InstanceIdLen = 10;
    const uint SaveVersion = 1;

    static readonly VariantType VardictType = VariantType.New("a{sv}");
    static readonly VariantType StringType = VariantType.New("s");
    static readonly VariantType StringArrayType = VariantType.New("as");
    static readonly VariantType UInt32Type = VariantType.New("u");
    static readonly VariantType BooleanType = VariantType.New("b");
    static readonly VariantType AppInstancesMapType = VariantType.New("a{s(a{sv}aa{sv})}");

    readonly object _gate = new();
    readonly string _sessionId;
    readonly string _filePath;
    readonly ILogger<GsmSessionRestoreStore> _logger;
    readonly Dictionary<string, AppState> _apps = new(StringComparer.Ordinal);

    public GsmSessionRestoreStore(IConfiguration configuration, ILogger<GsmSessionRestoreStore> logger)
    {
        _logger = logger;
        _sessionId = (configuration["session"] ?? "gnome").Trim();
        if (string.IsNullOrWhiteSpace(_sessionId))
            _sessionId = "gnome";

        var stateHome = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        if (string.IsNullOrWhiteSpace(stateHome))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            stateHome = Path.Combine(home, ".local", "state");
        }

        _filePath = Path.Combine(stateHome, $"gnome-session@{_sessionId}.state");
        LoadFromDisk();
        _logger.LogInformation("Session restore store initialized. path={Path}", _filePath);
    }

    public (uint Reason, string InstanceId, string[] CleanupIds) RegisterRestore(string appId, string dbusName)
    {
        lock (_gate)
        {
            if (!_apps.TryGetValue(appId, out var app))
            {
                app = new AppState();
                _apps[appId] = app;
            }

            var instance = app.Instances.FirstOrDefault(static i => string.IsNullOrEmpty(i.DBusName));
            GsmRestoreReason reason;
            if (instance is null)
            {
                instance = new InstanceState { InstanceId = NextInstanceId(appId, app) };
                app.Instances.Add(instance);
                reason = GsmRestoreReason.Launch;
            }
            else if (instance.Crashed)
            {
                instance.Crashed = false;
                reason = GsmRestoreReason.Recover;
            }
            else
            {
                reason = GsmRestoreReason.Restore;
            }

            instance.DBusName = dbusName;
            var cleanupIds = app.DiscardedIds.ToArray();
            FlushToDisk();
            return ((uint)reason, instance.InstanceId, cleanupIds);
        }
    }

    public void DeletedInstanceIds(string appId, IReadOnlyList<string> ids)
    {
        lock (_gate)
        {
            if (!_apps.TryGetValue(appId, out var app))
                return;

            foreach (var id in ids)
                app.DiscardedIds.Remove(id);

            FlushToDisk();
        }
    }

    public bool UnregisterRestore(string appId, string instanceId)
    {
        lock (_gate)
        {
            if (!_apps.TryGetValue(appId, out var app))
                return false;

            var instance = app.Instances.FirstOrDefault(i => string.Equals(i.InstanceId, instanceId, StringComparison.Ordinal));
            if (instance is null)
                return false;

            if (!string.IsNullOrEmpty(app.NextId))
                app.DiscardedIds.Add(app.NextId);

            app.NextId = instance.InstanceId;
            app.Instances.Remove(instance);
            FlushToDisk();
            return true;
        }
    }

    string NextInstanceId(string appId, AppState app)
    {
        if (!string.IsNullOrEmpty(app.NextId))
        {
            var next = app.NextId;
            app.NextId = null;
            return next;
        }

        while (true)
        {
            var random = RandomNumberGenerator.GetInt32(int.MinValue, int.MaxValue);
            var raw = $"gnome-session{_sessionId}{appId}{random}";
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
            var hash = Convert.ToHexString(bytes).ToLowerInvariant();
            var candidate = hash.Substring(0, InstanceIdLen);

            if (app.Instances.All(i => !string.Equals(i.InstanceId, candidate, StringComparison.Ordinal)))
                return candidate;
        }
    }

    void LoadFromDisk()
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            if (!File.Exists(_filePath))
                return;

            var rawBytes = File.ReadAllBytes(_filePath);
            if (rawBytes.Length == 0)
                return;

            using var bytes = Bytes.New(rawBytes);
            using var root = Variant.NewFromBytes(VardictType, bytes, trusted: false);
            using var dict = VariantDict.New(root);

            using var versionVariant = dict.LookupValue("version", UInt32Type);
            if (versionVariant is null)
                throw new InvalidDataException("Missing 'version' field.");

            var version = versionVariant.GetUint32();
            if (version != SaveVersion)
            {
                _logger.LogWarning("Unsupported restore save version {Version}, discarding existing state.", version);
                return;
            }

            using var dirtyVariant = dict.LookupValue("dirty", BooleanType);
            if (dirtyVariant is null)
                throw new InvalidDataException("Missing 'dirty' field.");

            var dirty = dirtyVariant.GetBoolean();

            using var instancesVariant = dict.LookupValue("instances", AppInstancesMapType);
            if (instancesVariant is null)
                throw new InvalidDataException("Missing 'instances' field.");

            _apps.Clear();

            var appsIter = instancesVariant.IterNew();
            Variant? appEntry;
            while ((appEntry = appsIter.NextValue()) is not null)
            {
                using (appEntry)
                {
                    using var appIdVariant = appEntry.GetChildValue(0);
                    using var appPayloadVariant = appEntry.GetChildValue(1);

                    var appId = appIdVariant.GetString(out _);
                    var app = new AppState();

                    using (var appPropsVariant = appPayloadVariant.GetChildValue(0))
                    using (var appProps = VariantDict.New(appPropsVariant))
                    {
                        using var nextIdVariant = appProps.LookupValue("next-instance-id", StringType);
                        if (nextIdVariant is not null)
                            app.NextId = nextIdVariant.GetString(out _);

                        using var discardedIdsVariant = appProps.LookupValue("discarded-instance-ids", StringArrayType);
                        if (discardedIdsVariant is not null)
                        {
                            var discardedIter = discardedIdsVariant.IterNew();
                            Variant? discardedId;
                            while ((discardedId = discardedIter.NextValue()) is not null)
                            {
                                using (discardedId)
                                    app.DiscardedIds.Add(discardedId.GetString(out _));
                            }
                        }
                    }

                    using (var runningInstances = appPayloadVariant.GetChildValue(1))
                    {
                        var runningIter = runningInstances.IterNew();
                        Variant? instanceEntry;
                        while ((instanceEntry = runningIter.NextValue()) is not null)
                        {
                            using (instanceEntry)
                            using (var instanceDict = VariantDict.New(instanceEntry))
                            {
                                using var instanceIdVariant = instanceDict.LookupValue("instance-id", StringType);
                                if (instanceIdVariant is null)
                                {
                                    _logger.LogWarning("Dropping malformed restore instance for app {AppId}: missing instance-id.", appId);
                                    continue;
                                }

                                var crashed = false;
                                using var crashedVariant = instanceDict.LookupValue("crashed", BooleanType);
                                if (crashedVariant is not null)
                                    crashed = crashedVariant.GetBoolean();

                                app.Instances.Add(new InstanceState
                                {
                                    InstanceId = instanceIdVariant.GetString(out _),
                                    DBusName = null,
                                    Crashed = crashed || dirty,
                                });
                            }
                        }
                    }

                    _apps[appId] = app;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load restore store from disk. Starting with empty state.");
            _apps.Clear();
        }
    }

    void FlushToDisk()
    {
        using var instancesArray = BuildInstancesVariant();

        using var rootBuilder = VariantBuilder.New(VardictType);
        rootBuilder.AddValue(NewVariantDictEntry("version", Variant.NewUint32(SaveVersion)));
        rootBuilder.AddValue(NewVariantDictEntry("dirty", Variant.NewBoolean(false)));
        rootBuilder.AddValue(NewVariantDictEntry("instances", instancesArray));

        using var root = rootBuilder.End();
        using var bytes = root.GetDataAsBytes();

        var size = checked((int)bytes.GetSize());
        var data = bytes.GetRegionSpan<byte>(0, (nuint)size).ToArray();
        File.WriteAllBytes(_filePath, data);
    }

    Variant BuildInstancesVariant()
    {
        var appEntries = new List<Variant>(_apps.Count);

        try
        {
            foreach (var (appId, appState) in _apps)
            {
                using var appProps = BuildAppPropertiesVariant(appState);
                using var runningInstances = BuildRunningInstancesVariant(appState);
                using var appTuple = Variant.NewTuple([appProps, runningInstances]);

                appEntries.Add(Variant.NewDictEntry(
                    Variant.NewString(appId),
                    appTuple));
            }

            return Variant.NewArray(VariantType.New("{s(a{sv}aa{sv})}"), [.. appEntries]);
        }
        finally
        {
            foreach (var entry in appEntries)
                entry.Dispose();
        }
    }

    static Variant BuildAppPropertiesVariant(AppState appState)
    {
        var props = new List<Variant>(2);

        try
        {
            if (!string.IsNullOrEmpty(appState.NextId))
                props.Add(NewVariantDictEntry("next-instance-id", Variant.NewString(appState.NextId)));

            if (appState.DiscardedIds.Count > 0)
            {
                var discarded = appState.DiscardedIds.Select(Variant.NewString).ToArray();
                try
                {
                    var discardedArray = Variant.NewArray(StringType, discarded);
                    props.Add(NewVariantDictEntry("discarded-instance-ids", discardedArray));
                }
                finally
                {
                    foreach (var item in discarded)
                        item.Dispose();
                }
            }

            return Variant.NewArray(VariantType.New("{sv}"), [.. props]);
        }
        finally
        {
            foreach (var prop in props)
                prop.Dispose();
        }
    }

    static Variant BuildRunningInstancesVariant(AppState appState)
    {
        var instances = new List<Variant>(appState.Instances.Count);

        try
        {
            foreach (var instance in appState.Instances)
            {
                var instanceEntries = new Variant[]
                {
                    NewVariantDictEntry("instance-id", Variant.NewString(instance.InstanceId)),
                    NewVariantDictEntry("crashed", Variant.NewBoolean(instance.Crashed)),
                };

                try
                {
                    instances.Add(Variant.NewArray(VariantType.New("{sv}"), instanceEntries));
                }
                finally
                {
                    foreach (var entry in instanceEntries)
                        entry.Dispose();
                }
            }

            return Variant.NewArray(VardictType, [.. instances]);
        }
        finally
        {
            foreach (var instance in instances)
                instance.Dispose();
        }
    }

    static Variant NewVariantDictEntry(string key, Variant value)
    {
        using (value)
        {
            return Variant.NewDictEntry(
                Variant.NewString(key),
                Variant.NewVariant(value));
        }
    }

    sealed class AppState
    {
        public string? NextId { get; set; }

        public List<string> DiscardedIds { get; set; } = new();

        public List<InstanceState> Instances { get; set; } = new();
    }

    sealed class InstanceState
    {
        public required string InstanceId { get; set; }

        public string? DBusName { get; set; }

        public bool Crashed { get; set; }
    }
}
