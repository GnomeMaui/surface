using System;
using System.Threading;

namespace GnomeSurfaceCanvas;

public static class ShellDrawingGate
{
	static readonly Lock StateLock = new();
	static int _layoutDepth;
	static int _drawDepth;
	static bool _redrawPending;

	public static bool IsDrawingSuspended
	{
		get
		{
			lock (StateLock)
				return _layoutDepth > 0;
		}
	}

	public static ShellLayoutScope BeginLayoutUpdate()
	{
		lock (StateLock)
		{
			_layoutDepth++;
			return new ShellLayoutScope();
		}
	}

	public static bool TryBeginDraw(out ShellDrawScope? scope)
	{
		lock (StateLock)
		{
			if (_layoutDepth > 0)
			{
				_redrawPending = true;
				scope = default;
				return false;
			}

			_drawDepth++;
			scope = new ShellDrawScope();
			return true;
		}
	}

	public static void RequestRedraw()
	{
		lock (StateLock)
			_redrawPending = true;
	}

	static void EndDraw()
	{
		lock (StateLock)
		{
			if (_drawDepth > 0)
				_drawDepth--;
		}
	}

	static bool EndLayoutUpdate()
	{
		lock (StateLock)
		{
			if (_layoutDepth > 0)
				_layoutDepth--;

			if (_layoutDepth > 0 || !_redrawPending)
				return false;

			_redrawPending = false;
			return true;
		}
	}

	public sealed class ShellDrawScope : IDisposable
	{
		bool _disposed;

		public void Dispose()
		{
			if (_disposed)
				return;

			_disposed = true;
			EndDraw();
		}
	}

	public sealed class ShellLayoutScope : IDisposable
	{
		bool _disposed;

		public bool DisposeAndConsumeRedrawPending()
		{
			if (_disposed)
				return false;

			_disposed = true;
			return EndLayoutUpdate();
		}

		public void Dispose() => DisposeAndConsumeRedrawPending();
	}
}
