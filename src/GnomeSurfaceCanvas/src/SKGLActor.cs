using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace GnomeSurfaceCanvas;

[UnsupportedOSPlatform("Windows")]
[UnsupportedOSPlatform("macOS")]
public class SKGLActor(Clutter.Stage stage, Clutter.Actor parentActor, ILogger<SKGLActor> logger)
	: ActorBase(stage, parentActor, logger)
{
	Cogl.Context _coglContext => Stage.GetContext().GetBackend().GetCoglContext();
	Clutter.Actor? _actor;
	Cogl.Texture2D? _texture;
	Clutter.Content? _content;
	Cogl.Renderer? _renderer;
	GRGlInterface? _glInterface;
	GRContext? _grContext;
	GRBackendRenderTarget? _renderTarget;
	SKSurface? _surface;
	uint _glFramebuffer;
	uint _glTextureId;
	uint _glTextureTarget;
	int _surfaceWidth;
	int _surfaceHeight;
	uint _repaintHandle;
	bool _renderPending;
	bool _contentRendered;
	ulong _renderSequence;

	protected virtual SKColorType ColorType => SKColorType.Rgba8888;
	protected virtual GRSurfaceOrigin SurfaceOrigin => GRSurfaceOrigin.TopLeft;
	protected virtual int Width => 460;
	protected virtual int Height => 150;
	protected virtual int OffsetX => 18;
	protected virtual int OffsetY => 236;

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
			{
				ParentActor?.AddChild(_actor);
			}

			_actor.Show();
			EnsureAnimationRunning();
			if (ShellDrawingGate.TryBeginDraw(out var drawScope))
			{
				using (drawScope)
				{
					EnsureTexture(Width, Height);
					if (!_contentRendered)
						RequestRender();

					_actor.QueueRedraw();
					Stage.QueueRedraw();
				}
			}
			else
			{
				_renderPending = true;
			}
		}
		catch (Exception ex)
		{
			Logger.LogError(ex, "[SKGLActor] Failed to create SkiaSharp GL texture actor.");
		}
	}

	public virtual void Destroy()
	{
		if (ShellDrawingGate.IsDrawingSuspended)
		{
			ShellDrawingGate.RequestRedraw();
			return;
		}

		DisposeSkiaSurface();
		DeleteGlFramebuffer();
		Gesture.Detach();
		_actor?.Destroy();
		StopAnimation();
		_actor = null;
		_texture = null;
		_content = null;
		_contentRendered = false;
	}

	public override void Invalidate()
	{
		if (_actor is null)
			return;

		if (ShellDrawingGate.TryBeginDraw(out var drawScope))
		{
			using (drawScope)
			{
				_contentRendered = false;
				RequestRender();
				_actor.QueueRedraw();
				Stage.QueueRedraw();
			}
		}
		else
		{
			_renderPending = true;
		}
	}

	protected virtual Clutter.Actor CreateActor()
	{
		var actor = Clutter.Actor.New();
		actor.SetName("SKGLActor");
		actor.SetSize(Width, Height);
		actor.SetContentGravity(Clutter.ContentGravity.ResizeFill);
		actor.SetContentScalingFilters(Clutter.ScalingFilter.Linear, Clutter.ScalingFilter.Linear);
		actor.SetReactive(false);
		Gesture.Attach(actor);

		return actor;
	}

	protected virtual void LayoutActor(MonitorLayout monitor)
	{
		_actor?.SetPosition(monitor.X + OffsetX, monitor.Y + OffsetY);
		_actor?.SetSize(Width, Height);
	}

	void RequestRender()
	{
		_renderPending = true;

		if (_repaintHandle != 0)
		{
			return;
		}

		_repaintHandle = Clutter.Functions.ThreadsAddRepaintFunc(Clutter.RepaintFlags.PrePaint, OnPrePaint);
	}

	bool OnPrePaint()
	{
		var renderId = ++_renderSequence;

		try
		{
			if (!_renderPending)
			{
				return false;
			}

			if (!ShellDrawingGate.TryBeginDraw(out var drawScope))
			{
				ShellDrawingGate.RequestRedraw();
				return false;
			}

			using (drawScope)
			{
				RenderTextureOnCurrentGlContext(renderId, Width, Height);
				_renderPending = false;
				_contentRendered = true;
			}
		}
		catch (Exception ex)
		{
			Logger.LogError(ex, "[SKGLActor] pre-paint render #{RenderId} failed.", renderId);
		}
		finally
		{
			_repaintHandle = 0;
		}

		return false;
	}

	void EnsureTexture(int width, int height)
	{
		if (_texture is not null && _texture.GetWidth() == width && _texture.GetHeight() == height)
		{
			return;
		}

		_contentRendered = false;
		DisposeSkiaSurface();
		DeleteGlFramebuffer();

		_texture = Cogl.Texture2D.NewWithFormat(_coglContext, width, height, Cogl.PixelFormat.Rgba8888Pre);
		_texture.SetPremultiplied(true);

		if (!_texture.Allocate())
			Logger.LogWarning("[SKGLActor] Cogl texture Allocate returned false.");

		_content = Clutter.TextureContent.NewFromTexture(_texture, null);

		if (_actor is not null)
		{
			_actor.SetContent(_content);
			_actor.SetSize(width, height);
		}
	}

	void RenderTextureOnCurrentGlContext(ulong renderId, int width, int height)
	{
		EnsureTexture(width, height);

		if (_texture is null)
		{
			Logger.LogWarning("[SKGLActor] render #{RenderId} skipped: Cogl texture is null.", renderId);
			return;
		}

		var currentDisplay = EGL.Functions.EglGetCurrentDisplay();
		var currentContext = EGL.Functions.EglGetCurrentContext();
		var currentDrawSurface = EGL.Functions.EglGetCurrentSurface(EGL.Constants.DRAW);
		var currentReadSurface = EGL.Functions.EglGetCurrentSurface(EGL.Constants.READ);

		if ((nint)currentDisplay == nint.Zero || (nint)currentContext == nint.Zero)
		{
			Logger.LogWarning("[SKGLActor] render #{RenderId} skipped: EGL context is not current.", renderId);
			return;
		}

		_coglContext.Flush();

		_renderer ??= _coglContext.GetRenderer();

		_glInterface ??= GRGlInterface.CreateOpenGl(LoadGlProc);
		if (_glInterface is null)
		{
			Logger.LogWarning("[SKGLActor] render #{RenderId} skipped: GRGlInterface.CreateOpenGl returned null.", renderId);
			return;
		}

		_grContext ??= GRContext.CreateGl(_glInterface);
		if (_grContext is null)
		{
			Logger.LogWarning("[SKGLActor] render #{RenderId} skipped: GRContext.CreateGl returned null.", renderId);
			return;
		}

		var glState = NativeGlState.Capture();
		try
		{
			if (!EnsureSkiaSurfaceForTexture(renderId, width, height))
				return;

			if (_surface is null)
			{
				Logger.LogWarning("[SKGLActor] render #{RenderId} skipped: SKSurface is null after setup.", renderId);
				return;
			}

			_grContext.ResetContext(GRGlBackendState.All);

			var canvas = _surface.Canvas;
			canvas.Clear(SKColors.Transparent);

			using (new SKAutoCanvasRestore(canvas, true))
			{
				OnPaintSurface(new SKPaintSurfaceEventArgs(_surface, new SKImageInfo(width, height, ColorType, SKAlphaType.Premul)));
			}

			_surface.Flush(submit: true, synchronous: false);

			NativeGl.Flush();

			_content?.Invalidate();
			_actor?.QueueRedraw();
		}
		finally
		{
			_grContext?.ResetContext(GRGlBackendState.All);
			glState.Restore();
		}
	}

	bool EnsureSkiaSurfaceForTexture(ulong renderId, int width, int height)
	{
		if (_texture is null || _grContext is null)
			return false;

		if (!_texture.GetGlTexture(out var glTextureId, out var glTextureTarget))
		{
			Logger.LogWarning("[SKGLActor] render #{RenderId} skipped: Cogl texture has no GL texture handle.", renderId);
			return false;
		}

		if (_surface is not null &&
			_renderTarget is not null &&
			_surfaceWidth == width &&
			_surfaceHeight == height &&
			_glTextureId == glTextureId &&
			_glTextureTarget == glTextureTarget &&
			_glFramebuffer != 0)
		{
			return true;
		}

		DisposeSkiaSurface();
		DeleteGlFramebuffer();

		uint[] framebuffers = [0];
		NativeGl.GenFramebuffers(1, framebuffers);
		_glFramebuffer = framebuffers[0];

		NativeGl.BindFramebuffer(NativeGl.Framebuffer, (int)_glFramebuffer);
		NativeGl.FramebufferTexture2D(NativeGl.Framebuffer, NativeGl.ColorAttachment0, (int)glTextureTarget, glTextureId, 0);
		var status = NativeGl.CheckFramebufferStatus(NativeGl.Framebuffer);

		if (status != NativeGl.FramebufferComplete)
		{
			Logger.LogWarning("[SKGLActor] render #{RenderId} skipped: private texture framebuffer is incomplete. status=0x{Status:X}", renderId, status);
			DeleteGlFramebuffer();
			return false;
		}

		var framebufferInfo = new GRGlFramebufferInfo(_glFramebuffer, ColorType.ToGlSizedFormat());
		_renderTarget = new GRBackendRenderTarget(width, height, sampleCount: 0, stencilBits: 0, framebufferInfo);
		_surface = SKSurface.Create(_grContext, _renderTarget, SurfaceOrigin, ColorType);
		_surfaceWidth = width;
		_surfaceHeight = height;
		_glTextureId = glTextureId;
		_glTextureTarget = glTextureTarget;

		if (_surface is null)
		{
			Logger.LogWarning(
				"[SKGLActor] render #{RenderId} skipped: SKSurface.Create returned null. fbo={Framebuffer} texture={TextureId} size={Width}x{Height}",
				renderId,
				_glFramebuffer,
				glTextureId,
				width,
				height);
			DisposeSkiaSurface();
			DeleteGlFramebuffer();
			return false;
		}

		return true;
	}

	void DisposeSkiaSurface()
	{
		_surface?.Dispose();
		_surface = null;
		_renderTarget?.Dispose();
		_renderTarget = null;
		_surfaceWidth = 0;
		_surfaceHeight = 0;
	}

	void DeleteGlFramebuffer()
	{
		if (_glFramebuffer == 0)
			return;

		uint[] framebuffers = [_glFramebuffer];
		NativeGl.DeleteFramebuffers(1, framebuffers);
		_glFramebuffer = 0;
		_glTextureId = 0;
		_glTextureTarget = 0;
	}

	IntPtr LoadGlProc(string name)
	{
		var ptr = _renderer?.GetProcAddress(name) ?? IntPtr.Zero;
		if (ptr != IntPtr.Zero)
			return ptr;

		ptr = EGL.Functions.EglGetProcAddress(name);
		if (ptr != IntPtr.Zero)
			return ptr;

		if (NativeLibrary.TryLoad("libGL.so.1", out var libGl))
		{
			try
			{
				if (NativeLibrary.TryGetExport(libGl, name, out ptr) && ptr != IntPtr.Zero)
					return ptr;
			}
			finally
			{
				NativeLibrary.Free(libGl);
			}
		}

		if (NativeLibrary.TryLoad("libEGL.so.1", out var libEgl))
		{
			try
			{
				if (NativeLibrary.TryGetExport(libEgl, name, out ptr) && ptr != IntPtr.Zero)
					return ptr;
			}
			finally
			{
				NativeLibrary.Free(libEgl);
			}
		}

		Logger.LogWarning("[SKGLActor] LoadGlProc unresolved. name={ProcName}", name);
		return IntPtr.Zero;
	}

	static partial class NativeGl
	{
		public const int CurrentProgram = 0x8B8D;
		public const int ActiveTexture = 0x84E0;
		public const int TextureBinding2D = 0x8069;
		public const int ArrayBufferBinding = 0x8894;
		public const int ElementArrayBufferBinding = 0x8895;
		public const int VertexArrayBinding = 0x85B5;
		public const int FramebufferBinding = 0x8CA6;
		public const int RenderbufferBinding = 0x8CA7;
		public const int Viewport = 0x0BA2;
		public const int ScissorBox = 0x0C10;
		public const int ColorWritemask = 0x0C23;
		public const int Blend = 0x0BE2;
		public const int ScissorTest = 0x0C11;
		public const int DepthTest = 0x0B71;
		public const int StencilTest = 0x0B90;
		public const int CullFace = 0x0B44;
		public const int Framebuffer = 0x8D40;
		public const int Renderbuffer = 0x8D41;
		public const int ColorAttachment0 = 0x8CE0;
		public const int FramebufferComplete = 0x8CD5;

		[DllImport("libGL.so.1", EntryPoint = "glGetIntegerv")]
		public static extern void GetIntegerv(int pname, [Out, MarshalAs(UnmanagedType.LPArray)] int[] data);

		[DllImport("libGL.so.1", EntryPoint = "glIsEnabled")]
		public static extern byte IsEnabled(int cap);

		[DllImport("libGL.so.1", EntryPoint = "glUseProgram")]
		public static extern void UseProgram(int program);

		[DllImport("libGL.so.1", EntryPoint = "glActiveTexture")]
		public static extern void ActiveTextureFunc(int texture);

		[DllImport("libGL.so.1", EntryPoint = "glBindTexture")]
		public static extern void BindTexture(int target, int texture);

		[DllImport("libGL.so.1", EntryPoint = "glBindBuffer")]
		public static extern void BindBuffer(int target, int buffer);

		[DllImport("libGL.so.1", EntryPoint = "glBindVertexArray")]
		public static extern void BindVertexArray(int array);

		[DllImport("libGL.so.1", EntryPoint = "glGenFramebuffers")]
		public static extern void GenFramebuffers(int n, [Out, MarshalAs(UnmanagedType.LPArray)] uint[] framebuffers);

		[DllImport("libGL.so.1", EntryPoint = "glDeleteFramebuffers")]
		public static extern void DeleteFramebuffers(int n, [In, MarshalAs(UnmanagedType.LPArray)] uint[] framebuffers);

		[DllImport("libGL.so.1", EntryPoint = "glBindFramebuffer")]
		public static extern void BindFramebuffer(int target, int framebuffer);

		[DllImport("libGL.so.1", EntryPoint = "glFramebufferTexture2D")]
		public static extern void FramebufferTexture2D(int target, int attachment, int textarget, uint texture, int level);

		[DllImport("libGL.so.1", EntryPoint = "glCheckFramebufferStatus")]
		public static extern uint CheckFramebufferStatus(int target);

		[DllImport("libGL.so.1", EntryPoint = "glBindRenderbuffer")]
		public static extern void BindRenderbuffer(int target, int renderbuffer);

		[DllImport("libGL.so.1", EntryPoint = "glViewport")]
		public static extern void ViewportFunc(int x, int y, int width, int height);

		[DllImport("libGL.so.1", EntryPoint = "glScissor")]
		public static extern void Scissor(int x, int y, int width, int height);

		[DllImport("libGL.so.1", EntryPoint = "glEnable")]
		public static extern void Enable(int cap);

		[DllImport("libGL.so.1", EntryPoint = "glDisable")]
		public static extern void Disable(int cap);

		[DllImport("libGL.so.1", EntryPoint = "glColorMask")]
		public static extern void ColorMask(byte red, byte green, byte blue, byte alpha);

		[DllImport("libGL.so.1", EntryPoint = "glFlush")]
		public static extern void Flush();
	}

	readonly struct NativeGlState
	{
		const int Texture0 = 0x84C0;
		const int Texture2D = 0x0DE1;
		const int ArrayBuffer = 0x8892;
		const int ElementArrayBuffer = 0x8893;

		readonly int _program;
		readonly int _activeTexture;
		readonly int _texture2D;
		readonly int _arrayBuffer;
		readonly int _elementArrayBuffer;
		readonly int _vertexArray;
		readonly int _framebuffer;
		readonly int _renderbuffer;
		readonly int[] _viewport;
		readonly int[] _scissorBox;
		readonly int[] _colorMask;
		readonly bool _blend;
		readonly bool _scissorTest;
		readonly bool _depthTest;
		readonly bool _stencilTest;
		readonly bool _cullFace;

		NativeGlState(
			int program,
			int activeTexture,
			int texture2D,
			int arrayBuffer,
			int elementArrayBuffer,
			int vertexArray,
			int framebuffer,
			int renderbuffer,
			int[] viewport,
			int[] scissorBox,
			int[] colorMask,
			bool blend,
			bool scissorTest,
			bool depthTest,
			bool stencilTest,
			bool cullFace)
		{
			_program = program;
			_activeTexture = activeTexture;
			_texture2D = texture2D;
			_arrayBuffer = arrayBuffer;
			_elementArrayBuffer = elementArrayBuffer;
			_vertexArray = vertexArray;
			_framebuffer = framebuffer;
			_renderbuffer = renderbuffer;
			_viewport = viewport;
			_scissorBox = scissorBox;
			_colorMask = colorMask;
			_blend = blend;
			_scissorTest = scissorTest;
			_depthTest = depthTest;
			_stencilTest = stencilTest;
			_cullFace = cullFace;
		}

		public static NativeGlState Capture()
		{
			return new NativeGlState(
				Get1(NativeGl.CurrentProgram),
				Get1(NativeGl.ActiveTexture),
				Get1(NativeGl.TextureBinding2D),
				Get1(NativeGl.ArrayBufferBinding),
				Get1(NativeGl.ElementArrayBufferBinding),
				Get1(NativeGl.VertexArrayBinding),
				Get1(NativeGl.FramebufferBinding),
				Get1(NativeGl.RenderbufferBinding),
				Get4(NativeGl.Viewport),
				Get4(NativeGl.ScissorBox),
				Get4(NativeGl.ColorWritemask),
				IsEnabled(NativeGl.Blend),
				IsEnabled(NativeGl.ScissorTest),
				IsEnabled(NativeGl.DepthTest),
				IsEnabled(NativeGl.StencilTest),
				IsEnabled(NativeGl.CullFace));
		}

		public void Restore()
		{
			SetEnabled(NativeGl.Blend, _blend);
			SetEnabled(NativeGl.ScissorTest, _scissorTest);
			SetEnabled(NativeGl.DepthTest, _depthTest);
			SetEnabled(NativeGl.StencilTest, _stencilTest);
			SetEnabled(NativeGl.CullFace, _cullFace);

			NativeGl.UseProgram(_program);
			NativeGl.ActiveTextureFunc(_activeTexture == 0 ? Texture0 : _activeTexture);
			NativeGl.BindTexture(Texture2D, _texture2D);
			NativeGl.BindBuffer(ArrayBuffer, _arrayBuffer);
			NativeGl.BindBuffer(ElementArrayBuffer, _elementArrayBuffer);
			NativeGl.BindVertexArray(_vertexArray);
			NativeGl.BindFramebuffer(NativeGl.Framebuffer, _framebuffer);
			NativeGl.BindRenderbuffer(NativeGl.Renderbuffer, _renderbuffer);
			NativeGl.ViewportFunc(_viewport[0], _viewport[1], _viewport[2], _viewport[3]);
			NativeGl.Scissor(_scissorBox[0], _scissorBox[1], _scissorBox[2], _scissorBox[3]);
			NativeGl.ColorMask(ToGlBool(_colorMask[0]), ToGlBool(_colorMask[1]), ToGlBool(_colorMask[2]), ToGlBool(_colorMask[3]));
		}

		static int Get1(int pname)
		{
			int[] value = [0];
			NativeGl.GetIntegerv(pname, value);
			return value[0];
		}

		static int[] Get4(int pname)
		{
			int[] value = [0, 0, 0, 0];
			NativeGl.GetIntegerv(pname, value);
			return value;
		}

		static bool IsEnabled(int cap) => NativeGl.IsEnabled(cap) != 0;

		static void SetEnabled(int cap, bool enabled)
		{
			if (enabled)
				NativeGl.Enable(cap);
			else
				NativeGl.Disable(cap);
		}

		static byte ToGlBool(int value) => value == 0 ? (byte)0 : (byte)1;
	}
}
