using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GnomeSurfaceSession.Services;
using Microsoft.Extensions.Logging;
using Tmds.DBus.Protocol;

namespace GnomeSurfaceSession.DBus;

/// <summary>
/// Manual DBus method handler for org.gnome.SessionManager and related objects.
/// </summary>
sealed class SessionManagerServer : IPathMethodHandler
{
    public const string ServiceName = "org.gnome.SessionManager";
    public const string InterfaceName = "org.gnome.SessionManager";
    public const string ClientPrivateInterfaceName = "org.gnome.SessionManager.ClientPrivate";
    public const string PresenceInterfaceName = "org.gnome.SessionManager.Presence";
    public const string ObjectPath = "/org/gnome/SessionManager";
    public const string ClientPathPrefix = "/org/gnome/SessionManager/Client";
    public const string PresencePath = "/org/gnome/SessionManager/Presence";

    static readonly ReadOnlyMemory<byte> IntrospectionXml = """
<node>
  <interface name="org.gnome.SessionManager">
    <method name="Setenv">
      <arg name="variable" type="s" direction="in"/>
      <arg name="value" type="s" direction="in"/>
    </method>
    <method name="GetLocale">
      <arg name="category" type="i" direction="in"/>
      <arg name="value" type="s" direction="out"/>
    </method>
    <method name="InitializationError">
      <arg name="message" type="s" direction="in"/>
      <arg name="fatal" type="b" direction="in"/>
    </method>
        <method name="Initialized"/>
        <method name="RegisterClient">
            <arg name="app_id" type="s" direction="in"/>
            <arg name="ignored" type="s" direction="in"/>
            <arg name="client_id" type="o" direction="out"/>
        </method>
        <method name="UnregisterClient">
            <arg name="client_id" type="o" direction="in"/>
        </method>
        <method name="RegisterRestore">
            <arg name="app_id" type="s" direction="in"/>
            <arg name="dbus_name" type="s" direction="in"/>
            <arg name="reason" type="u" direction="out"/>
            <arg name="instance_id" type="s" direction="out"/>
            <arg name="cleanup_ids" type="as" direction="out"/>
        </method>
        <method name="DeletedInstanceIds">
            <arg name="app_id" type="s" direction="in"/>
            <arg name="ids" type="as" direction="in"/>
        </method>
        <method name="UnregisterRestore">
            <arg name="app_id" type="s" direction="in"/>
            <arg name="instance_id" type="s" direction="in"/>
        </method>
        <method name="Inhibit">
            <arg name="app_id" type="s" direction="in"/>
            <arg name="ignored" type="u" direction="in"/>
            <arg name="reason" type="s" direction="in"/>
            <arg name="flags" type="u" direction="in"/>
            <arg name="inhibit_cookie" type="u" direction="out"/>
        </method>
        <method name="Uninhibit">
            <arg name="inhibit_cookie" type="u" direction="in"/>
        </method>
        <method name="IsInhibited">
            <arg name="flags" type="u" direction="in"/>
            <arg name="is_inhibited" type="b" direction="out"/>
        </method>
        <method name="GetInhibitors">
            <arg name="inhibitors" type="ao" direction="out"/>
        </method>
        <method name="Shutdown"/>
        <method name="Reboot"/>
        <method name="Suspend"/>
    <method name="CanShutdown">
            <arg name="availability" type="u" direction="out"/>
    </method>
    <method name="CanReboot">
            <arg name="availability" type="u" direction="out"/>
    </method>
    <method name="CanSuspend">
            <arg name="availability" type="u" direction="out"/>
    </method>
    <method name="CanRebootToFirmwareSetup">
      <arg name="is_allowed" type="b" direction="out"/>
    </method>
    <method name="Logout">
      <arg name="mode" type="u" direction="in"/>
    </method>
    <method name="IsSessionRunning">
      <arg name="running" type="b" direction="out"/>
    </method>
        <signal name="SessionRunning"/>
        <signal name="SessionOver"/>
        <signal name="ClientAdded">
            <arg name="id" type="o"/>
        </signal>
        <signal name="ClientRemoved">
            <arg name="id" type="o"/>
        </signal>
        <signal name="InhibitorAdded">
            <arg name="id" type="o"/>
        </signal>
        <signal name="InhibitorRemoved">
            <arg name="id" type="o"/>
        </signal>
    <property name="SessionName" type="s" access="read"/>
    <property name="Renderer" type="s" access="read"/>
    <property name="SessionIsActive" type="b" access="read"/>
    <property name="InhibitedActions" type="u" access="read"/>
    <property name="RestoreSupported" type="b" access="read"/>
  </interface>
</node>
"""u8.ToArray();

    static readonly ReadOnlyMemory<byte> ClientIntrospectionXml = """
<node>
    <interface name="org.gnome.SessionManager.ClientPrivate">
        <method name="EndSessionResponse">
            <arg name="is_ok" type="b" direction="in"/>
            <arg name="reason" type="s" direction="in"/>
        </method>
        <signal name="QueryEndSession">
            <arg name="flags" type="u"/>
        </signal>
        <signal name="EndSession">
            <arg name="flags" type="u"/>
        </signal>
    </interface>
</node>
"""u8.ToArray();

    static readonly ReadOnlyMemory<byte> PresenceIntrospectionXml = """
<node>
    <interface name="org.gnome.SessionManager.Presence">
        <property name="status" type="u" access="readwrite"/>
        <property name="status-text" type="s" access="readwrite"/>
        <method name="SetStatus">
            <arg type="u" name="status" direction="in"/>
        </method>
        <method name="SetStatusText">
            <arg type="s" name="status_text" direction="in"/>
        </method>
        <signal name="StatusChanged">
            <arg name="status" type="u"/>
        </signal>
        <signal name="StatusTextChanged">
            <arg name="status_text" type="s"/>
        </signal>
    </interface>
</node>
"""u8.ToArray();

    readonly GsmPhaseManager _phaseManager;
    readonly GsmClientStore _clients;
    readonly GsmInhibitorStore _inhibitors;
    readonly GsmSessionRestoreStore _restoreStore;
    readonly GsmPresenceService _presence;
    readonly SystemdManagerService _systemd;
    readonly ILogger<SessionManagerServer> _logger;

    public SessionManagerServer(
        GsmPhaseManager phaseManager,
        GsmClientStore clients,
        GsmInhibitorStore inhibitors,
        GsmSessionRestoreStore restoreStore,
        GsmPresenceService presence,
        SystemdManagerService systemd,
        ILogger<SessionManagerServer> logger)
    {
        _phaseManager = phaseManager;
        _clients = clients;
        _inhibitors = inhibitors;
        _restoreStore = restoreStore;
        _presence = presence;
        _systemd = systemd;
        _logger = logger;
    }

    public string Path => ObjectPath;

    public bool HandlesChildPaths => true;

    public async ValueTask HandleMethodAsync(MethodContext context)
    {
        var request = context.Request;
        var path = request.PathAsString;
        if (string.IsNullOrEmpty(path))
        {
            context.ReplyUnknownMethodError();
            return;
        }

        if (context.IsDBusIntrospectRequest)
        {
            if (path == ObjectPath)
            {
                context.ReplyIntrospectXml([IntrospectionXml]);
            }
            else if (path == PresencePath)
            {
                context.ReplyIntrospectXml([PresenceIntrospectionXml]);
            }
            else if (path.StartsWith(ClientPathPrefix, StringComparison.Ordinal))
            {
                context.ReplyIntrospectXml([ClientIntrospectionXml]);
            }
            else
            {
                context.ReplyUnknownMethodError();
            }

            return;
        }

        if (path.StartsWith(ClientPathPrefix, StringComparison.Ordinal))
        {
            HandleClientPrivate(context, path);
            return;
        }

        if (path == PresencePath)
        {
            HandlePresence(context);
            return;
        }

        if (path != ObjectPath)
        {
            context.ReplyUnknownMethodError();
            return;
        }

        if (context.IsPropertiesInterfaceRequest)
        {
            HandleProperties(context);
            return;
        }

        if (request.InterfaceAsString != InterfaceName)
        {
            context.ReplyUnknownMethodError();
            return;
        }

        var reader = request.GetBodyReader();

        switch (request.MemberAsString)
        {
            case "Setenv":
                {
                    var variable = reader.ReadString();
                    var value = reader.ReadString();

                    if (!_phaseManager.CanSetEnvironment)
                    {
                        context.ReplyError(
                            "org.gnome.SessionManager.Error.NotInInitialization",
                            "Setenv is only allowed during the initialization phase.");
                        break;
                    }

                    Environment.SetEnvironmentVariable(variable, value);
                    ReplyEmpty(context);
                    break;
                }
            case "GetLocale":
                {
                    _ = reader.ReadInt32();
                    ReplyString(context, Environment.GetEnvironmentVariable("LANG") ?? string.Empty);
                    break;
                }
            case "InitializationError":
                {
                    var message = reader.ReadString();
                    var fatal = reader.ReadBool();

                    if (_phaseManager.HandleInitializationError(fatal))
                    {
                        context.ReplyError(
                            "org.gnome.SessionManager.Error.FatalInitializationError",
                            message);
                        break;
                    }

                    ReplyEmpty(context);
                    break;
                }
            case "Initialized":
                {
                    if (!_phaseManager.HandleInitialized())
                    {
                        var error = _phaseManager.LastInitializationError
                            ?? "Initialized can only be called once during initialization phase.";
                        context.ReplyError(
                            "org.gnome.SessionManager.Error.InitializationFailed",
                            error);
                        break;
                    }

                    ReplyEmpty(context);
                    break;
                }
            case "RegisterClient":
                {
                    var appId = reader.ReadString();
                    var startupId = reader.ReadString();

                    // Match gnome-session behavior: reject only once shutdown starts.
                    if (_phaseManager.Phase >= GsmManagerPhase.QueryEndSession)
                    {
                        context.ReplyError(
                            "org.gnome.SessionManager.Error.NotInRunning",
                            "Unable to register client during shutdown.");
                        break;
                    }

                    var sender = request.SenderAsString ?? string.Empty;
                    var clientPath = _clients.RegisterClient(appId, startupId, sender);
                    ReplyObjectPath(context, clientPath);
                    break;
                }
            case "UnregisterClient":
                {
                    var clientPath = reader.ReadObjectPathAsString();
                    if (!_clients.UnregisterClient(clientPath))
                    {
                        context.ReplyError(
                            "org.gnome.SessionManager.Error.InvalidClient",
                            $"Unknown client id '{clientPath}'.");
                        break;
                    }

                    ReplyEmpty(context);
                    break;
                }
            case "RegisterRestore":
                {
                    var appId = reader.ReadString();
                    var dbusName = reader.ReadString();

                    if (string.IsNullOrWhiteSpace(appId))
                    {
                        context.ReplyError(
                            "org.freedesktop.DBus.Error.InvalidArgs",
                            "Invalid app id specified.");
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(dbusName))
                    {
                        dbusName = request.SenderAsString ?? string.Empty;
                    }

                    var (reason, instanceId, cleanupIds) = _restoreStore.RegisterRestore(appId, dbusName);
                    _logger.LogInformation(
                        "DBus call: RegisterRestore(appId={AppId}, dbusName={DbusName}) -> reason={Reason}, instanceId={InstanceId}, cleanupIds={CleanupCount}",
                        appId,
                        dbusName,
                        reason,
                        instanceId,
                        cleanupIds.Length);
                    ReplyRegisterRestore(context, reason, instanceId, cleanupIds);
                    break;
                }
            case "DeletedInstanceIds":
                {
                    var appId = reader.ReadString();
                    var ids = reader.ReadArrayOfString();
                    _restoreStore.DeletedInstanceIds(appId, ids);
                    _logger.LogInformation(
                        "DBus call: DeletedInstanceIds(appId={AppId}, idsCount={Count})",
                        appId,
                        ids.Length);
                    ReplyEmpty(context);
                    break;
                }
            case "UnregisterRestore":
                {
                    var appId = reader.ReadString();
                    var instanceId = reader.ReadString();

                    if (!_restoreStore.UnregisterRestore(appId, instanceId))
                    {
                        context.ReplyError(
                            "org.gnome.SessionManager.Error.NotRegistered",
                            "Provided instance not found");
                        break;
                    }

                    _logger.LogInformation(
                        "DBus call: UnregisterRestore(appId={AppId}, instanceId={InstanceId})",
                        appId,
                        instanceId);
                    ReplyEmpty(context);
                    break;
                }
            case "Inhibit":
                {
                    var appId = reader.ReadString();
                    _ = reader.ReadUInt32(); // ignored
                    var reason = reader.ReadString();
                    var flags = reader.ReadUInt32();

                    var sender = request.SenderAsString ?? string.Empty;
                    var cookie = _inhibitors.Inhibit(appId, reason, flags, sender);
                    ReplyUInt32(context, cookie);
                    break;
                }
            case "Uninhibit":
                {
                    var cookie = reader.ReadUInt32();
                    if (!_inhibitors.Uninhibit(cookie))
                    {
                        context.ReplyError(
                            "org.gnome.SessionManager.Error.InvalidInhibitor",
                            $"Unknown inhibitor cookie '{cookie}'.");
                        break;
                    }

                    ReplyEmpty(context);
                    break;
                }
            case "IsInhibited":
                {
                    var flags = reader.ReadUInt32();
                    ReplyBool(context, _inhibitors.IsInhibited(flags));
                    break;
                }
            case "GetInhibitors":
                {
                    ReplyObjectPathArray(context, _inhibitors.GetInhibitorObjectPaths());
                    break;
                }
            case "Shutdown":
                {
                    _logger.LogInformation(
                        "DBus call: Shutdown (phase={Phase}, canPerformPowerActions={CanPerform})",
                        _phaseManager.Phase,
                        _phaseManager.CanPerformPowerActions);

                    if (!_phaseManager.HandleShutdown())
                    {
                        _logger.LogWarning("Shutdown rejected: not in running phase or logout already in progress.");
                        context.ReplyError(
                            "org.gnome.SessionManager.Error.NotInRunning",
                            "Shutdown is only allowed during running phase.");
                        break;
                    }

                    _logger.LogInformation("Shutdown accepted: logout pipeline started.");
                    ReplyEmpty(context);

                    break;
                }
            case "Reboot":
                {
                    _logger.LogInformation(
                        "DBus call: Reboot (phase={Phase}, canPerformPowerActions={CanPerform})",
                        _phaseManager.Phase,
                        _phaseManager.CanPerformPowerActions);

                    if (!_phaseManager.HandleReboot())
                    {
                        _logger.LogWarning("Reboot rejected: not in running phase or logout already in progress.");
                        context.ReplyError(
                            "org.gnome.SessionManager.Error.NotInRunning",
                            "Reboot is only allowed during running phase.");
                        break;
                    }

                    _logger.LogInformation("Reboot accepted: logout pipeline started.");
                    ReplyEmpty(context);

                    break;
                }
            case "Suspend":
                {
                    _logger.LogInformation(
                        "DBus call: Suspend (phase={Phase}, canPerformPowerActions={CanPerform})",
                        _phaseManager.Phase,
                        _phaseManager.CanPerformPowerActions);

                    if (!_phaseManager.CanPerformPowerActions)
                    {
                        _logger.LogWarning("Suspend rejected: not in running phase.");
                        context.ReplyError(
                            "org.gnome.SessionManager.Error.NotInRunning",
                            "Suspend is only allowed during running phase.");
                        break;
                    }

                    try
                    {
                        await _systemd.SuspendAsync();
                        _logger.LogInformation("Suspend request forwarded to login1.");
                        ReplyEmpty(context);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Suspend failed while forwarding to login1.");
                        context.ReplyError("org.gnome.SessionManager.Error.SuspendFailed", ex.Message);
                    }

                    break;
                }
            case "CanShutdown":
                {
                    var availability = await ResolveAvailabilityAsync("CanShutdown", static systemd => systemd.CanPowerOffAsync());
                    ReplyUInt32(context, availability);
                    break;
                }
            case "CanReboot":
                {
                    var availability = await ResolveAvailabilityAsync("CanReboot", static systemd => systemd.CanRebootAsync());
                    ReplyUInt32(context, availability);
                    break;
                }
            case "CanSuspend":
                {
                    var availability = await ResolveAvailabilityAsync("CanSuspend", static systemd => systemd.CanSuspendAsync());
                    ReplyUInt32(context, availability);
                    break;
                }
            case "CanRebootToFirmwareSetup":
                {
                    ReplyBool(context, false);
                    break;
                }
            case "IsSessionRunning":
                {
                    ReplyBool(context, _phaseManager.IsSessionRunning);
                    break;
                }
            case "Logout":
                {
                    var mode = reader.ReadUInt32();
                    _logger.LogInformation(
                        "DBus call: Logout(mode={Mode}, phase={Phase}, canRequestLogout={CanRequestLogout})",
                        mode,
                        _phaseManager.Phase,
                        _phaseManager.CanRequestLogout);

                    if (!Enum.IsDefined(typeof(GsmLogoutMode), mode))
                    {
                        _logger.LogWarning("Logout rejected: invalid mode {Mode}.", mode);
                        context.ReplyError(
                            "org.gnome.SessionManager.Error.InvalidOption",
                            "Unknown logout mode flag.");
                        break;
                    }

                    if (!_phaseManager.HandleLogout((GsmLogoutMode)mode))
                    {
                        _logger.LogWarning("Logout rejected: not in running phase or logout already in progress.");
                        context.ReplyError(
                            "org.gnome.SessionManager.Error.NotInRunning",
                            "Logout is only allowed during running phase.");
                        break;
                    }

                    _logger.LogInformation("Logout accepted: mode={Mode}, logout pipeline started.", mode);
                    ReplyEmpty(context);
                    break;
                }
            default:
                ReplyNotImplemented(context, request.MemberAsString ?? "<unknown>");
                break;
        }

        return;
    }

    void HandleClientPrivate(MethodContext context, string path)
    {
        var request = context.Request;
        if (request.InterfaceAsString != ClientPrivateInterfaceName)
        {
            context.ReplyUnknownMethodError();
            return;
        }

        var reader = request.GetBodyReader();
        switch (request.MemberAsString)
        {
            case "EndSessionResponse":
                {
                    var isOk = reader.ReadBool();
                    var reason = reader.ReadString();
                    if (!_clients.HandleEndSessionResponse(path, isOk, reason))
                    {
                        context.ReplyError(
                            "org.gnome.SessionManager.Error.InvalidClient",
                            $"Unknown client id '{path}'.");
                        return;
                    }

                    ReplyEmpty(context);
                    return;
                }
            default:
                context.ReplyUnknownMethodError();
                return;
        }
    }

    void HandlePresence(MethodContext context)
    {
        var request = context.Request;

        if (context.IsPropertiesInterfaceRequest)
        {
            HandlePresenceProperties(context);
            return;
        }

        if (request.InterfaceAsString != PresenceInterfaceName)
        {
            context.ReplyUnknownMethodError();
            return;
        }

        var reader = request.GetBodyReader();
        switch (request.MemberAsString)
        {
            case "SetStatus":
                {
                    var status = reader.ReadUInt32();
                    _presence.SetStatus(status);
                    ReplyEmpty(context);
                    return;
                }
            case "SetStatusText":
                {
                    var statusText = reader.ReadString();
                    _presence.SetStatusText(statusText);
                    ReplyEmpty(context);
                    return;
                }
            default:
                context.ReplyUnknownMethodError();
                return;
        }
    }

    void HandlePresenceProperties(MethodContext context)
    {
        var request = context.Request;
        var reader = request.GetBodyReader();

        switch (request.MemberAsString)
        {
            case "Get":
                {
                    var interfaceName = reader.ReadString();
                    var propertyName = reader.ReadString();
                    if (interfaceName != PresenceInterfaceName)
                    {
                        context.ReplyError("org.freedesktop.DBus.Error.InvalidArgs", $"Unsupported interface '{interfaceName}'.");
                        return;
                    }

                    switch (propertyName)
                    {
                        case "status":
                            using (var writer = context.CreateReplyWriter("v"))
                            {
                                writer.WriteVariantUInt32(_presence.Status);
                                context.Reply(writer.CreateMessage());
                            }
                            break;
                        case "status-text":
                            using (var writer = context.CreateReplyWriter("v"))
                            {
                                writer.WriteVariantString(_presence.StatusText);
                                context.Reply(writer.CreateMessage());
                            }
                            break;
                        default:
                            context.ReplyError("org.freedesktop.DBus.Error.InvalidArgs", $"Unknown property '{propertyName}'.");
                            break;
                    }

                    return;
                }
            case "GetAll":
                {
                    var interfaceName = reader.ReadString();
                    if (interfaceName != PresenceInterfaceName)
                    {
                        context.ReplyError("org.freedesktop.DBus.Error.InvalidArgs", $"Unsupported interface '{interfaceName}'.");
                        return;
                    }

                    using var writer = context.CreateReplyWriter("a{sv}");
                    writer.WriteDictionary([
                        new KeyValuePair<string, VariantValue>("status", VariantValue.UInt32(_presence.Status)),
                        new KeyValuePair<string, VariantValue>("status-text", VariantValue.String(_presence.StatusText))
                    ]);
                    context.Reply(writer.CreateMessage());
                    return;
                }
            case "Set":
                {
                    var interfaceName = reader.ReadString();
                    var propertyName = reader.ReadString();
                    if (interfaceName != PresenceInterfaceName)
                    {
                        context.ReplyError("org.freedesktop.DBus.Error.InvalidArgs", $"Unsupported interface '{interfaceName}'.");
                        return;
                    }

                    if (propertyName == "status")
                    {
                        var value = reader.ReadVariantValue().GetUInt32();
                        _presence.SetStatus(value);
                        ReplyEmpty(context);
                        return;
                    }

                    if (propertyName == "status-text")
                    {
                        var value = reader.ReadVariantValue().GetString();
                        _presence.SetStatusText(value ?? string.Empty);
                        ReplyEmpty(context);
                        return;
                    }

                    context.ReplyError("org.freedesktop.DBus.Error.InvalidArgs", $"Unknown property '{propertyName}'.");
                    return;
                }
            default:
                context.ReplyUnknownMethodError();
                return;
        }
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
            default:
                context.ReplyUnknownMethodError();
                return;
        }
    }

    void ReplyProperty(MethodContext context, string propertyName)
    {
        switch (propertyName)
        {
            case "SessionName":
                using (var writer = context.CreateReplyWriter("v"))
                {
                    writer.WriteVariantString(_phaseManager.SessionName);
                    context.Reply(writer.CreateMessage());
                }
                break;
            case "Renderer":
                using (var writer = context.CreateReplyWriter("v"))
                {
                    writer.WriteVariantString(_phaseManager.Renderer);
                    context.Reply(writer.CreateMessage());
                }
                break;
            case "SessionIsActive":
                using (var writer = context.CreateReplyWriter("v"))
                {
                    writer.WriteVariantBool(_phaseManager.SessionIsActive);
                    context.Reply(writer.CreateMessage());
                }
                break;
            case "InhibitedActions":
                using (var writer = context.CreateReplyWriter("v"))
                {
                    writer.WriteVariantUInt32(_inhibitors.GetInhibitedActions());
                    context.Reply(writer.CreateMessage());
                }
                break;
            case "RestoreSupported":
                using (var writer = context.CreateReplyWriter("v"))
                {
                    writer.WriteVariantBool(_phaseManager.RestoreSupported);
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
        new("SessionName", VariantValue.String(_phaseManager.SessionName)),
        new("Renderer", VariantValue.String(_phaseManager.Renderer)),
        new("SessionIsActive", VariantValue.Bool(_phaseManager.SessionIsActive)),
        new("InhibitedActions", VariantValue.UInt32(_inhibitors.GetInhibitedActions())),
        new("RestoreSupported", VariantValue.Bool(_phaseManager.RestoreSupported)),
    ];

    async Task<uint> ResolveAvailabilityAsync(string methodName, Func<SystemdManagerService, Task<uint>> action)
    {
        try
        {
            var availability = await action(_systemd);
            _logger.LogInformation(
                "DBus call: {Method} -> availability={Availability} (phase={Phase})",
                methodName,
                availability,
                _phaseManager.Phase);
            return availability;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DBus call: {Method} failed while querying login1 availability.", methodName);
            return 0u;
        }
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

    static void ReplyString(MethodContext context, string value)
    {
        using var writer = context.CreateReplyWriter("s");
        writer.WriteString(value);
        context.Reply(writer.CreateMessage());
    }

    static void ReplyUInt32(MethodContext context, uint value)
    {
        using var writer = context.CreateReplyWriter("u");
        writer.WriteUInt32(value);
        context.Reply(writer.CreateMessage());
    }

    static void ReplyRegisterRestore(MethodContext context, uint reason, string instanceId, string[] cleanupIds)
    {
        using var writer = context.CreateReplyWriter("usas");
        writer.WriteUInt32(reason);
        writer.WriteString(instanceId);
        writer.WriteArray(cleanupIds);
        context.Reply(writer.CreateMessage());
    }

    static void ReplyObjectPath(MethodContext context, string path)
    {
        using var writer = context.CreateReplyWriter("o");
        writer.WriteObjectPath(new ObjectPath(path));
        context.Reply(writer.CreateMessage());
    }

    static void ReplyObjectPathArray(MethodContext context, string[] paths)
    {
        using var writer = context.CreateReplyWriter("ao");
        var arrayStart = writer.WriteArrayStart(DBusType.ObjectPath);
        foreach (var path in paths)
        {
            writer.WriteObjectPath(new ObjectPath(path));
        }

        writer.WriteArrayEnd(arrayStart);
        context.Reply(writer.CreateMessage());
    }

    static void ReplyNotImplemented(MethodContext context, string memberName)
    {
        context.ReplyError(
            "org.freedesktop.DBus.Error.NotSupported",
            $"org.gnome.SessionManager.{memberName} is not implemented yet.");
    }
}
