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
public class ChristmasTreeCPU : SKActor
{
	static readonly (float X, float Y, SKColor Color, float Phase)[] Lights =
	[
		(174, 92, SKColors.Gold, 0.0f),
		(150, 128, SKColors.DeepSkyBlue, 1.2f),
		(198, 132, SKColors.OrangeRed, 2.1f),
		(126, 170, SKColors.HotPink, 2.9f),
		(176, 174, SKColors.Lime, 3.7f),
		(226, 178, SKColors.Cyan, 4.4f),
		(104, 220, SKColors.Yellow, 5.1f),
		(150, 224, SKColors.Tomato, 5.9f),
		(206, 228, SKColors.White, 0.8f),
		(256, 232, SKColors.DeepPink, 1.8f)
	];

	bool _hovered;
	public override SurfaceLayer Layer { get; set; } = SurfaceLayer.Ambient;

	protected override int Width => 360;
	protected override int Height => 300;
	protected override int OffsetX => 496;
	protected override int OffsetY => 72;

	public ChristmasTreeCPU(Clutter.Stage stage, Clutter.Actor parent, ILogger<SKActor> logger)
		: base(stage, parent, logger)
	{
		Initialize();
	}

	void Initialize()
	{
		Gesture.Entered += OnPointerEntered;
		Gesture.Left += OnPointerLeft;
	}

	protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
	{
		Paint(e.Surface.Canvas, e.Info.Width, e.Info.Height);
		base.OnPaintSurface(e);
	}

	void Paint(SKCanvas canvas, int width, int height)
	{
		canvas.Clear(SKColors.Transparent);

		using var snow = new SKPaint { Color = new SKColor(255, 255, 255, 230), IsAntialias = true };
		for (var i = 0; i < 42; i++)
		{
			var x = (i * 67 + 17) % width;
			var y = (i * 43 + (int)(Frame * 1.5f)) % height;
			canvas.DrawCircle(x, y, 1.2f + i % 3, snow);
		}

		DrawTree(canvas, width, height);
		DrawLights(canvas);

		using var font = new SKFont(SKTypeface.FromFamilyName("Adwaita Sans"), 15);
		using var text = new SKPaint { Color = new SKColor(238, 246, 255, 225), IsAntialias = true };
		canvas.DrawText(_hovered ? "hover: lights active" : "move pointer over the tree", 18, height - 18, font, text);
	}

	static void DrawTree(SKCanvas canvas, int width, int height)
	{
		var center = width * 0.5f;

		using var trunk = new SKPaint { Color = new SKColor(104, 65, 38), IsAntialias = true };
		canvas.DrawRoundRect(new SKRect(center - 18, height - 62, center + 18, height - 18), 5, 5, trunk);

		using var green = new SKPaint { Color = new SKColor(21, 116, 66), IsAntialias = true };
		DrawTriangle(canvas, green, center, 42, 92, 112);
		DrawTriangle(canvas, green, center, 82, 134, 156);
		DrawTriangle(canvas, green, center, 128, 174, 188);
		DrawTriangle(canvas, green, center, 180, 212, 204);

		using var garland = new SKPaint
		{
			Color = new SKColor(255, 236, 154, 175),
			IsAntialias = true,
			StrokeWidth = 4,
			Style = SKPaintStyle.Stroke
		};
		canvas.DrawArc(new SKRect(center - 92, 112, center + 92, 176), 20, 140, false, garland);
		canvas.DrawArc(new SKRect(center - 132, 168, center + 132, 248), 20, 140, false, garland);

		using var star = new SKPaint { Color = SKColors.Gold, IsAntialias = true };
		using var path = new SKPath();
		for (var i = 0; i < 10; i++)
		{
			var radius = i % 2 == 0 ? 22f : 9f;
			var angle = -MathF.PI / 2f + i * MathF.PI / 5f;
			var x = center + MathF.Cos(angle) * radius;
			var y = 36 + MathF.Sin(angle) * radius;
			if (i == 0)
				path.MoveTo(x, y);
			else
				path.LineTo(x, y);
		}
		path.Close();
		canvas.DrawPath(path, star);
	}

	static void DrawTriangle(SKCanvas canvas, SKPaint paint, float center, float top, float halfWidth, float height)
	{
		using var path = new SKPath();
		path.MoveTo(center, top);
		path.LineTo(center - halfWidth, top + height);
		path.LineTo(center + halfWidth, top + height);
		path.Close();
		canvas.DrawPath(path, paint);
	}

	void DrawLights(SKCanvas canvas)
	{
		using var glow = new SKPaint { IsAntialias = true };
		using var core = new SKPaint { IsAntialias = true };
		var t = Frame * 0.22f;

		foreach (var light in Lights)
		{
			var pulse = _hovered ? (MathF.Sin(t + light.Phase) + 1f) * 0.5f : 0.28f;
			var alpha = (byte)(90 + pulse * 165);
			glow.Color = light.Color.WithAlpha((byte)(alpha * 0.38f));
			core.Color = light.Color.WithAlpha(alpha);
			canvas.DrawCircle(light.X, light.Y, 9 + pulse * 5, glow);
			canvas.DrawCircle(light.X, light.Y, 4 + pulse * 2, core);
		}
	}

	void OnPointerEntered(object? sender, SKActorRawEventArgs e)
	{
		_hovered = true;
		Invalidate();
	}

	void OnPointerLeft(object? sender, SKActorRawEventArgs e)
	{
		_hovered = false;
		Invalidate();
	}
}
