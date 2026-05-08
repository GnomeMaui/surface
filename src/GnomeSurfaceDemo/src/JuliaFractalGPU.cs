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
public class JuliaFractalGPU : SKGLActor
{
	const int MinIterations = 10;
	const int MaxIterations = 1000;
	const float MinZoom = 0.1f;
	const float MaxZoom = 10.0f;

	static string GenerateShaderSource(int maxIterations, float maxZoom) => $@"
uniform vec2 u_resolution;
uniform float u_time;
uniform float u_zoom;
uniform float u_maxIterations;
uniform vec2 u_julia_c;

vec3 palette(float t) {{
    vec3 a = vec3(0.5, 0.5, 0.5);
    vec3 b = vec3(0.5, 0.5, 0.5);
    vec3 c = vec3(1.0, 1.0, 1.0);
    vec3 d = vec3(0.263, 0.416, 0.557);
    return a + b * cos(6.28318 * (c * t + d));
}}

vec4 main(vec2 fragCoord) {{
    vec2 uv = (fragCoord - u_resolution * 0.5) / min(u_resolution.x, u_resolution.y);
    uv *= u_zoom;
    vec2 z = uv;
    vec2 c = u_julia_c;
    float iterations = 0.0;

    for (float i = 0.0; i < {maxIterations}.0; i++) {{
        if (i >= u_maxIterations) break;
        float x = z.x * z.x - z.y * z.y + c.x;
        float y = 2.0 * z.x * z.y + c.y;
        z = vec2(x, y);
        if (length(z) > 4.0) {{
            iterations = i;
            break;
        }}
    }}

    float t = iterations / u_maxIterations;
    vec3 color = palette(t + u_time * 0.1);
    return vec4(color, 1.0);
}}";

	SKRuntimeEffect? _juliaEffect;
	readonly SKFont _statFont = new(SKTypeface.FromFamilyName("monospace"), 12);
	readonly SKPaint _statPaint = new() { IsAntialias = false };
	float _zoom = 2.0f;
	int _currentIterations = 140;
	public override SurfaceLayer Layer { get; set; } = SurfaceLayer.Ambient;

	protected override int Width => 520;
	protected override int Height => 300;
	protected override int OffsetX => 876;
	protected override int OffsetY => 72;

	double Time => Frame * 0.02;
	double CurrentReal => -0.7 + 0.3 * Math.Sin(Time * 0.7);
	double CurrentImaginary => 0.27 + 0.3 * Math.Cos(Time * 0.5);

	public JuliaFractalGPU(Clutter.Stage stage, Clutter.Actor parent, ILogger<SKGLActor> logger)
		: base(stage, parent, logger)
	{
		Initialize();
	}

	void Initialize()
	{
		_juliaEffect = SKRuntimeEffect.CreateShader(GenerateShaderSource(MaxIterations, MaxZoom), out _);
		Gesture.Scrolled += OnScrolled;
		Gesture.PanUpdated += OnPanUpdated;
	}

	public override void Destroy()
	{
		_juliaEffect?.Dispose();
		_statFont.Dispose();
		_statPaint.Dispose();
		base.Destroy();
	}

	protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
	{
		var canvas = e.Surface.Canvas;
		canvas.Clear(SKColors.Black);

		if (_juliaEffect is null)
			return;

		var width = e.Info.Width;
		var height = e.Info.Height;
		var uniforms = new SKRuntimeEffectUniforms(_juliaEffect)
		{
			["u_resolution"] = new float[] { width, height },
			["u_time"] = (float)Time,
			["u_zoom"] = _zoom,
			["u_maxIterations"] = (float)_currentIterations,
			["u_julia_c"] = new float[] { (float)CurrentReal, (float)CurrentImaginary }
		};

		using var shader = _juliaEffect.ToShader(uniforms);
		using var paint = new SKPaint { Shader = shader };
		canvas.DrawRect(0, 0, width, height, paint);

		_statPaint.Color = SKColors.Black;
		canvas.DrawText($"Julia GPU | scroll/pan zoom: {_zoom:F2} [{MinZoom}-{MaxZoom}] | iter: {_currentIterations}",
			10, 20, _statFont, _statPaint);

		base.OnPaintSurface(e);
	}

	void OnScrolled(object? sender, SKActorRawEventArgs e)
	{
		var dy = e.ScrollDirection switch
		{
			Clutter.ScrollDirection.Up => -1.0,
			Clutter.ScrollDirection.Down => 1.0,
			_ => e.ScrollDeltaY
		};

		if (Math.Abs(dy) < double.Epsilon)
			return;

		_zoom = Math.Clamp(_zoom * (dy > 0 ? 1.1f : 0.9f), MinZoom, MaxZoom);
		e.Handled = true;
		Invalidate();
	}

	void OnPanUpdated(object? sender, SKActorPanEventArgs e)
	{
		if (Math.Abs(e.AccumulatedDeltaY) < 1)
			return;

		_zoom = Math.Clamp(2.0f + e.AccumulatedDeltaY * 0.01f, MinZoom, MaxZoom);
		Invalidate();
	}
}
