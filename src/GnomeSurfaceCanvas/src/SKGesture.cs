using System;
using System.Runtime.Versioning;

namespace GnomeSurfaceCanvas;

[UnsupportedOSPlatform("Windows")]
[UnsupportedOSPlatform("macOS")]
public sealed class SKGesture
{
	const string ClickGestureName = "SKActor click gesture";
	const string LongPressGestureName = "SKActor long press gesture";
	const string PanGestureName = "SKActor pan gesture";

	Clutter.Actor? _actor;
	Clutter.ClickGesture? _clickGesture;
	Clutter.LongPressGesture? _longPressGesture;
	Clutter.PanGesture? _panGesture;
	bool _pointerPressed;

	public event EventHandler<SKActorRawEventArgs>? Entered;
	public event EventHandler<SKActorRawEventArgs>? Left;
	public event EventHandler<SKActorRawEventArgs>? ButtonPressed;
	public event EventHandler<SKActorRawEventArgs>? ButtonReleased;
	public event EventHandler<SKActorRawEventArgs>? Moved;
	public event EventHandler<SKActorRawEventArgs>? Touched;
	public event EventHandler<SKActorRawEventArgs>? Scrolled;
	public event EventHandler<SKActorPointerEventArgs>? Clicked;
	public event EventHandler<SKActorPointerEventArgs>? LongPressed;
	public event EventHandler<SKActorPointerEventArgs>? PressedChanged;
	public event EventHandler<SKActorPanEventArgs>? PanStarted;
	public event EventHandler<SKActorPanEventArgs>? PanUpdated;
	public event EventHandler<SKActorPanEventArgs>? PanEnded;
	public event EventHandler<SKActorPanEventArgs>? PanCancelled;

	public bool IsPointerPressed => _pointerPressed;

	public void Attach(Clutter.Actor actor)
	{
		if (_actor == actor)
			return;

		Detach();

		_actor = actor;
		InitializeActorEvents(actor);
		InitializeClickGesture(actor);
		InitializeLongPressGesture(actor);
		InitializePanGesture(actor);
		actor.SetReactive(true);
	}

	public void Detach()
	{
		ClearClickGesture();
		ClearLongPressGesture();
		ClearPanGesture();
		ClearActorEvents();
		_actor = null;
		_pointerPressed = false;
	}

	void InitializeActorEvents(Clutter.Actor actor)
	{
		actor.OnEnterEvent += OnActorEnterEvent;
		actor.OnLeaveEvent += OnActorLeaveEvent;
		actor.OnButtonPressEvent += OnActorButtonPressEvent;
		actor.OnButtonReleaseEvent += OnActorButtonReleaseEvent;
		actor.OnMotionEvent += OnActorMotionEvent;
		actor.OnTouchEvent += OnActorTouchEvent;
		actor.OnScrollEvent += OnActorScrollEvent;
	}

	void ClearActorEvents()
	{
		if (_actor is null)
			return;

		_actor.OnEnterEvent -= OnActorEnterEvent;
		_actor.OnLeaveEvent -= OnActorLeaveEvent;
		_actor.OnButtonPressEvent -= OnActorButtonPressEvent;
		_actor.OnButtonReleaseEvent -= OnActorButtonReleaseEvent;
		_actor.OnMotionEvent -= OnActorMotionEvent;
		_actor.OnTouchEvent -= OnActorTouchEvent;
		_actor.OnScrollEvent -= OnActorScrollEvent;
	}

	bool OnActorEnterEvent(Clutter.Actor sender, Clutter.Actor.EnterEventSignalArgs args)
	{
		return RaiseRawEvent(SKActorRawEventKind.Enter, args.Event, Entered);
	}

	bool OnActorLeaveEvent(Clutter.Actor sender, Clutter.Actor.LeaveEventSignalArgs args)
	{
		return RaiseRawEvent(SKActorRawEventKind.Leave, args.Event, Left);
	}

	bool OnActorButtonPressEvent(Clutter.Actor sender, Clutter.Actor.ButtonPressEventSignalArgs args)
	{
		return RaiseRawEvent(SKActorRawEventKind.ButtonPress, args.Event, ButtonPressed);
	}

	bool OnActorButtonReleaseEvent(Clutter.Actor sender, Clutter.Actor.ButtonReleaseEventSignalArgs args)
	{
		return RaiseRawEvent(SKActorRawEventKind.ButtonRelease, args.Event, ButtonReleased);
	}

	bool OnActorMotionEvent(Clutter.Actor sender, Clutter.Actor.MotionEventSignalArgs args)
	{
		return RaiseRawEvent(SKActorRawEventKind.Motion, args.Event, Moved);
	}

	bool OnActorTouchEvent(Clutter.Actor sender, Clutter.Actor.TouchEventSignalArgs args)
	{
		return RaiseRawEvent(SKActorRawEventKind.Touch, args.Event, Touched);
	}

	bool OnActorScrollEvent(Clutter.Actor sender, Clutter.Actor.ScrollEventSignalArgs args)
	{
		return RaiseRawEvent(SKActorRawEventKind.Scroll, args.Event, Scrolled);
	}

	bool RaiseRawEvent(SKActorRawEventKind kind, Clutter.Event clutterEvent, EventHandler<SKActorRawEventArgs>? handler)
	{
		if (handler is null)
			return false;

		var args = SKActorRawEventArgs.Create(kind, clutterEvent);
		handler(this, args);
		return args.Handled;
	}

	void InitializeClickGesture(Clutter.Actor actor)
	{
		_clickGesture = Clutter.ClickGesture.New();
		_clickGesture.SetName(ClickGestureName);
		_clickGesture.SetNClicksRequired(1);
		_clickGesture.SetRecognizeOnPress(false);
		_clickGesture.SetCancelThreshold(-1);
		_clickGesture.OnRecognize += OnClickGestureRecognize;
		_clickGesture.OnNotify += OnPressGestureNotify;

		actor.AddAction(_clickGesture);
	}

	void ClearClickGesture()
	{
		if (_clickGesture is null)
			return;

		_clickGesture.OnRecognize -= OnClickGestureRecognize;
		_clickGesture.OnNotify -= OnPressGestureNotify;
		_clickGesture.Cancel();

		_actor?.RemoveAction(_clickGesture);
		_clickGesture = null;
	}

	void InitializeLongPressGesture(Clutter.Actor actor)
	{
		_longPressGesture = Clutter.LongPressGesture.New();
		_longPressGesture.SetName(LongPressGestureName);
		_longPressGesture.OnRecognize += OnLongPressGestureRecognize;
		_longPressGesture.OnNotify += OnPressGestureNotify;

		actor.AddAction(_longPressGesture);
	}

	void ClearLongPressGesture()
	{
		if (_longPressGesture is null)
			return;

		_longPressGesture.OnRecognize -= OnLongPressGestureRecognize;
		_longPressGesture.OnNotify -= OnPressGestureNotify;
		_longPressGesture.Cancel();

		_actor?.RemoveAction(_longPressGesture);
		_longPressGesture = null;
	}

	void InitializePanGesture(Clutter.Actor actor)
	{
		_panGesture = Clutter.PanGesture.New();
		_panGesture.SetName(PanGestureName);
		_panGesture.SetPanAxis(Clutter.PanAxis.Both);
		_panGesture.SetMinNPoints(1);
		_panGesture.SetMaxNPoints(0);
		_panGesture.SetRequiredButton(0);
		_panGesture.OnRecognize += OnPanGestureRecognize;
		_panGesture.OnPanUpdate += OnPanGestureUpdate;
		_panGesture.OnEnd += OnPanGestureEnd;
		_panGesture.OnCancel += OnPanGestureCancel;

		actor.AddAction(_panGesture);
	}

	void ClearPanGesture()
	{
		if (_panGesture is null)
			return;

		_panGesture.OnRecognize -= OnPanGestureRecognize;
		_panGesture.OnPanUpdate -= OnPanGestureUpdate;
		_panGesture.OnEnd -= OnPanGestureEnd;
		_panGesture.OnCancel -= OnPanGestureCancel;
		_panGesture.Cancel();

		_actor?.RemoveAction(_panGesture);
		_panGesture = null;
	}

	void OnClickGestureRecognize(Clutter.Gesture sender, EventArgs args)
	{
		var pressGesture = sender as Clutter.PressGesture ?? _clickGesture;
		if (pressGesture is null)
			return;

		Clicked?.Invoke(this, CreatePointerEventArgs(pressGesture, pressed: false));
	}

	void OnLongPressGestureRecognize(Clutter.Gesture sender, EventArgs args)
	{
		var pressGesture = sender as Clutter.PressGesture ?? _longPressGesture;
		if (pressGesture is null)
			return;

		LongPressed?.Invoke(this, CreatePointerEventArgs(pressGesture, pressed: pressGesture.GetPressed()));
	}

	void OnPanGestureRecognize(Clutter.Gesture sender, EventArgs args)
	{
		var panGesture = sender as Clutter.PanGesture ?? _panGesture;
		if (panGesture is null)
			return;

		PanStarted?.Invoke(this, CreatePanEventArgs(panGesture));
	}

	void OnPanGestureUpdate(Clutter.PanGesture sender, EventArgs args)
	{
		PanUpdated?.Invoke(this, CreatePanEventArgs(sender));
	}

	void OnPanGestureEnd(Clutter.Gesture sender, EventArgs args)
	{
		var panGesture = sender as Clutter.PanGesture ?? _panGesture;
		if (panGesture is null)
			return;

		PanEnded?.Invoke(this, CreatePanEventArgs(panGesture));
	}

	void OnPanGestureCancel(Clutter.Gesture sender, EventArgs args)
	{
		var panGesture = sender as Clutter.PanGesture ?? _panGesture;
		if (panGesture is null)
			return;

		PanCancelled?.Invoke(this, CreatePanEventArgs(panGesture));
	}

	void OnPressGestureNotify(GObject.Object sender, GObject.Object.NotifySignalArgs args)
	{
		if (args.Pspec.GetName() != "pressed")
			return;

		var pressGesture = sender as Clutter.PressGesture;
		if (pressGesture is null)
			return;

		var pressed = pressGesture.GetPressed();
		if (_pointerPressed == pressed)
			return;

		_pointerPressed = pressed;
		PressedChanged?.Invoke(this, CreatePointerEventArgs(pressGesture, pressed));
	}

	static SKActorPointerEventArgs CreatePointerEventArgs(Clutter.PressGesture pressGesture, bool pressed)
	{
		pressGesture.GetCoords(out var coords);
		pressGesture.GetCoordsAbs(out var absoluteCoords);

		return new SKActorPointerEventArgs(
			button: pressGesture.GetButton(),
			presses: pressGesture.GetNPresses(),
			x: coords.X,
			y: coords.Y,
			absoluteX: absoluteCoords.X,
			absoluteY: absoluteCoords.Y,
			pressed: pressed);
	}

	static SKActorPanEventArgs CreatePanEventArgs(Clutter.PanGesture panGesture)
	{
		panGesture.GetBeginCentroid(out var beginCentroid);
		panGesture.GetBeginCentroidAbs(out var beginCentroidAbs);
		panGesture.GetCentroid(out var centroid);
		panGesture.GetCentroidAbs(out var centroidAbs);
		panGesture.GetDelta(out var delta);
		panGesture.GetDeltaAbs(out var deltaAbs);
		panGesture.GetAccumulatedDelta(out var accumulatedDelta);
		panGesture.GetAccumulatedDeltaAbs(out var accumulatedDeltaAbs);
		panGesture.GetVelocity(out var velocity);
		panGesture.GetVelocityAbs(out var velocityAbs);

		return new SKActorPanEventArgs(
			button: panGesture.GetButton(),
			nPoints: panGesture.GetNPoints(),
			beginX: beginCentroid.X,
			beginY: beginCentroid.Y,
			beginAbsoluteX: beginCentroidAbs.X,
			beginAbsoluteY: beginCentroidAbs.Y,
			x: centroid.X,
			y: centroid.Y,
			absoluteX: centroidAbs.X,
			absoluteY: centroidAbs.Y,
			deltaX: delta.GetX(),
			deltaY: delta.GetY(),
			absoluteDeltaX: deltaAbs.GetX(),
			absoluteDeltaY: deltaAbs.GetY(),
			accumulatedDeltaX: accumulatedDelta.GetX(),
			accumulatedDeltaY: accumulatedDelta.GetY(),
			absoluteAccumulatedDeltaX: accumulatedDeltaAbs.GetX(),
			absoluteAccumulatedDeltaY: accumulatedDeltaAbs.GetY(),
			velocityX: velocity.GetX(),
			velocityY: velocity.GetY(),
			absoluteVelocityX: velocityAbs.GetX(),
			absoluteVelocityY: velocityAbs.GetY(),
			gestureState: ((Clutter.Gesture)panGesture).GetState(),
			modifierState: panGesture.GetState());
	}
}

public sealed class SKActorPointerEventArgs(
	uint button,
	uint presses,
	float x,
	float y,
	float absoluteX,
	float absoluteY,
	bool pressed) : EventArgs
{
	public uint Button { get; } = button;
	public uint Presses { get; } = presses;
	public float X { get; } = x;
	public float Y { get; } = y;
	public float AbsoluteX { get; } = absoluteX;
	public float AbsoluteY { get; } = absoluteY;
	public bool Pressed { get; } = pressed;
}

[UnsupportedOSPlatform("Windows")]
[UnsupportedOSPlatform("macOS")]
public sealed class SKActorRawEventArgs : EventArgs
{
	SKActorRawEventArgs(
		SKActorRawEventKind kind,
		Clutter.Event clutterEvent,
		Clutter.EventType eventType,
		float x,
		float y,
		uint button,
		Clutter.ScrollDirection scrollDirection,
		double scrollDeltaX,
		double scrollDeltaY)
	{
		Kind = kind;
		Event = clutterEvent;
		EventType = eventType;
		X = x;
		Y = y;
		Button = button;
		ScrollDirection = scrollDirection;
		ScrollDeltaX = scrollDeltaX;
		ScrollDeltaY = scrollDeltaY;
	}

	public SKActorRawEventKind Kind { get; }
	public Clutter.Event Event { get; }
	public Clutter.EventType EventType { get; }
	public float X { get; }
	public float Y { get; }
	public uint Button { get; }
	public Clutter.ScrollDirection ScrollDirection { get; }
	public double ScrollDeltaX { get; }
	public double ScrollDeltaY { get; }
	public bool Handled { get; set; }

	internal static SKActorRawEventArgs Create(SKActorRawEventKind kind, Clutter.Event clutterEvent)
	{
		var handle = clutterEvent.Handle;
		var eventType = Clutter.Internal.Event.Type(handle);

		Clutter.Internal.Event.GetCoords(handle, out var x, out var y);
		var button = Clutter.Internal.Event.GetButton(handle);
		var scrollDirection = eventType == Clutter.EventType.Scroll
			? Clutter.Internal.Event.GetScrollDirection(handle)
			: default;

		var scrollDeltaX = 0.0;
		var scrollDeltaY = 0.0;
		if (eventType == Clutter.EventType.Scroll)
			Clutter.Internal.Event.GetScrollDelta(handle, out scrollDeltaX, out scrollDeltaY);

		return new SKActorRawEventArgs(
			kind,
			clutterEvent,
			eventType,
			x,
			y,
			button,
			scrollDirection,
			scrollDeltaX,
			scrollDeltaY);
	}
}

public enum SKActorRawEventKind
{
	Enter,
	Leave,
	ButtonPress,
	ButtonRelease,
	Motion,
	Touch,
	Scroll
}

public sealed class SKActorPanEventArgs(
	uint button,
	uint nPoints,
	float beginX,
	float beginY,
	float beginAbsoluteX,
	float beginAbsoluteY,
	float x,
	float y,
	float absoluteX,
	float absoluteY,
	float deltaX,
	float deltaY,
	float absoluteDeltaX,
	float absoluteDeltaY,
	float accumulatedDeltaX,
	float accumulatedDeltaY,
	float absoluteAccumulatedDeltaX,
	float absoluteAccumulatedDeltaY,
	float velocityX,
	float velocityY,
	float absoluteVelocityX,
	float absoluteVelocityY,
	Clutter.GestureState gestureState,
	Clutter.ModifierType modifierState) : EventArgs
{
	public uint Button { get; } = button;
	public uint NPoints { get; } = nPoints;
	public float BeginX { get; } = beginX;
	public float BeginY { get; } = beginY;
	public float BeginAbsoluteX { get; } = beginAbsoluteX;
	public float BeginAbsoluteY { get; } = beginAbsoluteY;
	public float X { get; } = x;
	public float Y { get; } = y;
	public float AbsoluteX { get; } = absoluteX;
	public float AbsoluteY { get; } = absoluteY;
	public float DeltaX { get; } = deltaX;
	public float DeltaY { get; } = deltaY;
	public float AbsoluteDeltaX { get; } = absoluteDeltaX;
	public float AbsoluteDeltaY { get; } = absoluteDeltaY;
	public float AccumulatedDeltaX { get; } = accumulatedDeltaX;
	public float AccumulatedDeltaY { get; } = accumulatedDeltaY;
	public float AbsoluteAccumulatedDeltaX { get; } = absoluteAccumulatedDeltaX;
	public float AbsoluteAccumulatedDeltaY { get; } = absoluteAccumulatedDeltaY;
	public float VelocityX { get; } = velocityX;
	public float VelocityY { get; } = velocityY;
	public float AbsoluteVelocityX { get; } = absoluteVelocityX;
	public float AbsoluteVelocityY { get; } = absoluteVelocityY;
	public Clutter.GestureState GestureState { get; } = gestureState;
	public Clutter.ModifierType ModifierState { get; } = modifierState;
}
