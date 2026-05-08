using System.Runtime.Versioning;
using GnomeSurfaceCanvas;
using GnomeSurfaceCanvas.Plugin;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace GnomeSurfacePlugins;

[UnsupportedOSPlatform("Windows")]
[UnsupportedOSPlatform("macOS")]
public class BackgroundImagePlugin(Clutter.Stage stage, Clutter.Actor parentActor, ILogger<BackgroundImagePlugin> logger)
	: SKActor(stage, parentActor, logger)
{
	const string BackgroundSchema = "org.gnome.desktop.background";
	const string PictureUriKey = "picture-uri";
	const string PictureUriDarkKey = "picture-uri-dark";

	readonly Gio.Settings _backgroundSettings = Gio.Settings.New(BackgroundSchema);
	readonly ThemeDetector _themeDetector = new();
	uint _backgroundChangedSourceId;
	string? _currentImagePath;
	SKBitmap? _currentBitmap;
	int _width = 1;
	int _height = 1;

	protected override int Width => _width;
	protected override int Height => _height;
	protected override int OffsetX => 0;
	protected override int OffsetY => 0;
	public override SurfaceLayer Layer { get; set; } = SurfaceLayer.Foundation;

	protected override Clutter.Actor CreateActor()
	{
		var actor = base.CreateActor();
		actor.SetReactive(false);
		return actor;
	}

	public override void EnsureVisible(MonitorLayout monitor)
	{
		_width = Math.Max(1, monitor.Width);
		_height = Math.Max(1, monitor.Height);
		EnsureSubscriptions();
		base.EnsureVisible(monitor);
	}

	protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
	{
		Paint(e.Surface.Canvas, e.Info.Width, e.Info.Height);
		base.OnPaintSurface(e);
	}

	void Paint(SKCanvas canvas, int width, int height)
	{
		canvas.Clear(_themeDetector.Current.IsDark ? new SKColor(36, 31, 49) : new SKColor(246, 245, 244));

		var bitmap = GetCurrentBitmap();
		if (bitmap is null || bitmap.Width <= 0 || bitmap.Height <= 0)
			return;

		var scale = Math.Max(width / (float)bitmap.Width, height / (float)bitmap.Height);
		var scaledWidth = bitmap.Width * scale;
		var scaledHeight = bitmap.Height * scale;
		var left = (width - scaledWidth) * 0.5f;
		var top = (height - scaledHeight) * 0.5f;
		var destination = new SKRect(left, top, left + scaledWidth, top + scaledHeight);

		using var paint = new SKPaint
		{
			IsAntialias = true
		};

		canvas.DrawBitmap(bitmap, destination, paint);
	}

	void EnsureSubscriptions()
	{
		_backgroundSettings.OnChanged -= OnBackgroundSettingsChanged;
		_backgroundSettings.OnChanged += OnBackgroundSettingsChanged;

		_themeDetector.ThemeChanged -= OnThemeChanged;
		_themeDetector.ThemeChanged += OnThemeChanged;
	}

	void OnBackgroundSettingsChanged(Gio.Settings sender, Gio.Settings.ChangedSignalArgs args)
	{
		if (args.Key is PictureUriKey or PictureUriDarkKey)
			QueueBackgroundChanged();
	}

	void OnThemeChanged(ThemeInfo theme)
	{
		QueueBackgroundChanged();
	}

	void QueueBackgroundChanged()
	{
		if (_backgroundChangedSourceId != 0)
			return;

		_backgroundChangedSourceId = GLib.Functions.IdleAdd(GLib.Constants.PRIORITY_DEFAULT_IDLE, () =>
		{
			_backgroundChangedSourceId = 0;
			_currentImagePath = null;
			_currentBitmap?.Dispose();
			_currentBitmap = null;
			Invalidate();
			return GLib.Constants.SOURCE_REMOVE;
		});
	}

	SKBitmap? GetCurrentBitmap()
	{
		var imagePath = GetCurrentImagePath();
		if (string.IsNullOrWhiteSpace(imagePath))
			return null;

		if (string.Equals(_currentImagePath, imagePath, StringComparison.Ordinal) && _currentBitmap is not null)
			return _currentBitmap;

		_currentBitmap?.Dispose();
		_currentBitmap = null;
		_currentImagePath = imagePath;

		try
		{
			_currentBitmap = SKBitmap.Decode(imagePath);
			if (_currentBitmap is null)
				logger.LogWarning("[BackgroundImagePlugin] Failed to decode background image. path={Path}", imagePath);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "[BackgroundImagePlugin] Failed to load background image. path={Path}", imagePath);
		}

		return _currentBitmap;
	}

	string? GetCurrentImagePath()
	{
		var key = _themeDetector.Current.IsDark ? PictureUriDarkKey : PictureUriKey;
		var path = ParseFileUri(_backgroundSettings.GetString(key));
		if (!string.IsNullOrWhiteSpace(path))
			return path;

		return ParseFileUri(_backgroundSettings.GetString(PictureUriKey));
	}

	static string? ParseFileUri(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
			return null;

		if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
		{
			if (uri.IsFile)
				return uri.LocalPath;

			return null;
		}

		return Path.IsPathRooted(value) ? value : null;
	}

	public override void Destroy()
	{
		if (_backgroundChangedSourceId != 0 && GLib.Functions.SourceRemove(_backgroundChangedSourceId))
			_backgroundChangedSourceId = 0;

		_backgroundSettings.OnChanged -= OnBackgroundSettingsChanged;
		_themeDetector.ThemeChanged -= OnThemeChanged;
		_backgroundSettings.Dispose();
		_themeDetector.Dispose();
		_currentBitmap?.Dispose();
		_currentBitmap = null;

		base.Destroy();
	}
}
