using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using GnomeSurfaceCanvas;
using GnomeSurfaceCanvas.Plugin;
using Microsoft.Extensions.Logging;

namespace GnomeSurfaceShell.PluginLoading;

public sealed class GnomeSurfacePluginLoader(ILogger<GnomeSurfacePluginLoader> logger)
{
	static readonly object ResolverLock = new();
	static readonly HashSet<string> DependencySearchDirectories = [];
	static bool _resolverInstalled;

	readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
	{
		PropertyNameCaseInsensitive = true
	};

	public IReadOnlyList<Type> DiscoverPluginTypes()
	{
		var pluginTypes = new List<Type>();
		foreach (var assemblyPath in DiscoverAssemblyPaths())
		{
			try
			{
				var assembly = LoadAssembly(assemblyPath);
				foreach (var type in assembly.GetTypes())
				{
					if (type.IsAbstract || !type.IsClass)
						continue;

					if (!typeof(IGnomeSurfacePlugin).IsAssignableFrom(type))
						continue;

					if (!typeof(SKActor).IsAssignableFrom(type) && !typeof(SKGLActor).IsAssignableFrom(type))
						continue;

					pluginTypes.Add(type);
					logger.LogInformation("[Plugin.Loader] Discovered plugin type {PluginType} from {AssemblyPath}.", type.FullName, assemblyPath);
				}
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "[Plugin.Loader] Failed to load plugin assembly {AssemblyPath}.", assemblyPath);
			}
		}

		return [.. pluginTypes.Distinct().OrderBy(type => type.FullName, StringComparer.Ordinal)];
	}

	IEnumerable<string> DiscoverAssemblyPaths()
	{
		var homePath = GetHomePath();
		var manifestPath = Path.Combine(homePath, "dotnet-tools.json");
		if (!File.Exists(manifestPath))
		{
			logger.LogWarning("[Plugin.Loader] Dotnet tool manifest not found. path={ManifestPath}", manifestPath);
			yield break;
		}

		DotNetToolManifest? manifest;
		try
		{
			manifest = JsonSerializer.Deserialize<DotNetToolManifest>(File.ReadAllText(manifestPath), _jsonOptions);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "[Plugin.Loader] Failed to parse dotnet tool manifest. path={ManifestPath}", manifestPath);
			yield break;
		}

		if (manifest?.Tools.Count is null or 0)
			yield break;

		foreach (var (packageId, tool) in manifest.Tools.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
		{
			if (!IsPluginPackage(packageId))
				continue;

			var cachePath = Path.Combine(homePath, ".dotnet", "toolResolverCache", "1", packageId);
			var cacheEntry = ReadCacheEntry(cachePath, tool.Version);
			var assemblyPath = cacheEntry?.PathToExecutable;
			if (string.IsNullOrWhiteSpace(assemblyPath))
				assemblyPath = FindAssemblyPathFromNuGetCache(homePath, packageId, tool.Version);

			if (string.IsNullOrWhiteSpace(assemblyPath))
			{
				logger.LogWarning("[Plugin.Loader] Plugin tool has no resolvable assembly. package={PackageId} version={Version}", packageId, tool.Version);
				continue;
			}

			if (!File.Exists(assemblyPath))
			{
				logger.LogWarning("[Plugin.Loader] Plugin assembly does not exist. package={PackageId} path={AssemblyPath}", packageId, assemblyPath);
				continue;
			}

			yield return assemblyPath;
		}
	}

	static string GetHomePath()
	{
		var home = Environment.GetEnvironmentVariable("HOME");
		return string.IsNullOrWhiteSpace(home)
			? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
			: home;
	}

	ToolResolverCacheEntry? ReadCacheEntry(string cachePath, string version)
	{
		if (!File.Exists(cachePath))
			return null;

		try
		{
			var entries = JsonSerializer.Deserialize<ToolResolverCacheEntry[]>(File.ReadAllText(cachePath), _jsonOptions) ?? [];
			return entries
				.Where(entry => string.Equals(entry.Version, version, StringComparison.OrdinalIgnoreCase))
				.OrderByDescending(entry => entry.TargetFramework, StringComparer.OrdinalIgnoreCase)
				.FirstOrDefault()
				?? entries.LastOrDefault();
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "[Plugin.Loader] Failed to parse tool resolver cache. path={CachePath}", cachePath);
			return null;
		}
	}

	static string? FindAssemblyPathFromNuGetCache(string homePath, string packageId, string version)
	{
		var toolsDirectory = Path.Combine(homePath, ".nuget", "packages", packageId, version, "tools", "net10.0", "any");
		if (!Directory.Exists(toolsDirectory))
			return null;

		return Directory
			.EnumerateFiles(toolsDirectory, "*.dll", SearchOption.TopDirectoryOnly)
			.FirstOrDefault(path => !Path.GetFileName(path).Contains('-', StringComparison.Ordinal));
	}

	static Assembly LoadAssembly(string assemblyPath)
	{
		InstallResolver(Path.GetDirectoryName(assemblyPath)!);
		return AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
	}

	static void InstallResolver(string pluginDirectory)
	{
		lock (ResolverLock)
		{
			DependencySearchDirectories.Add(pluginDirectory);
			if (_resolverInstalled)
				return;

			AssemblyLoadContext.Default.Resolving += ResolvePluginDependency;
			_resolverInstalled = true;
		}
	}

	static Assembly? ResolvePluginDependency(AssemblyLoadContext context, AssemblyName assemblyName)
	{
		lock (ResolverLock)
		{
			foreach (var directory in DependencySearchDirectories)
			{
				var candidate = Path.Combine(directory, $"{assemblyName.Name}.dll");
				if (File.Exists(candidate))
					return context.LoadFromAssemblyPath(candidate);
			}
		}

		return null;
	}

	static bool IsPluginPackage(string packageId)
	{
		return packageId.StartsWith("gnomesurfaceplugin", StringComparison.OrdinalIgnoreCase)
			|| packageId.StartsWith("gnomemauiplugin", StringComparison.OrdinalIgnoreCase);
	}
}
