using System;
using System.Runtime.Versioning;
using GnomeSurfaceCanvas;
using GnomeSurfaceCanvas.Plugin;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace GnomeSurfaceDemo;

[UnsupportedOSPlatform("Windows")]
[UnsupportedOSPlatform("macOS")]
public class CPUDemo : SKActor
{
	int _clickCount;
	public override SurfaceLayer Layer { get; set; } = SurfaceLayer.Ambient;

	static readonly (float X, float Y, float Radius, float Phase)[] Stars =
	[
		(32f, 26f, 1.6f, 0.0f),
		(68f, 40f, 1.3f, 1.2f),
		(102f, 22f, 1.5f, 2.1f),
		(156f, 36f, 1.1f, 2.8f),
		(210f, 18f, 1.4f, 3.6f),
		(262f, 30f, 1.7f, 4.4f),
		(314f, 24f, 1.2f, 5.0f),
		(356f, 38f, 1.5f, 5.8f),
		(396f, 20f, 1.3f, 0.7f),
		(428f, 34f, 1.4f, 1.9f)
	];

	public CPUDemo(Clutter.Stage stage, Clutter.Actor parent, ILogger<SKActor> logger)
		: base(stage, parent, logger)
	{
		Initialize();
	}

	void Initialize()
	{
		Gesture.Clicked += OnGestureClicked;
	}

	protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
	{
		Paint(e.Surface.Canvas, e.Info.Width, e.Info.Height);
		base.OnPaintSurface(e);
	}

	void Paint(SKCanvas canvas, int width, int height)
	{
		var bounds = new SKRoundRect(new SKRect(0, 0, width, height), 18, 18);
		canvas.Save();
		canvas.ClipRoundRect(bounds, SKClipOperation.Intersect, antialias: true);

		using var background = new SKPaint
		{
			Color = new SKColor(104, 33, 122, 220),
			IsAntialias = true
		};
		canvas.DrawRoundRect(bounds, background);
		DrawTwinklingStars(canvas);

		using var accent = new SKPaint
		{
			Color = new SKColor(255, 255, 255, 42),
			IsAntialias = true
		};
		canvas.DrawCircle(width - 58, 46, 72, accent);
		canvas.DrawCircle(width - 145, height + 12, 96, accent);

		var typeface = SKFontManager.Default.MatchFamily("Adwaita Sans") ?? SKFontManager.Default.MatchFamily("FreeSans");

		using var titlePaint = new SKPaint
		{
			Color = SKColors.White,
			IsAntialias = true
		};
		using var titleFont = new SKFont(typeface, 28);
		canvas.DrawText("SkiaSharp", 26, 54, SKTextAlign.Left, titleFont, titlePaint);

		using var counterPaint = new SKPaint
		{
			Color = new SKColor(255, 255, 255, 230),
			IsAntialias = true
		};
		using var counterFont = new SKFont(typeface, 18);
		canvas.DrawText($"Clicks: {_clickCount}", width - 28, 54, SKTextAlign.Right, counterFont, counterPaint);

		using var subtitlePaint = new SKPaint
		{
			Color = new SKColor(235, 230, 240, 235),
			IsAntialias = true
		};
		using var subtitleFont = new SKFont(typeface, 16);
		canvas.DrawText("CPU surface -> Clutter Actor", 28, 92, SKTextAlign.Left, subtitleFont, subtitlePaint);

		using var line = new SKPaint
		{
			Color = new SKColor(255, 255, 255, 130),
			StrokeWidth = 3,
			IsAntialias = true
		};
		canvas.DrawLine(28, 118, width - 28, 118, line);

		canvas.Restore();
	}

	void DrawTwinklingStars(SKCanvas canvas)
	{
		var t = Frame * 0.18f;
		using var starPaint = new SKPaint { IsAntialias = true };

		foreach (var star in Stars)
		{
			var pulse = (MathF.Sin(t + star.Phase) + 1f) * 0.5f;
			var alpha = (byte)(40 + pulse * 190);
			starPaint.Color = new SKColor(255, 255, 255, alpha);
			canvas.DrawCircle(star.X, star.Y, star.Radius + pulse * 0.8f, starPaint);
		}
	}

	void OnGestureClicked(object? sender, SKActorPointerEventArgs e)
	{
		_clickCount++;
		Invalidate();
	}
}
