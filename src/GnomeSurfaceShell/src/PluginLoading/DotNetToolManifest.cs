using System.Text.Json.Serialization;

namespace GnomeSurfaceShell.PluginLoading;

public sealed class DotNetToolManifest
{
	[JsonPropertyName("tools")]
	public Dictionary<string, DotNetToolEntry> Tools { get; set; } = [];
}

public sealed class DotNetToolEntry
{
	[JsonPropertyName("version")]
	public string Version { get; set; } = string.Empty;

	[JsonPropertyName("commands")]
	public string[] Commands { get; set; } = [];
}

public sealed class ToolResolverCacheEntry
{
	public string Version { get; set; } = string.Empty;
	public string TargetFramework { get; set; } = string.Empty;
	public string RuntimeIdentifier { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public string Runner { get; set; } = string.Empty;
	public string PathToExecutable { get; set; } = string.Empty;
}
