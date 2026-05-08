using System.Runtime.Versioning;
using Clutter;
using GnomeSurfaceCanvas;
using GnomeSurfaceCanvas.Plugin;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace GnomeSurfaceCanvas;

[UnsupportedOSPlatform("Windows")]
[UnsupportedOSPlatform("macOS")]
public class ActorBase : IGnomeSurfacePlugin
{
	uint _animationSourceId;
	int _frame;

	protected virtual int FrameInterval => 33;
	protected long Frame => _frame;

	protected ILogger<ActorBase> Logger => _logger;
	protected Clutter.Stage Stage => _stage;
	protected Clutter.Actor? ParentActor => _parentActor;
	protected SKGesture Gesture { get; } = new();
	public virtual SurfaceLayer Layer { get; set; } = SurfaceLayer.Workspace;

	readonly Stage _stage;
	Actor? _parentActor;
	readonly ILogger<ActorBase> _logger;

	public ActorBase(Clutter.Stage stage, Clutter.Actor? parentActor, ILogger<ActorBase> logger)
	{
		_stage = stage;
		_parentActor = parentActor;
		_logger = logger;
	}

	public void SetParentActor(Clutter.Actor? parentActor)
	{
		_parentActor = parentActor;
	}

	public event EventHandler<SKPaintSurfaceEventArgs>? PaintSurface;

	protected virtual void OnPaintSurface(SKPaintSurfaceEventArgs e)
	{
		PaintSurface?.Invoke(this, e);
	}

	protected void EnsureAnimationRunning()
	{
		if (_animationSourceId != 0)
			return;

		_animationSourceId = GLib.Functions.TimeoutAdd(
			priority: GLib.Constants.PRIORITY_DEFAULT_IDLE,
			interval: (uint)Math.Max(1, FrameInterval),
			function: new GLib.SourceFunc(() =>
			{
				_frame++;
				Invalidate();
				return GLib.Constants.SOURCE_CONTINUE;
			})
		);
	}

	protected void StopAnimation()
	{
		if (GLib.Functions.SourceRemove(_animationSourceId))
		{
			_animationSourceId = 0;
		}
	}

	public virtual void Invalidate()
	{
	}
}
