using System.Threading.Tasks;
using Tmds.DBus.Protocol;

namespace GnomeSurfaceSession.DBus;

sealed class Login1Manager : DBusObject
{
	public const string ServiceName = "org.freedesktop.login1";
	public const string InterfaceName = "org.freedesktop.login1.Manager";
	public static readonly ObjectPath ObjectPath = new("/org/freedesktop/login1");

	public Login1Manager(DBusConnection connection)
		: base(connection, ServiceName, ObjectPath)
	{
	}

	public Task<string> CanPowerOffAsync() => CallStringMethodAsync("CanPowerOff");

	public Task<string> CanRebootAsync() => CallStringMethodAsync("CanReboot");

	public Task<string> CanSuspendAsync() => CallStringMethodAsync("CanSuspend");

	public Task PowerOffAsync(bool interactive) => CallBoolMethodAsync("PowerOff", interactive);

	public Task RebootAsync(bool interactive) => CallBoolMethodAsync("Reboot", interactive);

	public Task SuspendAsync(bool interactive) => CallBoolMethodAsync("Suspend", interactive);

	/// <summary>
	/// GetSessionByPID(u pid) → o
	/// Returns the D-Bus object path of the session associated with the given PID.
	/// Pass pid=0 to let logind use the calling process's own PID.
	/// </summary>
	public Task<ObjectPath> GetSessionByPidAsync(uint pid)
	{
		return Connection.CallMethodAsync(CreateMessage(), static (Message m, object? s) => ReadObjectPath(m), this);

		MessageBuffer CreateMessage()
		{
			var writer = Connection.GetMessageWriter();
			writer.WriteMethodCallHeader(
				destination: Destination,
				path: Path,
				@interface: InterfaceName,
				signature: "u",
				member: "GetSessionByPID");
			writer.WriteUInt32(pid);
			return writer.CreateMessage();
		}
	}

	Task<string> CallStringMethodAsync(string member)
	{
		return Connection.CallMethodAsync(CreateMessage(), static (Message m, object? s) => ReadString(m), this);

		MessageBuffer CreateMessage()
		{
			var writer = Connection.GetMessageWriter();
			writer.WriteMethodCallHeader(
				destination: Destination,
				path: Path,
				@interface: InterfaceName,
				member: member);
			return writer.CreateMessage();
		}
	}

	Task CallBoolMethodAsync(string member, bool value)
	{
		return Connection.CallMethodAsync(CreateMessage());

		MessageBuffer CreateMessage()
		{
			var writer = Connection.GetMessageWriter();
			writer.WriteMethodCallHeader(
				destination: Destination,
				path: Path,
				@interface: InterfaceName,
				signature: "b",
				member: member);
			writer.WriteBool(value);
			return writer.CreateMessage();
		}
	}

	static string ReadString(Message message)
	{
		var reader = message.GetBodyReader();
		return reader.ReadString();
	}

	static ObjectPath ReadObjectPath(Message message)
	{
		var reader = message.GetBodyReader();
		return reader.ReadObjectPath();
	}
}
