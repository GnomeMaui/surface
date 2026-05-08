using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GnomeSurfaceShell.Services;
using Microsoft.Extensions.Logging;
using Tmds.DBus.Protocol;

namespace GnomeSurfaceShell.DBus;

sealed class GnomeShellIntrospectServer : IPathMethodHandler
{
    public const string ServiceName = "org.gnome.Shell.Introspect";
    public const string InterfaceName = "org.gnome.Shell.Introspect";
    public const string ObjectPath = "/org/gnome/Shell/Introspect";

    const uint ApiVersion = 3;

    static readonly ReadOnlyMemory<byte> IntrospectionXml = """
<node>
  <interface name="org.gnome.Shell.Introspect">
    <signal name="RunningApplicationsChanged"/>
    <signal name="WindowsChanged"/>
    <method name="GetRunningApplications">
      <arg name="apps" direction="out" type="a{sa{sv}}"/>
    </method>
    <method name="GetWindows">
      <arg name="windows" direction="out" type="a{ta{sv}}"/>
    </method>
    <property name="AnimationsEnabled" type="b" access="read"/>
    <property name="ScreenSize" type="(ii)" access="read"/>
    <property name="version" type="u" access="read"/>
  </interface>
</node>
"""u8.ToArray();

    readonly SessionBusConnection _bus;
    readonly ShellRuntimeState _runtimeState;
    readonly ILogger<GnomeShellIntrospectServer> _logger;

    public GnomeShellIntrospectServer(
        SessionBusConnection bus,
        ShellRuntimeState runtimeState,
        ILogger<GnomeShellIntrospectServer> logger)
    {
        _bus = bus;
        _runtimeState = runtimeState;
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

        switch (request.MemberAsString)
        {
            case "GetRunningApplications":
                ReplyRunningApplications(context);
                break;
            case "GetWindows":
                ReplyWindows(context);
                break;
            default:
                _logger.LogInformation("DBus call: Introspect.{Member} is not supported.", request.MemberAsString);
                context.ReplyUnknownMethodError();
                break;
        }

        return ValueTask.CompletedTask;
    }

    public void EmitWindowsChanged()
    {
        using var writer = _bus.Connection.GetMessageWriter();
        writer.WriteSignalHeader(
            destination: null,
            path: ObjectPath,
            @interface: InterfaceName,
            member: "WindowsChanged");

        var sent = _bus.TrySendMessage(writer.CreateMessage());
        if (!sent)
        {
            _logger.LogWarning("Failed to emit Introspect.WindowsChanged.");
            return;
        }

        _logger.LogInformation("Emitted Introspect.WindowsChanged.");
    }

    public void EmitRunningApplicationsChanged()
    {
        using var writer = _bus.Connection.GetMessageWriter();
        writer.WriteSignalHeader(
            destination: null,
            path: ObjectPath,
            @interface: InterfaceName,
            member: "RunningApplicationsChanged");

        var sent = _bus.TrySendMessage(writer.CreateMessage());
        if (!sent)
        {
            _logger.LogWarning("Failed to emit Introspect.RunningApplicationsChanged.");
            return;
        }

        _logger.LogInformation("Emitted Introspect.RunningApplicationsChanged.");
    }

    void ReplyRunningApplications(MethodContext context)
    {
        var apps = BuildRunningApplications();
        _logger.LogInformation("DBus call: Introspect.GetRunningApplications => {Count} apps.", apps.Count);

        var writer = context.CreateReplyWriter("a{sa{sv}}");
        try
        {
            writer.WriteDictionaryOfStringToVariantDictionary(apps);
            context.Reply(writer.CreateMessage());
        }
        finally
        {
            writer.Dispose();
        }
    }

    void ReplyWindows(MethodContext context)
    {
        var windows = BuildWindows();
        _logger.LogInformation("DBus call: Introspect.GetWindows => {Count} windows.", windows.Count);

        var writer = context.CreateReplyWriter("a{ta{sv}}");
        try
        {
            writer.WriteDictionaryOfUInt64ToVariantDictionary(windows);
            context.Reply(writer.CreateMessage());
        }
        finally
        {
            writer.Dispose();
        }
    }

    Dictionary<string, Dictionary<string, VariantValue>> BuildRunningApplications()
    {
        var apps = new Dictionary<string, Dictionary<string, VariantValue>>(StringComparer.Ordinal);
        foreach (var window in _runtimeState.GetWindows())
        {
            if (!apps.TryGetValue(window.AppId, out var appInfo))
            {
                appInfo = [];
                apps[window.AppId] = appInfo;
            }

            if (window.HasFocus)
                appInfo["active-on-seats"] = VariantValue.Array(new[] { "seat0" });

            if (!string.IsNullOrWhiteSpace(window.SandboxedAppId))
                appInfo["sandboxed-app-id"] = VariantValue.String(window.SandboxedAppId);
        }

        return apps;
    }

    Dictionary<ulong, Dictionary<string, VariantValue>> BuildWindows()
    {
        var windows = new Dictionary<ulong, Dictionary<string, VariantValue>>();
        foreach (var window in _runtimeState.GetWindows())
        {
            var properties = new Dictionary<string, VariantValue>(StringComparer.Ordinal)
            {
                ["app-id"] = VariantValue.String(window.AppId),
                ["client-type"] = VariantValue.UInt32(window.ClientType),
                ["is-hidden"] = VariantValue.Bool(window.IsHidden),
                ["has-focus"] = VariantValue.Bool(window.HasFocus),
                ["width"] = VariantValue.UInt32(window.Width),
                ["height"] = VariantValue.UInt32(window.Height)
            };

            if (!string.IsNullOrWhiteSpace(window.Title))
                properties["title"] = VariantValue.String(window.Title);

            if (!string.IsNullOrWhiteSpace(window.WmClass))
                properties["wm-class"] = VariantValue.String(window.WmClass);

            if (!string.IsNullOrWhiteSpace(window.SandboxedAppId))
                properties["sandboxed-app-id"] = VariantValue.String(window.SandboxedAppId);

            windows[window.Id] = properties;
        }

        return windows;
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
                context.ReplyError("org.freedesktop.DBus.Error.InvalidArgs", "Introspect properties are read-only.");
                return;
            default:
                context.ReplyUnknownMethodError();
                return;
        }
    }

    void ReplyProperty(MethodContext context, string propertyName)
    {
        switch (propertyName)
        {
            case "AnimationsEnabled":
                using (var writer = context.CreateReplyWriter("v"))
                {
                    writer.WriteVariantBool(true);
                    context.Reply(writer.CreateMessage());
                }
                break;
            case "ScreenSize":
                using (var writer = context.CreateReplyWriter("v"))
                {
                    writer.WriteVariant(GetScreenSizeVariant());
                    context.Reply(writer.CreateMessage());
                }
                break;
            case "version":
                using (var writer = context.CreateReplyWriter("v"))
                {
                    writer.WriteVariantUInt32(ApiVersion);
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
        new("AnimationsEnabled", VariantValue.Bool(true)),
        new("ScreenSize", GetScreenSizeVariant()),
        new("version", VariantValue.UInt32(ApiVersion))
    ];

    VariantValue GetScreenSizeVariant()
    {
        var display = _runtimeState.GetDisplayConfiguration();
        var width = 0;
        var height = 0;
        foreach (var monitor in display.Monitors)
        {
            width = Math.Max(width, monitor.X + monitor.Width);
            height = Math.Max(height, monitor.Y + monitor.Height);
        }

        if (width <= 0)
            width = DisplayMonitorState.Fallback.Width;

        if (height <= 0)
            height = DisplayMonitorState.Fallback.Height;

        return VariantValue.Struct(VariantValue.Int32(width), VariantValue.Int32(height));
    }
}

file static class IntrospectMessageWriterExtensions
{
    public static void WriteDictionaryOfStringToVariantDictionary(
        this ref MessageWriter writer,
        Dictionary<string, Dictionary<string, VariantValue>> value)
    {
        var arrayStart = writer.WriteDictionaryStart();
        foreach (var item in value)
        {
            writer.WriteDictionaryEntryStart();
            writer.WriteString(item.Key);
            writer.WriteDictionary(item.Value);
        }

        writer.WriteDictionaryEnd(arrayStart);
    }

    public static void WriteDictionaryOfUInt64ToVariantDictionary(
        this ref MessageWriter writer,
        Dictionary<ulong, Dictionary<string, VariantValue>> value)
    {
        var arrayStart = writer.WriteDictionaryStart();
        foreach (var item in value)
        {
            writer.WriteDictionaryEntryStart();
            writer.WriteUInt64(item.Key);
            writer.WriteDictionary(item.Value);
        }

        writer.WriteDictionaryEnd(arrayStart);
    }
}
