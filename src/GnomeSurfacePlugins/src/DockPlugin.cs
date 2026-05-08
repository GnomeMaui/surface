using System;
using System.Runtime.Versioning;
using GnomeSurfaceCanvas;
using GnomeSurfaceCanvas.Plugin;
using GnomeSurfaceCanvas.Services;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace GnomeSurfacePlugins;

[UnsupportedOSPlatform("Windows")]
[UnsupportedOSPlatform("macOS")]
public sealed class DockPlugin : SKGLActor
{
	const string InterfaceSchema = "org.gnome.desktop.interface";
	const string IconThemeKey = "icon-theme";

	readonly GnomeSurfaceCanvasCatalog appCatalog;
	readonly ILogger<DockPlugin> logger;
	readonly Gio.Settings _interfaceSettings = Gio.Settings.New(InterfaceSchema);
	uint _iconThemeChangedSourceId;

	public DockPlugin(
		Clutter.Stage stage,
		Clutter.Actor parentActor,
		GnomeSurfaceCanvasCatalog appCatalog,
		ILogger<DockPlugin> logger)
		: base(stage, parentActor, logger)
	{
		this.appCatalog = appCatalog;
		this.logger = logger;
		_interfaceSettings.OnChanged += OnInterfaceSettingsChanged;
		Gesture.Clicked += OnGestureClicked;
		Gesture.Moved += OnPointerMoved;
		Gesture.Left += OnPointerLeft;
	}
	const int IconSize = 44;
	const int IconSlot = 58;
	const int DockHeight = 72;
	const int DockOverflowTop = 42;
	const int MaxDockApps = 14;

	static readonly string[] DockAppIds =
	[
		"org.gnome.Ptyxis.desktop",
		"org.gnome.Nautilus.desktop",
		"org.gnome.Settings.desktop",
		"org.gnome.Software.desktop",
		"org.gnome.Extensions.desktop",
		"org.gnome.tweaks.desktop",
		"org.gnome.Epiphany.desktop",
		"org.gnome.Weather.desktop",
		"org.gnome.font-viewer.desktop",
		"org.manjaro.pamac.manager.desktop",
	];

	readonly HashSet<string> _dockAppIdSet = new(DockAppIds, StringComparer.Ordinal);
	readonly SKPaint _backgroundPaint = new() { Color = new SKColor(18, 18, 22, 212), IsAntialias = true };
	readonly SKPaint _glossPaint = new() { IsAntialias = true };
	readonly SKPaint _borderPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
	readonly SKPaint _iconPaint = new() { IsAntialias = true };
	readonly SKPaint _dotPaint = new() { Color = new SKColor(255, 255, 255, 75), IsAntialias = true };
	readonly SKPaint _fallbackFillPaint = new() { IsAntialias = true };
	readonly SKPaint _fallbackTextPaint = new() { Color = SKColors.White, IsAntialias = true };
	readonly SKPaint _tooltipBgPaint = new() { Color = new SKColor(28, 28, 34, 228), IsAntialias = true };
	readonly SKPaint _tooltipTextPaint = new() { Color = SKColors.White, IsAntialias = true };
	readonly SKFont _fallbackFont = new(SKTypeface.FromFamilyName("Adwaita Sans"), IconSize * 0.48f);
	readonly SKFont _tooltipFont = new(SKTypeface.FromFamilyName("Adwaita Sans"), 13);
	static readonly SKSamplingOptions IconSampling = new(SKCubicResampler.Mitchell);
	ShellAppEntry[] _dockAppsCache = [];
	bool _dockAppsLoaded;
	int _width = 1;
	int _height = DockHeight;
	int _offsetX;
	int _offsetY;
	int _hoveredIndex = -1;

	public override SurfaceLayer Layer { get; set; } = SurfaceLayer.Interactive;

	protected override int FrameInterval => 16;
	protected override int Width => _width;
	protected override int Height => _height;
	protected override int OffsetX => _offsetX;
	protected override int OffsetY => _offsetY;

	public override void EnsureVisible(MonitorLayout monitor)
	{
		var apps = GetDockApps();
		var dockWidth = Math.Clamp(apps.Length * IconSlot + 26, 96, Math.Max(96, monitor.Width - 48));
		_width = dockWidth;
		_height = DockHeight + DockOverflowTop;
		_offsetX = monitor.X + Math.Max(12, (monitor.Width - dockWidth) / 2);
		_offsetY = monitor.Y + Math.Max(0, monitor.Height - DockHeight - 18 - DockOverflowTop);

		base.EnsureVisible(monitor);
		StopAnimation();
	}

	protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
	{
		Paint(e.Surface.Canvas, e.Info.Width, e.Info.Height);
		base.OnPaintSurface(e);
	}

	void Paint(SKCanvas canvas, int width, int height)
	{
		canvas.Clear(SKColors.Transparent);

		var apps = GetDockApps();
		var t = (float)Frame * 0.05f;

		DrawBackground(canvas, width, DockOverflowTop, DockHeight, t);

		var baseY = DockOverflowTop + (DockHeight - IconSize) / 2;
		var x = 13;
		for (var i = 0; i < apps.Length; i++)
		{
			var slotCenterX = x + IconSize / 2f;
			var scale = ComputeScale(i, t);
			var iconW = (int)(IconSize * scale);
			var iconH = (int)(IconSize * scale);
			var iconX = (int)(slotCenterX - iconW / 2f);
			var iconY = baseY + IconSize - iconH; // bottom-anchored: icon grows upward

			DrawIconSlot(canvas, apps[i], i, iconX, iconY, iconW, iconH, t);

			if (i == _hoveredIndex)
				DrawTooltip(canvas, apps[i].Name, slotCenterX, iconY - 8, width);

			x += IconSlot;
		}
	}

	float ComputeScale(int index, float t)
	{
		if (_hoveredIndex < 0)
			return 1f;

		var d = Math.Abs(index - _hoveredIndex);
		var mag = d switch
		{
			0 => 0.40f,
			1 => 0.18f,
			2 => 0.07f,
			_ => 0f
		};

		return 1f + mag;
	}

	void DrawBackground(SKCanvas canvas, int width, int y, int height, float t)
	{
		var dockRect = new SKRoundRect(new SKRect(0, y, width, y + height), 20, 20);
		canvas.Save();
		canvas.ClipRoundRect(dockRect, SKClipOperation.Intersect, antialias: true);

		canvas.DrawRoundRect(dockRect, _backgroundPaint);

		using var glossShader = SKShader.CreateLinearGradient(
			new SKPoint(0, y), new SKPoint(0, y + height * 0.45f),
			[new SKColor(255, 255, 255, 42), new SKColor(255, 255, 255, 0)],
			null, SKShaderTileMode.Clamp);
		_glossPaint.Shader = glossShader;
		canvas.DrawRoundRect(new SKRect(1, y + 1, width - 1, y + height * 0.5f), 19, 19, _glossPaint);
		_glossPaint.Shader = null;

		var shimmerX = ((t * 0.28f) % 1f) * (width + 80) - 40;
		using var shimmerShader = SKShader.CreateLinearGradient(
			new SKPoint(shimmerX - 40, y), new SKPoint(shimmerX + 40, y),
			[new SKColor(255, 255, 255, 18), new SKColor(255, 255, 255, 72), new SKColor(255, 255, 255, 18)],
			null, SKShaderTileMode.Clamp);
		_borderPaint.Shader = shimmerShader;
		canvas.DrawRoundRect(new SKRect(0.5f, y + 0.5f, width - 0.5f, y + height - 0.5f), 20, 20, _borderPaint);
		_borderPaint.Shader = null;

		canvas.Restore();
	}

	void DrawIconSlot(SKCanvas canvas, ShellAppEntry app, int index, int x, int y, int w, int h, float t)
	{
		if (app.Icon is not null)
		{
			canvas.DrawImage(app.Icon, new SKRect(x, y, x + w, y + h), IconSampling, _iconPaint);
		}
		else
		{
			DrawFallbackIcon(canvas, app, index, x, y, w, h);
		}

		var dotCx = x + w / 2f;
		canvas.DrawCircle(dotCx, y + h + 5, 2.4f, _dotPaint);
	}

	void DrawFallbackIcon(SKCanvas canvas, ShellAppEntry app, int index, int x, int y, int w, int h)
	{
		HsvToRgb((index * 41f) % 360f, 0.65f, 0.85f, out var r1, out var g1, out var b1);
		HsvToRgb((index * 41f + 50f) % 360f, 0.55f, 0.65f, out var r2, out var g2, out var b2);
		using var gradShader = SKShader.CreateLinearGradient(
			new SKPoint(x, y), new SKPoint(x + w, y + h),
			[new SKColor(r1, g1, b1), new SKColor(r2, g2, b2)],
			null, SKShaderTileMode.Clamp);
		_fallbackFillPaint.Shader = gradShader;
		canvas.DrawRoundRect(new SKRect(x, y, x + w, y + h), 10, 10, _fallbackFillPaint);
		_fallbackFillPaint.Shader = null;

		var letter = string.IsNullOrWhiteSpace(app.Name) ? "?" : app.Name[..1].ToUpperInvariant();
		_fallbackFont.MeasureText(letter, out var bounds);
		canvas.DrawText(
			letter,
			x + (w - bounds.Width) * 0.5f - bounds.Left,
			y + (h - bounds.Height) * 0.5f - bounds.Top,
			_fallbackFont, _fallbackTextPaint);
	}

	void DrawTooltip(SKCanvas canvas, string name, float centerX, float bottomY, int canvasWidth)
	{
		if (string.IsNullOrWhiteSpace(name))
			return;

		_tooltipFont.MeasureText(name, out var tb);
		const float pad = 10f;
		var bw = tb.Width + pad * 2;
		var bh = 24f;
		var bx = Math.Clamp(centerX - bw / 2f, 4f, canvasWidth - bw - 4f);
		var by = bottomY - bh - 4f;

		canvas.DrawRoundRect(new SKRect(bx, by, bx + bw, by + bh), 7, 7, _tooltipBgPaint);
		canvas.DrawText(name, bx + pad - tb.Left, by + bh / 2f - tb.MidY, _tooltipFont, _tooltipTextPaint);

	}

	void OnGestureClicked(object? sender, SKActorPointerEventArgs e)
	{
		var apps = GetDockApps();
		var index = (int)((e.X - 13) / IconSlot);
		if (index < 0 || index >= apps.Length)
			return;

		Invalidate();
		appCatalog.Launch(apps[index].Id);
	}

	void OnPointerMoved(object? sender, SKActorRawEventArgs e)
	{
		var apps = GetDockApps();
		var localX = e.X - _offsetX;
		var index = (int)((localX - 13) / IconSlot);
		var newHover = index >= 0 && index < apps.Length ? index : -1;
		if (newHover == _hoveredIndex)
			return;

		_hoveredIndex = newHover;
		if (_hoveredIndex >= 0)
			EnsureAnimationRunning();
		else
			StopAnimation();

		Invalidate();
	}

	void OnPointerLeft(object? sender, SKActorRawEventArgs e)
	{
		if (_hoveredIndex < 0)
			return;
		_hoveredIndex = -1;
		StopAnimation();
		Invalidate();
	}

	ShellAppEntry[] GetDockApps()
	{
		if (_dockAppsLoaded)
			return _dockAppsCache;

		var apps = appCatalog.GetApps();
		_dockAppsCache = apps
			.Where(app => _dockAppIdSet.Contains(app.Id))
			.Take(MaxDockApps)
			.ToArray();
		_dockAppsLoaded = true;

		logger.LogInformation(
			"[DockPlugin] Dock apps loaded once. count={Count} ids={AppIds}",
			_dockAppsCache.Length,
			string.Join(',', _dockAppsCache.Select(static app => app.Id)));

		return _dockAppsCache;
	}

	void OnInterfaceSettingsChanged(Gio.Settings sender, Gio.Settings.ChangedSignalArgs args)
	{
		if (args.Key == IconThemeKey)
			QueueIconThemeChanged();
	}

	void QueueIconThemeChanged()
	{
		if (_iconThemeChangedSourceId != 0)
			return;

		_iconThemeChangedSourceId = GLib.Functions.IdleAdd(GLib.Constants.PRIORITY_DEFAULT_IDLE, () =>
		{
			_iconThemeChangedSourceId = 0;
			var iconTheme = _interfaceSettings.GetString(IconThemeKey);

			appCatalog.Refresh();
			_dockAppsCache = [];
			_dockAppsLoaded = false;

			logger.LogInformation("[DockPlugin] Icon theme changed, dock icons reloaded. icon_theme={IconTheme}", iconTheme);
			Invalidate();
			return GLib.Constants.SOURCE_REMOVE;
		});
	}

	static void HsvToRgb(float h, float s, float v, out byte r, out byte g, out byte b)
	{
		var hi = (int)(h / 60f) % 6;
		var f = h / 60f - MathF.Floor(h / 60f);
		var p = v * (1 - s);
		var q = v * (1 - f * s);
		var t2 = v * (1 - (1 - f) * s);
		var (rv, gv, bv) = hi switch
		{
			0 => (v, t2, p),
			1 => (q, v, p),
			2 => (p, v, t2),
			3 => (p, q, v),
			4 => (t2, p, v),
			_ => (v, p, q)
		};
		r = (byte)(rv * 255);
		g = (byte)(gv * 255);
		b = (byte)(bv * 255);
	}

	public override void Destroy()
	{
		if (_iconThemeChangedSourceId != 0 && GLib.Functions.SourceRemove(_iconThemeChangedSourceId))
			_iconThemeChangedSourceId = 0;

		_interfaceSettings.OnChanged -= OnInterfaceSettingsChanged;
		_interfaceSettings.Dispose();

		Gesture.Clicked -= OnGestureClicked;
		Gesture.Moved -= OnPointerMoved;
		Gesture.Left -= OnPointerLeft;

		_backgroundPaint.Dispose();
		_glossPaint.Dispose();
		_borderPaint.Dispose();
		_iconPaint.Dispose();
		_dotPaint.Dispose();
		_fallbackFillPaint.Dispose();
		_fallbackTextPaint.Dispose();
		_tooltipBgPaint.Dispose();
		_tooltipTextPaint.Dispose();
		_fallbackFont.Dispose();
		_tooltipFont.Dispose();

		base.Destroy();
	}
}
