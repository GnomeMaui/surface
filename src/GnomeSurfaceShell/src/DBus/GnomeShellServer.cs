using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GnomeSurfaceShell.Services;
using Microsoft.Extensions.Logging;
using Tmds.DBus.Protocol;

namespace GnomeSurfaceShell.DBus;

/// <summary>
/// Minimal manual DBus method handler for org.gnome.Shell.
/// This is an MVP surface for early integration and can be extended incrementally.
/// </summary>
sealed class GnomeShellServer : IPathMethodHandler
{
    public const string ServiceName = "org.gnome.Shell";
    public const string InterfaceName = "org.gnome.Shell";
    public const string ObjectPath = "/org/gnome/Shell";

    static readonly ReadOnlyMemory<byte> IntrospectionXml = """
<node>
  <interface name="org.gnome.Shell">
    <method name="Eval">
      <arg type="s" direction="in" name="script"/>
      <arg type="b" direction="out" name="success"/>
      <arg type="s" direction="out" name="result"/>
    </method>
    <method name="FocusSearch"/>
    <method name="ShowOSD">
      <arg type="a{sv}" direction="in" name="params"/>
    </method>
    <method name="ShowMonitorLabels">
      <arg type="a{sv}" direction="in" name="params"/>
    </method>
    <method name="HideMonitorLabels"/>
    <method name="FocusApp">
      <arg type="s" direction="in" name="id"/>
    </method>
    <method name="ShowApplications"/>
        <method name="GrabAccelerator">
            <arg type="s" direction="in" name="accelerator"/>
            <arg type="u" direction="in" name="modeFlags"/>
            <arg type="u" direction="in" name="grabFlags"/>
            <arg type="u" direction="out" name="action"/>
        </method>
        <method name="GrabAccelerators">
            <arg type="a(suu)" direction="in" name="accelerators"/>
            <arg type="au" direction="out" name="actions"/>
        </method>
        <method name="UngrabAccelerator">
            <arg type="u" direction="in" name="action"/>
            <arg type="b" direction="out" name="success"/>
        </method>
        <method name="UngrabAccelerators">
            <arg type="au" direction="in" name="actions"/>
            <arg type="b" direction="out" name="success"/>
        </method>
    <method name="ScreenTransition"/>
    <property name="Mode" type="s" access="read"/>
    <property name="OverviewActive" type="b" access="readwrite"/>
    <property name="ShellVersion" type="s" access="read"/>
    <property name="ShellReady" type="b" access="read"/>
  </interface>
</node>
"""u8.ToArray();

    readonly ILogger<GnomeShellServer> _logger;
    readonly ShellRuntimeState _runtimeState;
    readonly Dictionary<uint, AcceleratorRequest> _accelerators = new();
    uint _nextActionId = 1;
    bool _overviewActive;

    public GnomeShellServer(
        ShellRuntimeState runtimeState,
        ILogger<GnomeShellServer> logger)
    {
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

        var reader = request.GetBodyReader();
        switch (request.MemberAsString)
        {
            case "Eval":
                {
                    _ = reader.ReadString();
                    ReplyEval(context, success: false, result: "Eval is disabled in GnomeSurfaceShell.");
                    break;
                }
            case "FocusSearch":
                {
                    _logger.LogInformation("DBus call: FocusSearch");
                    ReplyEmpty(context);
                    break;
                }
            case "ShowOSD":
                {
                    _ = reader.ReadDictionaryOfStringToVariantValue();
                    _logger.LogInformation("DBus call: ShowOSD");
                    ReplyEmpty(context);
                    break;
                }
            case "ShowMonitorLabels":
                {
                    _ = reader.ReadDictionaryOfStringToVariantValue();
                    _logger.LogInformation("DBus call: ShowMonitorLabels");
                    ReplyEmpty(context);
                    break;
                }
            case "HideMonitorLabels":
                {
                    _logger.LogInformation("DBus call: HideMonitorLabels");
                    ReplyEmpty(context);
                    break;
                }
            case "FocusApp":
                {
                    var appId = reader.ReadString();
                    _logger.LogInformation("DBus call: FocusApp({AppId})", appId);
                    ReplyEmpty(context);
                    break;
                }
            case "ShowApplications":
                {
                    _logger.LogInformation("DBus call: ShowApplications");
                    ReplyEmpty(context);
                    break;
                }
            case "GrabAccelerator":
                {
                    var accelerator = reader.ReadString();
                    var modeFlags = reader.ReadUInt32();
                    var grabFlags = reader.ReadUInt32();

                    var actionId = RegisterAccelerator(accelerator, modeFlags, grabFlags);
                    _logger.LogInformation("DBus call: GrabAccelerator({Accelerator}) => {ActionId}", accelerator, actionId);

                    ReplyUInt32(context, actionId);
                    break;
                }
            case "GrabAccelerators":
                {
                    var accelerators = ReadAcceleratorRequests(ref reader);
                    var actionIds = new uint[accelerators.Count];

                    for (var i = 0; i < accelerators.Count; i++)
                    {
                        var requestInfo = accelerators[i];
                        actionIds[i] = RegisterAccelerator(requestInfo.Accelerator, requestInfo.ModeFlags, requestInfo.GrabFlags);
                    }

                    _logger.LogInformation("DBus call: GrabAccelerators({Count})", actionIds.Length);
                    ReplyUInt32Array(context, actionIds);
                    break;
                }
            case "UngrabAccelerator":
                {
                    var actionId = reader.ReadUInt32();
                    var success = _accelerators.Remove(actionId);

                    _logger.LogInformation("DBus call: UngrabAccelerator({ActionId}) => {Success}", actionId, success);
                    ReplyBool(context, success);
                    break;
                }
            case "UngrabAccelerators":
                {
                    var actionIds = reader.ReadArrayOfUInt32();
                    var removedAny = false;
                    foreach (var actionId in actionIds)
                    {
                        removedAny |= _accelerators.Remove(actionId);
                    }

                    _logger.LogInformation("DBus call: UngrabAccelerators({Count}) => {Success}", actionIds.Length, removedAny);
                    ReplyBool(context, removedAny);
                    break;
                }
            case "ScreenTransition":
                {
                    _logger.LogInformation("DBus call: ScreenTransition");
                    ReplyEmpty(context);
                    break;
                }
            default:
                context.ReplyError(
                    "org.freedesktop.DBus.Error.NotSupported",
                    $"org.gnome.Shell.{request.MemberAsString} is not implemented yet.");
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

                    if (propertyName != "OverviewActive")
                    {
                        context.ReplyError("org.freedesktop.DBus.Error.InvalidArgs", $"Property '{propertyName}' is not writable.");
                        return;
                    }

                    _overviewActive = reader.ReadVariantValue().GetBool();
                    _logger.LogInformation("DBus property set: OverviewActive={OverviewActive}", _overviewActive);
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
            case "Mode":
                using (var writer = context.CreateReplyWriter("v"))
                {
                    writer.WriteVariantString(_runtimeState.Mode);
                    context.Reply(writer.CreateMessage());
                }
                break;
            case "OverviewActive":
                using (var writer = context.CreateReplyWriter("v"))
                {
                    writer.WriteVariantBool(_overviewActive);
                    context.Reply(writer.CreateMessage());
                }
                break;
            case "ShellVersion":
                using (var writer = context.CreateReplyWriter("v"))
                {
                    writer.WriteVariantString(_runtimeState.ShellVersion);
                    context.Reply(writer.CreateMessage());
                }
                break;
            case "ShellReady":
                using (var writer = context.CreateReplyWriter("v"))
                {
                    writer.WriteVariantBool(_runtimeState.ShellReady);
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
        new("Mode", VariantValue.String(_runtimeState.Mode)),
        new("OverviewActive", VariantValue.Bool(_overviewActive)),
        new("ShellVersion", VariantValue.String(_runtimeState.ShellVersion)),
        new("ShellReady", VariantValue.Bool(_runtimeState.ShellReady))
    ];

    uint RegisterAccelerator(string accelerator, uint modeFlags, uint grabFlags)
    {
        var actionId = _nextActionId++;
        _accelerators[actionId] = new AcceleratorRequest(accelerator, modeFlags, grabFlags);
        return actionId;
    }

    static List<AcceleratorRequest> ReadAcceleratorRequests(ref Reader reader)
    {
        var list = new List<AcceleratorRequest>();
        var arrayEnd = reader.ReadArrayStart(DBusType.Struct);

        while (reader.HasNext(arrayEnd))
        {
            reader.AlignStruct();
            var accelerator = reader.ReadString();
            var modeFlags = reader.ReadUInt32();
            var grabFlags = reader.ReadUInt32();
            list.Add(new AcceleratorRequest(accelerator, modeFlags, grabFlags));
        }

        return list;
    }

    static void ReplyEmpty(MethodContext context)
    {
        using var writer = context.CreateReplyWriter(null);
        context.Reply(writer.CreateMessage());
    }

    static void ReplyEval(MethodContext context, bool success, string result)
    {
        using var writer = context.CreateReplyWriter("bs");
        writer.WriteBool(success);
        writer.WriteString(result);
        context.Reply(writer.CreateMessage());
    }

    static void ReplyBool(MethodContext context, bool value)
    {
        using var writer = context.CreateReplyWriter("b");
        writer.WriteBool(value);
        context.Reply(writer.CreateMessage());
    }

    static void ReplyUInt32(MethodContext context, uint value)
    {
        using var writer = context.CreateReplyWriter("u");
        writer.WriteUInt32(value);
        context.Reply(writer.CreateMessage());
    }

    static void ReplyUInt32Array(MethodContext context, uint[] values)
    {
        using var writer = context.CreateReplyWriter("au");
        writer.WriteArray(values);
        context.Reply(writer.CreateMessage());
    }

    readonly record struct AcceleratorRequest(string Accelerator, uint ModeFlags, uint GrabFlags);
}
