using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Tmds.DBus.Protocol;

namespace GnomeSurfaceShell.DBus;

sealed class GnomeShellBrightnessServer : IPathMethodHandler
{
    public const string ServiceName = "org.gnome.Shell.Brightness";
    public const string InterfaceName = "org.gnome.Shell.Brightness";
    public const string ObjectPath = "/org/gnome/Shell/Brightness";

    static readonly ReadOnlyMemory<byte> IntrospectionXml = """
<node>
  <interface name="org.gnome.Shell.Brightness">
    <method name="SetDimming">
      <arg type="b" direction="in" name="enable"/>
    </method>
    <method name="SetAutoBrightnessTarget">
      <arg type="d" direction="in" name="target"/>
    </method>
    <signal name="BrightnessChanged"/>
    <property name="HasBrightnessControl" type="b" access="read"/>
  </interface>
</node>
"""u8.ToArray();

    readonly ILogger<GnomeShellBrightnessServer> _logger;
    bool _isDimmingEnabled;
    double _autoBrightnessTarget;

    public GnomeShellBrightnessServer(ILogger<GnomeShellBrightnessServer> logger)
    {
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
            case "SetDimming":
                _isDimmingEnabled = reader.ReadBool();
                _logger.LogInformation("DBus call: SetDimming({Enabled})", _isDimmingEnabled);
                ReplyEmpty(context);
                break;
            case "SetAutoBrightnessTarget":
                _autoBrightnessTarget = reader.ReadDouble();
                _logger.LogInformation("DBus call: SetAutoBrightnessTarget({Target})", _autoBrightnessTarget);
                ReplyEmpty(context);
                break;
            default:
                context.ReplyUnknownMethodError();
                break;
        }

        return ValueTask.CompletedTask;
    }

    static void HandleProperties(MethodContext context)
    {
        var request = context.Request;
        var reader = request.GetBodyReader();

        switch (request.MemberAsString)
        {
            case "Get":
                {
                    var interfaceName = reader.ReadString();
                    var propertyName = reader.ReadString();
                    if (interfaceName != InterfaceName || propertyName != "HasBrightnessControl")
                    {
                        context.ReplyError("org.freedesktop.DBus.Error.InvalidArgs", "Unsupported brightness property request.");
                        return;
                    }

                    using var writer = context.CreateReplyWriter("v");
                    writer.WriteVariantBool(false);
                    context.Reply(writer.CreateMessage());
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
                    writer.WriteDictionary(new KeyValuePair<string, VariantValue>[]
                    {
                        new("HasBrightnessControl", VariantValue.Bool(false))
                    });
                    context.Reply(writer.CreateMessage());
                    return;
                }
            case "Set":
                context.ReplyError("org.freedesktop.DBus.Error.InvalidArgs", "Brightness properties are read-only.");
                return;
            default:
                context.ReplyUnknownMethodError();
                return;
        }
    }

    static void ReplyEmpty(MethodContext context)
    {
        using var writer = context.CreateReplyWriter(null);
        context.Reply(writer.CreateMessage());
    }
}
