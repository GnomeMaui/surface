


using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using GObject;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace GnomeSurfaceCanvas;

[UnsupportedOSPlatform("Windows")]
[UnsupportedOSPlatform("macOS")]
public class SKActor(Clutter.Stage stage, Clutter.Actor parentActor, ILogger<SKActor> logger)
	: ActorBase(stage, parentActor, logger)
{
	Cogl.Context _coglContext => Stage.GetContext().GetBackend().GetCoglContext();
	Clutter.Actor? _actor;
	Cogl.Texture2D? _texture;
	Clutter.Content? _content;
	byte[] _pixels = [];
	SKImageInfo _imageInfo;

	protected virtual int Width => 460;
	protected virtual int Height => 150;
	protected virtual int OffsetX => 18;
	protected virtual int OffsetY => 72;

	public virtual void EnsureVisible()
	{
		EnsureVisible(new MonitorLayout(0, 0, Width, Height));
	}

	public virtual void EnsureVisible(MonitorLayout monitor)
	{
		try
		{
			_actor ??= CreateActor();
			LayoutActor(monitor);

			if (_actor.GetParent() is null)
				ParentActor?.AddChild(_actor);

			_actor.Show();
			EnsureAnimationRunning();

			if (ShellDrawingGate.TryBeginDraw(out var drawScope))
			{
				using (drawScope)
				{
					Render(Width, Height);
					_actor.QueueRedraw();
					Stage.QueueRedraw();
				}
			}
		}
		catch (Exception ex)
		{
			Logger.LogError(ex, "[SKActor] Failed to create SkiaSharp Clutter actor.");
		}
	}

	public virtual void Destroy()
	{
		if (ShellDrawingGate.IsDrawingSuspended)
		{
			ShellDrawingGate.RequestRedraw();
			return;
		}

		Gesture.Detach();
		_actor?.Destroy();
		StopAnimation();
		_actor = null;
		_texture = null;
		_content = null;
		_pixels = [];
	}

	public override void Invalidate()
	{
		if (_actor is null)
			return;

		if (ShellDrawingGate.TryBeginDraw(out var drawScope))
		{
			using (drawScope)
			{
				Render(Width, Height);
				_actor.QueueRedraw();
				Stage.QueueRedraw();
			}
		}
	}

	protected virtual Clutter.Actor CreateActor()
	{
		var actor = Clutter.Actor.New();
		actor.SetName("SKActor");
		actor.SetSize(Width, Height);
		actor.SetContentGravity(Clutter.ContentGravity.ResizeFill);
		actor.SetContentScalingFilters(Clutter.ScalingFilter.Linear, Clutter.ScalingFilter.Linear);
		Gesture.Attach(actor);

		return actor;
	}

	protected virtual void LayoutActor(MonitorLayout monitor)
	{
		_actor?.SetPosition(monitor.X + OffsetX, monitor.Y + OffsetY);
		_actor?.SetSize(Width, Height);
	}

	void Render(int width, int height)
	{
		EnsureTexture(width, height);

		var handle = GCHandle.Alloc(_pixels, GCHandleType.Pinned);
		try
		{
			using var surface = SKSurface.Create(_imageInfo, handle.AddrOfPinnedObject(), _imageInfo.RowBytes);
			if (surface is null)
			{
				return;
			}

			surface.Canvas.Clear(SKColors.Transparent);
			using (new SKAutoCanvasRestore(surface.Canvas, true))
			{
				OnPaintSurface(new SKPaintSurfaceEventArgs(surface, _imageInfo));
			}

			surface.Canvas.Flush();
			UploadTexture();
		}
		finally
		{
			handle.Free();
		}
	}

	void EnsureTexture(int width, int height)
	{
		if (_texture is not null && _imageInfo.Width == width && _imageInfo.Height == height)
			return;

		_imageInfo = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
		_pixels = new byte[_imageInfo.RowBytes * _imageInfo.Height];

		_texture = Cogl.Texture2D.NewWithSize(_coglContext, width, height);
		_texture.SetPremultiplied(true);
		_content = Clutter.TextureContent.NewFromTexture(_texture, null);

		if (_actor is not null)
		{
			_actor.SetContent(_content);
			_actor.SetSize(width, height);
		}
	}

	void UploadTexture()
	{
		if (_texture is null || _pixels.Length == 0)
			return;

		var result = Cogl.Internal.Texture.SetData(
			_texture.Handle.DangerousGetHandle(),
			Cogl.PixelFormat.Bgra8888Pre,
			_imageInfo.RowBytes,
			ref _pixels[0],
			level: 0,
			out var error);

		if (!error.IsInvalid)
			throw new GLib.GException(error);
	}
}
