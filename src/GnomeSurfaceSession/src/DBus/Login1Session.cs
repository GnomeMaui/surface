using System.Threading.Tasks;
using Tmds.DBus.Protocol;

namespace GnomeSurfaceSession.DBus;

/// <summary>
/// D-Bus proxy for the org.freedesktop.login1.Session interface.
/// Properties are read via org.freedesktop.DBus.Properties.Get.
/// </summary>
sealed class Login1Session : DBusObject
{
	const string PropertiesInterface = "org.freedesktop.DBus.Properties";
	const string SessionInterface = "org.freedesktop.login1.Session";

	public Login1Session(DBusConnection connection, ObjectPath path)
		: base(connection, Login1Manager.ServiceName, path)
	{
	}

	/// <summary>
	/// Returns the session identifier (e.g. "c1", "2").
	/// This is the value exported as XDG_SESSION_ID.
	/// </summary>
	public Task<string> GetIdAsync() => GetStringPropertyAsync("Id");

	/// <summary>
	/// Returns the session type: "wayland", "x11", "mir", "tty", etc.
	/// </summary>
	public Task<string> GetTypeAsync() => GetStringPropertyAsync("Type");

	/// <summary>
	/// Returns the session state: "online", "active", or "closing".
	/// </summary>
	public Task<string> GetStateAsync() => GetStringPropertyAsync("State");

	/// <summary>
	/// Returns whether the session is active (in the foreground).
	/// </summary>
	public Task<bool> GetActiveAsync() => GetBoolPropertyAsync("Active");

	Task<string> GetStringPropertyAsync(string propertyName)
	{
		return Connection.CallMethodAsync(CreateMessage(), static (Message m, object? s) => ReadStringVariant(m), this);

		MessageBuffer CreateMessage()
		{
			var writer = Connection.GetMessageWriter();
			writer.WriteMethodCallHeader(
				destination: Destination,
				path: Path,
				@interface: PropertiesInterface,
				signature: "ss",
				member: "Get");
			writer.WriteString(SessionInterface);
			writer.WriteString(propertyName);
			return writer.CreateMessage();
		}
	}

	Task<bool> GetBoolPropertyAsync(string propertyName)
	{
		return Connection.CallMethodAsync(CreateMessage(), static (Message m, object? s) => ReadBoolVariant(m), this);

		MessageBuffer CreateMessage()
		{
			var writer = Connection.GetMessageWriter();
			writer.WriteMethodCallHeader(
				destination: Destination,
				path: Path,
				@interface: PropertiesInterface,
				signature: "ss",
				member: "Get");
			writer.WriteString(SessionInterface);
			writer.WriteString(propertyName);
			return writer.CreateMessage();
		}
	}

	static string ReadStringVariant(Message message)
	{
		var reader = message.GetBodyReader();
		var variant = reader.ReadVariantValue();
		return variant.GetString();
	}

	static bool ReadBoolVariant(Message message)
	{
		var reader = message.GetBodyReader();
		var variant = reader.ReadVariantValue();
		return variant.GetBool();
	}
}
