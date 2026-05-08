using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace GnomeSurfaceCanvas.Services;

public sealed class GnomeSurfaceCanvasIconResolver
{
	readonly ILogger _logger;
	readonly string[] _iconSearchRoots;
	readonly object _cacheSync = new();
	readonly Dictionary<string, string?> _themedLookupCache = new(StringComparer.Ordinal);
	readonly Dictionary<string, string[]> _themeDirsCache = new(StringComparer.Ordinal);

	const string DefaultIconTheme = "Adwaita";
	const string FallbackIconTheme = "hicolor";
	const string InterfaceSchema = "org.gnome.desktop.interface";
	const string IconThemeKey = "icon-theme";

	static readonly string[] IconExtensions =
	[
		".png",
		".xpm",
		".jpg",
		".jpeg",
		".webp",
		".bmp",
		".svg"
	];

	public GnomeSurfaceCanvasIconResolver(ILogger logger)
	{
		_logger = logger;
		_iconSearchRoots = BuildIconSearchRoots();
	}

	public void ClearCache()
	{
		lock (_cacheSync)
			_themedLookupCache.Clear();
	}

	public SKImage? ResolveIcon(Gio.Icon? icon)
	{
		_logger.LogInformation("[AppCatalog.ResolveIcon] Resolving icon. type={IconType} icon={Icon}",
			icon?.GetType().Name, icon?.ToString());

		switch (icon)
		{
			case Gio.FileIcon fileIcon:
				{
					var path = fileIcon.GetFile().GetPath();
					if (string.IsNullOrWhiteSpace(path))
						return null;
					return DecodeImageFromPath(path);
				}

			case Gio.LoadableIcon loadableIcon:
				return DecodeImageFromLoadableIcon(loadableIcon);

			case Gio.ThemedIcon themedIcon:
				return ResolveThemedIconFromFileSystem(themedIcon);

			case Gio.EmblemedIcon emblemedIcon:
				return ResolveIcon(emblemedIcon.GetIcon());

			default:
				_logger.LogWarning("[AppCatalog.ResolveIcon] Unsupported icon type. type={IconType}",
					icon?.GetType().Name);
				return null;
		}
	}

	SKImage? DecodeImageFromLoadableIcon(Gio.LoadableIcon loadableIcon)
	{
		try
		{
			using var stream = loadableIcon.Load(size: 48, out var contentType, cancellable: null);
			using var memory = new MemoryStream(capacity: 16 * 1024);
			var chunk = new byte[8192];

			while (true)
			{
				var read = stream.Read(chunk, cancellable: null);
				if (read <= 0)
					break;

				memory.Write(chunk, 0, (int)read);
			}

			if (memory.Length == 0)
				return null;

			using var data = SKData.CreateCopy(memory.ToArray());
			var image = SKImage.FromEncodedData(data);
			if (image is null)
			{
				_logger.LogDebug(
					"[AppCatalog.ResolveIcon] LoadableIcon decode returned null. content_type={ContentType}",
					contentType);
			}

			return image;
		}
		catch (Exception ex)
		{
			_logger.LogDebug(ex, "[AppCatalog.ResolveIcon] LoadableIcon decode failed.");
			return null;
		}
	}

	SKImage? ResolveThemedIconFromFileSystem(Gio.ThemedIcon themedIcon)
	{
		foreach (var name in ExpandThemedIconNames(themedIcon.GetNames()))
		{
			var path = FindThemedIconPath(name);
			if (string.IsNullOrWhiteSpace(path))
				continue;

			var image = DecodeImageFromPath(path);
			if (image is not null)
				return image;
		}

		_logger.LogDebug(
			"[AppCatalog.ResolveIcon] ThemedIcon was not resolved from filesystem paths. names={Names}",
			string.Join(',', themedIcon.GetNames()));
		return null;
	}

	SKImage? DecodeImageFromPath(string path)
	{
		try
		{
			if (path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
				return DecodeSvgFromPath(path);

			using var stream = File.OpenRead(path);
			return SKImage.FromEncodedData(stream);
		}
		catch (Exception ex)
		{
			_logger.LogDebug(ex, "[AppCatalog.ResolveIcon] Failed to decode icon file. path={Path}", path);
			return null;
		}
	}

	SKImage? DecodeSvgFromPath(string path)
	{
		try
		{
			const int targetSize = 48;
			var svg = new Svg.Skia.SKSvg();
			var picture = svg.Load(path);
			if (picture is null)
				return null;

			var cullRect = picture.CullRect;
			if (cullRect.IsEmpty)
				return null;

			var scale = targetSize / Math.Max(cullRect.Width, cullRect.Height);
			var width = (int)(cullRect.Width * scale);
			var height = (int)(cullRect.Height * scale);

			if (width <= 0 || height <= 0)
				return null;

			using var bitmap = new SKBitmap(width, height);
			using var canvas = new SKCanvas(bitmap);
			canvas.Clear(SKColors.Transparent);
			canvas.Scale(scale);
			canvas.DrawPicture(picture);
			canvas.Flush();

			return SKImage.FromBitmap(bitmap);
		}
		catch (Exception ex)
		{
			_logger.LogDebug(ex, "[AppCatalog.ResolveIcon] SVG decode failed. path={Path}", path);
			return null;
		}
	}

	string? FindThemedIconPath(string iconName)
	{
		lock (_cacheSync)
		{
			if (_themedLookupCache.TryGetValue(iconName, out var cachedPath))
				return cachedPath;
		}

		var themeChain = BuildThemeChain();

		foreach (var root in _iconSearchRoots)
		{
			var found = FindIconUnderRoot(root, themeChain, iconName);
			if (found is null)
				continue;

			lock (_cacheSync)
				_themedLookupCache[iconName] = found;

			return found;
		}

		lock (_cacheSync)
			_themedLookupCache[iconName] = null;

		return null;
	}

	string? FindIconUnderRoot(string root, string[] themeChain, string iconName)
	{
		try
		{
			foreach (var themeName in themeChain)
			{
				var themeRoot = Path.Combine(root, themeName);
				if (!Directory.Exists(themeRoot))
					continue;

				foreach (var subdir in GetThemeDirectories(themeRoot))
				{
					foreach (var ext in IconExtensions)
					{
						var candidate = Path.Combine(themeRoot, subdir, iconName + ext);
						if (File.Exists(candidate))
							return candidate;
					}
				}
			}

			foreach (var ext in IconExtensions)
			{
				var directPath = Path.Combine(root, iconName + ext);
				if (File.Exists(directPath))
					return directPath;
			}

			foreach (var ext in IconExtensions)
			{
				var pixmapsPath = Path.Combine(root, "pixmaps", iconName + ext);
				if (File.Exists(pixmapsPath))
					return pixmapsPath;
			}
		}
		catch
		{
			// Ignore inaccessible locations, keep scanning other roots.
		}

		return null;
	}

	string[] BuildThemeChain()
	{
		var ordered = new List<string>();
		var seen = new HashSet<string>(StringComparer.Ordinal);

		AddThemeAndParents(ordered, seen, GetCurrentIconTheme(), depth: 0);
		AddThemeAndParents(ordered, seen, DefaultIconTheme, depth: 0);
		AddThemeAndParents(ordered, seen, FallbackIconTheme, depth: 0);

		return [.. ordered];
	}

	string? GetCurrentIconTheme()
	{
		try
		{
			using var settings = Gio.Settings.New(InterfaceSchema);
			return settings.GetString(IconThemeKey);
		}
		catch (Exception ex)
		{
			_logger.LogDebug(ex, "[AppCatalog.ResolveIcon] Failed to read current icon theme.");
			return null;
		}
	}

	void AddThemeAndParents(List<string> ordered, HashSet<string> seen, string? themeName, int depth)
	{
		if (string.IsNullOrWhiteSpace(themeName) || depth > 8 || !seen.Add(themeName))
			return;

		ordered.Add(themeName);

		foreach (var inherited in GetThemeInherits(themeName))
			AddThemeAndParents(ordered, seen, inherited, depth + 1);
	}

	IEnumerable<string> GetThemeInherits(string themeName)
	{
		foreach (var root in _iconSearchRoots)
		{
			var indexThemePath = Path.Combine(root, themeName, "index.theme");
			if (!File.Exists(indexThemePath))
				continue;

			var inherits = ReadIconThemeValue(indexThemePath, "Inherits");
			if (string.IsNullOrWhiteSpace(inherits))
				continue;

			foreach (var item in inherits.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
				yield return item;

			yield break;
		}
	}

	string[] GetThemeDirectories(string themeRoot)
	{
		lock (_cacheSync)
		{
			if (_themeDirsCache.TryGetValue(themeRoot, out var cachedDirs))
				return cachedDirs;
		}

		var indexThemePath = Path.Combine(themeRoot, "index.theme");
		var directoriesValue = File.Exists(indexThemePath)
			? ReadIconThemeValue(indexThemePath, "Directories")
			: null;

		var directories = string.IsNullOrWhiteSpace(directoriesValue)
			? []
			: directoriesValue
				.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
				.Distinct(StringComparer.Ordinal)
				.ToArray();

		lock (_cacheSync)
			_themeDirsCache[themeRoot] = directories;

		return directories;
	}

	static string? ReadIconThemeValue(string indexThemePath, string key)
	{
		var inIconThemeSection = false;
		var prefix = key + '=';

		foreach (var rawLine in File.ReadLines(indexThemePath))
		{
			var line = rawLine.Trim();
			if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
				continue;

			if (line.StartsWith('[') && line.EndsWith(']'))
			{
				inIconThemeSection = string.Equals(line, "[Icon Theme]", StringComparison.Ordinal);
				continue;
			}

			if (!inIconThemeSection || !line.StartsWith(prefix, StringComparison.Ordinal))
				continue;

			return line[prefix.Length..].Trim();
		}

		return null;
	}

	static IEnumerable<string> ExpandThemedIconNames(string[] names)
	{
		var seen = new HashSet<string>(StringComparer.Ordinal);

		foreach (var original in names)
		{
			if (string.IsNullOrWhiteSpace(original))
				continue;

			foreach (var candidate in ExpandName(original))
			{
				if (seen.Add(candidate))
					yield return candidate;
			}
		}
	}

	static IEnumerable<string> ExpandName(string name)
	{
		yield return name;

		if (name.EndsWith("-symbolic", StringComparison.Ordinal))
			yield return name[..^"-symbolic".Length];

		var idx = name.LastIndexOf('-');
		while (idx > 0)
		{
			yield return name[..idx];
			idx = name.LastIndexOf('-', idx - 1);
		}
	}

	static string[] BuildIconSearchRoots()
	{
		var paths = new List<string>();

		TryAdd(paths, Path.Combine(GLib.Functions.GetUserDataDir(), "icons"));
		TryAdd(paths, Path.Combine(GLib.Functions.GetHomeDir(), ".icons"));

		foreach (var dataDir in GLib.Functions.GetSystemDataDirs())
		{
			TryAdd(paths, Path.Combine(dataDir, "icons"));
			TryAdd(paths, Path.Combine(dataDir, "pixmaps"));
		}

		TryAdd(paths, "/usr/share/pixmaps");

		return paths.Distinct(StringComparer.Ordinal).ToArray();
	}

	static void TryAdd(List<string> paths, string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
			return;

		if (Directory.Exists(path))
			paths.Add(path);
	}
}
