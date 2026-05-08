using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using GnomeSurfaceShell.Services;
using Microsoft.Extensions.Logging;
using Tmds.DBus.Protocol;

namespace GnomeSurfaceShell.DBus;

/// <summary>
/// Minimal DBus stub for org.gnome.Mutter.DisplayConfig.
/// It currently only guarantees name ownership and PowerSaveMode property handling.
/// </summary>
sealed class GnomeMutterDisplayConfigServer : IPathMethodHandler
{
    public const string ServiceName = "org.gnome.Mutter.DisplayConfig";
    public const string InterfaceName = "org.gnome.Mutter.DisplayConfig";
    public const string ObjectPath = "/org/gnome/Mutter/DisplayConfig";

    static readonly ReadOnlyMemory<byte> IntrospectionXml = """
<node>
  <interface name="org.gnome.Mutter.DisplayConfig">
    <method name="GetResources">
      <arg name="serial" direction="out" type="u"/>
      <arg name="crtcs" direction="out" type="a(uxiiiiiuaua{sv})"/>
      <arg name="outputs" direction="out" type="a(uxiausauaua{sv})"/>
      <arg name="modes" direction="out" type="a(uxuudu)"/>
      <arg name="max_screen_width" direction="out" type="i"/>
      <arg name="max_screen_height" direction="out" type="i"/>
    </method>
    <method name="GetCurrentState">
      <arg name="serial" direction="out" type="u"/>
      <arg name="monitors" direction="out" type="a((ssss)a(siiddada{sv})a{sv})"/>
      <arg name="logical_monitors" direction="out" type="a(iiduba(ssss)a{sv})"/>
      <arg name="properties" direction="out" type="a{sv}"/>
    </method>
    <method name="ApplyMonitorsConfig">
      <arg name="serial" direction="in" type="u"/>
      <arg name="method" direction="in" type="u"/>
      <arg name="logical_monitors" direction="in" type="a(iiduba(ssa{sv}))"/>
      <arg name="properties" direction="in" type="a{sv}"/>
    </method>
    <signal name="MonitorsChanged"/>
    <method name="SetBacklight">
      <arg name="serial" direction="in" type="u"/>
      <arg name="connector" direction="in" type="s"/>
      <arg name="value" direction="in" type="i"/>
    </method>
    <property name="PowerSaveMode" type="i" access="readwrite"/>
    <property name="PanelOrientationManaged" type="b" access="read"/>
    <property name="ApplyMonitorsConfigAllowed" type="b" access="read"/>
    <property name="NightLightSupported" type="b" access="read"/>
    <property name="HasExternalMonitor" type="b" access="read"/>
  </interface>
</node>
"""u8.ToArray();

    const uint TransformNormal = 0;
    const uint LayoutModeLogical = 1;

    readonly ShellRuntimeState _runtimeState;
    readonly SessionBusConnection _bus;
    readonly ILogger<GnomeMutterDisplayConfigServer> _logger;
    int _powerSaveMode;

    public GnomeMutterDisplayConfigServer(
        ShellRuntimeState runtimeState,
        SessionBusConnection bus,
        ILogger<GnomeMutterDisplayConfigServer> logger)
    {
        _runtimeState = runtimeState;
        _bus = bus;
        _logger = logger;
    }

    public string Path => ObjectPath;

    public bool HandlesChildPaths => false;

    public ValueTask HandleMethodAsync(MethodContext context)
    {
        var request = context.Request;

        if (context.IsDBusIntrospectRequest)
        {
            context.ReplyIntrospectXml([IntrospectionXml]);
            return ValueTask.CompletedTask;
        }

        if (request.PathAsString != ObjectPath)
        {
            context.ReplyUnknownMethodError();
            return ValueTask.CompletedTask;
        }

        if (context.IsPropertiesInterfaceRequest)
        {
            HandleProperties(context);
            return ValueTask.CompletedTask;
        }

        if (request.InterfaceAsString != InterfaceName)
        {
            context.ReplyUnknownMethodError();
            return ValueTask.CompletedTask;
        }

        var reader = request.GetBodyReader();

        switch (request.MemberAsString)
        {
            case "GetResources":
                _logger.LogInformation("DBus call: GetResources. Returning legacy empty resource snapshot.");
                ReplyGetResources(context);
                break;
            case "GetCurrentState":
                {
                    var snapshot = _runtimeState.GetDisplayConfiguration();
                    _logger.LogInformation(
                        "DBus call: GetCurrentState. serial={Serial} monitors={MonitorCount}",
                        snapshot.Serial,
                        snapshot.Monitors.Length);

                    ReplyGetCurrentState(context, snapshot);
                    break;
                }
            case "ApplyMonitorsConfig":
                ApplyMonitorsConfig(context, ref reader);
                break;
            case "SetBacklight":
                var backlightSerial = reader.ReadUInt32();
                var backlightConnector = reader.ReadString();
                var backlightValue = reader.ReadInt32();
                _logger.LogInformation(
                    "DBus call: SetBacklight(serial={Serial}, connector={Connector}, value={Value}) no-op.",
                    backlightSerial,
                    backlightConnector,
                    backlightValue);
                ReplyEmpty(context);
                break;
            default:
                context.ReplyError(
                    "org.freedesktop.DBus.Error.NotSupported",
                    $"org.gnome.Mutter.DisplayConfig.{request.MemberAsString} is not implemented yet.");
                break;
        }

        return ValueTask.CompletedTask;
    }

    void HandleProperties(MethodContext context)
    {
        var request = context.Request;
        var reader = request.GetBodyReader();

        switch (request.MemberAsString)
        {
            case "Get":
                {
                    var interfaceName = reader.ReadString();
                    var propertyName = reader.ReadString();
                    if (interfaceName != InterfaceName)
                    {
                        context.ReplyError("org.freedesktop.DBus.Error.InvalidArgs", $"Unsupported interface '{interfaceName}'.");
                        return;
                    }

                    ReplyProperty(context, propertyName);
                    return;
                }
            case "GetAll":
                {
                    var interfaceName = reader.ReadString();
                    if (interfaceName != InterfaceName)
                    {
                        context.ReplyError("org.freedesktop.DBus.Error.InvalidArgs", $"Unsupported interface '{interfaceName}'.");
                        return;
                    }

                    using var writer = context.CreateReplyWriter("a{sv}");
                    writer.WriteDictionary(GetAllProperties());
                    context.Reply(writer.CreateMessage());
                    return;
                }
            case "Set":
                {
                    var interfaceName = reader.ReadString();
                    var propertyName = reader.ReadString();
                    if (interfaceName != InterfaceName)
                    {
                        context.ReplyError("org.freedesktop.DBus.Error.InvalidArgs", $"Unsupported interface '{interfaceName}'.");
                        return;
                    }

                    if (propertyName != "PowerSaveMode")
                    {
                        context.ReplyError("org.freedesktop.DBus.Error.InvalidArgs", $"Property '{propertyName}' is not writable.");
                        return;
                    }

                    _powerSaveMode = reader.ReadVariantValue().GetInt32();
                    _logger.LogInformation("DBus property set: PowerSaveMode={PowerSaveMode}", _powerSaveMode);
                    ReplyEmpty(context);
                    return;
                }
            default:
                context.ReplyUnknownMethodError();
                return;
        }
    }

    void ReplyProperty(MethodContext context, string propertyName)
    {
        switch (propertyName)
        {
            case "PowerSaveMode":
                using (var writer = context.CreateReplyWriter("v"))
                {
                    writer.WriteVariantInt32(_powerSaveMode);
                    context.Reply(writer.CreateMessage());
                }
                break;
            case "PanelOrientationManaged":
            case "ApplyMonitorsConfigAllowed":
            case "NightLightSupported":
            case "HasExternalMonitor":
                using (var writer = context.CreateReplyWriter("v"))
                {
                    writer.WriteVariantBool(propertyName == "ApplyMonitorsConfigAllowed");
                    context.Reply(writer.CreateMessage());
                }
                break;
            default:
                context.ReplyError("org.freedesktop.DBus.Error.InvalidArgs", $"Unknown property '{propertyName}'.");
                break;
        }
    }

    KeyValuePair<string, VariantValue>[] GetAllProperties() =>
    [
        new("PowerSaveMode", VariantValue.Int32(_powerSaveMode)),
        new("PanelOrientationManaged", VariantValue.Bool(false)),
        new("ApplyMonitorsConfigAllowed", VariantValue.Bool(true)),
        new("NightLightSupported", VariantValue.Bool(false)),
        new("HasExternalMonitor", VariantValue.Bool(false))
    ];

    void ApplyMonitorsConfig(MethodContext context, ref Reader reader)
    {
        var serial = reader.ReadUInt32();
        var method = reader.ReadUInt32();
        var requested = ReadRequestedLogicalMonitors(ref reader);
        _ = reader.ReadDictionaryOfStringToVariantValue();

        var current = _runtimeState.GetDisplayConfiguration();
        if (serial != current.Serial)
        {
            _logger.LogWarning(
                "DBus call: ApplyMonitorsConfig serial mismatch. requested={RequestedSerial} current={CurrentSerial}",
                serial,
                current.Serial);
        }

        var next = BuildAppliedMonitorState(current.Monitors, requested);
        var snapshot = _runtimeState.UpdateDisplayConfiguration(next);
        _logger.LogInformation(
            "DBus call: ApplyMonitorsConfig(method={Method}) applied. requested_logical_monitors={RequestedCount} active_monitors={MonitorCount} serial={Serial}",
            method,
            requested.Count,
            snapshot.Monitors.Length,
            snapshot.Serial);

        ReplyEmpty(context);
    }

    static List<RequestedLogicalMonitor> ReadRequestedLogicalMonitors(ref Reader reader)
    {
        var monitors = new List<RequestedLogicalMonitor>();
        var logicalEnd = reader.ReadArrayStart(DBusType.Struct);
        while (reader.HasNext(logicalEnd))
        {
            reader.AlignStruct();
            var x = reader.ReadInt32();
            var y = reader.ReadInt32();
            var scale = reader.ReadDouble();
            var transform = reader.ReadUInt32();
            var primary = reader.ReadBool();
            var specs = new List<RequestedMonitorSpec>();

            var monitorEnd = reader.ReadArrayStart(DBusType.Struct);
            while (reader.HasNext(monitorEnd))
            {
                reader.AlignStruct();
                var connector = reader.ReadString();
                var mode = reader.ReadString();
                _ = reader.ReadDictionaryOfStringToVariantValue();
                specs.Add(new RequestedMonitorSpec(connector, mode));
            }

            _ = reader.ReadDictionaryOfStringToVariantValue();
            monitors.Add(new RequestedLogicalMonitor(x, y, scale, transform, primary, specs));
        }

        return monitors;
    }

    static DisplayMonitorState[] BuildAppliedMonitorState(
        DisplayMonitorState[] current,
        IReadOnlyList<RequestedLogicalMonitor> requested)
    {
        var monitors = new List<DisplayMonitorState>();
        for (var logicalIndex = 0; logicalIndex < requested.Count; logicalIndex++)
        {
            var logical = requested[logicalIndex];
            for (var specIndex = 0; specIndex < logical.Monitors.Count; specIndex++)
            {
                var spec = logical.Monitors[specIndex];
                var existing = Array.Find(current, monitor => monitor.Connector == spec.Connector)
                    ?? DisplayMonitorState.Fallback with { Connector = spec.Connector };
                var (width, height, refreshRate) = ParseMode(spec.Mode, existing);

                monitors.Add(existing with
                {
                    Index = existing.Index,
                    X = logical.X,
                    Y = logical.Y,
                    Width = width,
                    Height = height,
                    Scale = logical.Scale > 0 ? logical.Scale : existing.Scale,
                    RefreshRate = refreshRate,
                    IsPrimary = logical.Primary && specIndex == 0
                });
            }
        }

        return monitors.Count == 0 ? [DisplayMonitorState.Fallback] : [.. monitors];
    }

    static (int Width, int Height, double RefreshRate) ParseMode(string mode, DisplayMonitorState fallback)
    {
        var width = fallback.Width;
        var height = fallback.Height;
        var refreshRate = fallback.RefreshRate;
        var atIndex = mode.IndexOf('@', StringComparison.Ordinal);
        var sizePart = atIndex >= 0 ? mode[..atIndex] : mode;
        var xIndex = sizePart.IndexOf('x', StringComparison.OrdinalIgnoreCase);
        if (xIndex > 0 &&
            int.TryParse(sizePart[..xIndex], out var parsedWidth) &&
            int.TryParse(sizePart[(xIndex + 1)..], out var parsedHeight))
        {
            width = parsedWidth;
            height = parsedHeight;
        }

        if (atIndex >= 0 &&
            double.TryParse(
                mode[(atIndex + 1)..],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsedRefreshRate))
        {
            refreshRate = parsedRefreshRate;
        }

        return (width, height, refreshRate);
    }

    public void EmitMonitorsChanged()
    {
        using var writer = _bus.Connection.GetMessageWriter();
        writer.WriteSignalHeader(
            destination: null,
            path: ObjectPath,
            @interface: InterfaceName,
            member: "MonitorsChanged");

        var sent = _bus.TrySendMessage(writer.CreateMessage());
        if (!sent)
        {
            _logger.LogWarning("Failed to emit DisplayConfig.MonitorsChanged.");
            return;
        }

        _logger.LogInformation("Emitted DisplayConfig.MonitorsChanged.");
    }

    static void ReplyGetResources(MethodContext context)
    {
        using var writer = context.CreateReplyWriter("ua(uxiiiiiuaua{sv})a(uxiausauaua{sv})a(uxuudu)ii");

        writer.WriteUInt32(1);

        var crtcsStart = writer.WriteArrayStart(DBusType.Struct);
        writer.WriteArrayEnd(crtcsStart);

        var outputsStart = writer.WriteArrayStart(DBusType.Struct);
        writer.WriteArrayEnd(outputsStart);

        var modesStart = writer.WriteArrayStart(DBusType.Struct);
        writer.WriteArrayEnd(modesStart);

        writer.WriteInt32(0);
        writer.WriteInt32(0);

        context.Reply(writer.CreateMessage());
    }

    void ReplyGetCurrentState(MethodContext context, DisplayConfigurationState snapshot)
    {
        var writer = context.CreateReplyWriter("ua((ssss)a(siiddada{sv})a{sv})a(iiduba(ssss)a{sv})a{sv}");
        try
        {
            writer.WriteUInt32(snapshot.Serial);
            WriteMonitors(ref writer, snapshot.Monitors);
            WriteLogicalMonitors(ref writer, snapshot.Monitors);
            writer.WriteDictionary(new KeyValuePair<string, VariantValue>[]
            {
                new("layout-mode", VariantValue.UInt32(LayoutModeLogical)),
                new("supports-changing-layout-mode", VariantValue.Bool(false)),
                new("global-scale-required", VariantValue.Bool(false))
            });

            context.Reply(writer.CreateMessage());
            _logger.LogInformation(
                "DBus reply: GetCurrentState completed. serial={Serial} monitors={MonitorCount} logical_monitors={LogicalMonitorCount}",
                snapshot.Serial,
                snapshot.Monitors.Length,
                snapshot.Monitors.Length);
        }
        finally
        {
            writer.Dispose();
        }
    }

    void WriteMonitors(ref MessageWriter writer, DisplayMonitorState[] monitors)
    {
        var arrayStart = writer.WriteArrayStart(DBusType.Struct);
        foreach (var monitor in monitors)
        {
            var modeId = GetModeId(monitor);

            _logger.LogInformation(
                "GetCurrentState monitor: connector={Connector} vendor={Vendor} product={Product} serial={Serial} geometry={X},{Y} {Width}x{Height} scale={Scale} refresh={RefreshRate} mode={ModeId}",
                monitor.Connector,
                monitor.Vendor,
                monitor.Product,
                monitor.Serial,
                monitor.X,
                monitor.Y,
                monitor.Width,
                monitor.Height,
                monitor.Scale,
                monitor.RefreshRate,
                modeId);

            writer.WriteStructureStart();
            WriteMonitorSpec(ref writer, monitor);
            WriteMonitorModes(ref writer, monitor, modeId);
            writer.WriteDictionary(new KeyValuePair<string, VariantValue>[]
            {
                new("display-name", VariantValue.String(monitor.Connector)),
                new("is-builtin", VariantValue.Bool(false)),
                new("is-for-lease", VariantValue.Bool(false)),
                new("color-mode", VariantValue.UInt32(0)),
                new("rgb-range", VariantValue.UInt32(0))
            });
        }

        writer.WriteArrayEnd(arrayStart);
    }

    void WriteMonitorModes(ref MessageWriter writer, DisplayMonitorState monitor, string modeId)
    {
        var arrayStart = writer.WriteArrayStart(DBusType.Struct);
        writer.WriteStructureStart();
        writer.WriteString(modeId);
        writer.WriteInt32(monitor.Width);
        writer.WriteInt32(monitor.Height);
        writer.WriteDouble(monitor.RefreshRate);
        writer.WriteDouble(monitor.Scale);
        writer.WriteArray(new[] { monitor.Scale });
        writer.WriteDictionary(new KeyValuePair<string, VariantValue>[]
        {
            new("is-current", VariantValue.Bool(true)),
            new("is-preferred", VariantValue.Bool(true))
        });
        writer.WriteArrayEnd(arrayStart);
    }

    void WriteLogicalMonitors(ref MessageWriter writer, DisplayMonitorState[] monitors)
    {
        var arrayStart = writer.WriteArrayStart(DBusType.Struct);
        foreach (var logicalMonitor in GroupLogicalMonitors(monitors))
        {
            writer.WriteStructureStart();
            writer.WriteInt32(logicalMonitor.X);
            writer.WriteInt32(logicalMonitor.Y);
            writer.WriteDouble(logicalMonitor.Scale);
            writer.WriteUInt32(TransformNormal);
            writer.WriteBool(logicalMonitor.IsPrimary);

            var monitorArrayStart = writer.WriteArrayStart(DBusType.Struct);
            foreach (var monitor in logicalMonitor.Monitors)
                WriteMonitorSpec(ref writer, monitor);
            writer.WriteArrayEnd(monitorArrayStart);

            writer.WriteDictionary(Array.Empty<KeyValuePair<string, VariantValue>>());

            _logger.LogInformation(
                "GetCurrentState logical monitor: connectors={Connectors} x={X} y={Y} scale={Scale} primary={Primary}",
                string.Join(",", Array.ConvertAll(logicalMonitor.Monitors, monitor => monitor.Connector)),
                logicalMonitor.X,
                logicalMonitor.Y,
                logicalMonitor.Scale,
                logicalMonitor.IsPrimary);
        }

        writer.WriteArrayEnd(arrayStart);
    }

    static LogicalMonitorState[] GroupLogicalMonitors(DisplayMonitorState[] monitors)
    {
        var groups = new List<LogicalMonitorState>();
        foreach (var monitor in monitors)
        {
            var index = groups.FindIndex(group =>
                group.Index == monitor.Index &&
                group.X == monitor.X &&
                group.Y == monitor.Y &&
                Math.Abs(group.Scale - monitor.Scale) < double.Epsilon);

            if (index < 0)
            {
                groups.Add(new LogicalMonitorState(
                    monitor.Index,
                    monitor.X,
                    monitor.Y,
                    monitor.Scale,
                    monitor.IsPrimary,
                    [monitor]));
                continue;
            }

            var group = groups[index];
            var groupedMonitors = new DisplayMonitorState[group.Monitors.Length + 1];
            Array.Copy(group.Monitors, groupedMonitors, group.Monitors.Length);
            groupedMonitors[^1] = monitor;
            groups[index] = group with
            {
                IsPrimary = group.IsPrimary || monitor.IsPrimary,
                Monitors = groupedMonitors
            };
        }

        return [.. groups];
    }

    static void WriteMonitorSpec(ref MessageWriter writer, DisplayMonitorState monitor)
    {
        writer.WriteStructureStart();
        writer.WriteString(monitor.Connector);
        writer.WriteString(monitor.Vendor);
        writer.WriteString(monitor.Product);
        writer.WriteString(monitor.Serial);
    }

    static string GetModeId(DisplayMonitorState monitor) =>
        $"{monitor.Width}x{monitor.Height}@{monitor.RefreshRate:0.###}";

    static void ReplyEmpty(MethodContext context)
    {
        using var writer = context.CreateReplyWriter(null);
        context.Reply(writer.CreateMessage());
    }

    sealed record RequestedLogicalMonitor(
        int X,
        int Y,
        double Scale,
        uint Transform,
        bool Primary,
        List<RequestedMonitorSpec> Monitors);

    sealed record RequestedMonitorSpec(string Connector, string Mode);

    sealed record LogicalMonitorState(
        int Index,
        int X,
        int Y,
        double Scale,
        bool IsPrimary,
        DisplayMonitorState[] Monitors);
}
