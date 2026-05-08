using System;
using System.Threading.Tasks;
using GnomeSurfaceShell.Services;
using Microsoft.Extensions.Logging;
using Tmds.DBus.Protocol;

namespace GnomeSurfaceShell.DBus;

sealed class GnomeScreenSaverServer : IPathMethodHandler
{
    public const string ServiceName = "org.gnome.ScreenSaver";
    public const string InterfaceName = "org.gnome.ScreenSaver";
    public const string ObjectPath = "/org/gnome/ScreenSaver";

    static readonly ReadOnlyMemory<byte> IntrospectionXml = """
<node>
  <interface name="org.gnome.ScreenSaver">
    <method name="Lock"/>
    <method name="GetActive">
      <arg type="b" direction="out" name="active"/>
    </method>
    <method name="SetActive">
      <arg type="b" direction="in" name="value"/>
    </method>
    <method name="GetActiveTime">
      <arg type="u" direction="out" name="seconds"/>
    </method>
    <signal name="ActiveChanged">
      <arg type="b" name="active"/>
    </signal>
  </interface>
</node>
"""u8.ToArray();

    readonly SessionBusConnection _bus;
    readonly ILogger<GnomeScreenSaverServer> _logger;
    bool _active;
    DateTimeOffset? _activeSince;

    public GnomeScreenSaverServer(
        SessionBusConnection bus,
        ILogger<GnomeScreenSaverServer> logger)
    {
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

        if (request.PathAsString != ObjectPath || request.InterfaceAsString != InterfaceName)
        {
            context.ReplyUnknownMethodError();
            return ValueTask.CompletedTask;
        }

        var reader = request.GetBodyReader();
        switch (request.MemberAsString)
        {
            case "Lock":
                _logger.LogInformation("DBus call: ScreenSaver.Lock ignored; lock UI is not implemented yet.");
                ReplyEmpty(context);
                break;
            case "GetActive":
                _logger.LogInformation("DBus call: ScreenSaver.GetActive => {Active}", _active);
                ReplyBool(context, _active);
                break;
            case "SetActive":
                {
                    var active = reader.ReadBool();
                    _logger.LogInformation("DBus call: ScreenSaver.SetActive({Active})", active);
                    SetActive(active);
                    ReplyEmpty(context);
                    break;
                }
            case "GetActiveTime":
                var activeTime = GetActiveTime();
                _logger.LogInformation("DBus call: ScreenSaver.GetActiveTime => {ActiveTime}", activeTime);
                ReplyUInt32(context, activeTime);
                break;
            default:
                _logger.LogInformation("DBus call: ScreenSaver.{Member} is not supported.", request.MemberAsString);
                context.ReplyUnknownMethodError();
                break;
        }

        return ValueTask.CompletedTask;
    }

    void SetActive(bool active)
    {
        if (_active == active)
            return;

        _active = active;
        _activeSince = active ? DateTimeOffset.UtcNow : null;
        EmitActiveChanged(active);
    }

    uint GetActiveTime()
    {
        if (!_active || _activeSince is null)
            return 0;

        var seconds = (DateTimeOffset.UtcNow - _activeSince.Value).TotalSeconds;
        return seconds <= 0 ? 0 : (uint)Math.Min(seconds, uint.MaxValue);
    }

    void EmitActiveChanged(bool active)
    {
        using var writer = _bus.Connection.GetMessageWriter();
        writer.WriteSignalHeader(
            destination: null,
            path: ObjectPath,
            @interface: InterfaceName,
            member: "ActiveChanged",
            signature: "b");
        writer.WriteBool(active);

        var sent = _bus.TrySendMessage(writer.CreateMessage());
        if (!sent)
        {
            _logger.LogWarning("Failed to emit ScreenSaver.ActiveChanged({Active}).", active);
            return;
        }

        _logger.LogInformation("Emitted ScreenSaver.ActiveChanged({Active}).", active);
    }

    static void ReplyEmpty(MethodContext context)
    {
        using var writer = context.CreateReplyWriter(null);
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
}
