using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using GnomeSurfaceCanvas;
using GnomeSurfaceCanvas.Plugin;
using GnomeSurfaceCanvas.Services;
using GnomeSurfaceShell.PluginLoading;
using GnomeSurfaceShell.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GnomeSurfaceShell;

[UnsupportedOSPlatform("Windows")]
[UnsupportedOSPlatform("macOS")]
public partial class GnomeShellPlugin : Meta.Plugin, GObject.GTypeProvider, GObject.InstanceFactory
{
	static readonly GObject.Internal.ClassInitFunc ClassInit = OnClassInit;
	static readonly GObject.Internal.InstanceInitFunc InstanceInit = static (_, _) => { };
	static readonly Meta.Internal.PluginClassData.StartCallback StartCallback = OnStart;
	static readonly Meta.Internal.PluginClassData.MinimizeCallback MinimizeCallback = OnMinimize;
	static readonly Meta.Internal.PluginClassData.UnminimizeCallback UnminimizeCallback = OnUnminimize;
	static readonly Meta.Internal.PluginClassData.SizeChangedCallback SizeChangedCallback = OnSizeChanged;
	static readonly Meta.Internal.PluginClassData.SizeChangeCallback SizeChangeCallback = OnSizeChange;
	static readonly Meta.Internal.PluginClassData.MapCallback MapCallback = OnMap;
	static readonly Meta.Internal.PluginClassData.DestroyCallback DestroyCallback = OnDestroy;
	static readonly Meta.Internal.PluginClassData.SwitchWorkspaceCallback SwitchWorkspaceCallback = OnSwitchWorkspace;
	static readonly Meta.Internal.PluginClassData.ShowTilePreviewCallback ShowTilePreviewCallback = OnShowTilePreview;
	static readonly Meta.Internal.PluginClassData.HideTilePreviewCallback HideTilePreviewCallback = OnHideTilePreview;
	static readonly Meta.Internal.PluginClassData.ShowWindowMenuCallback ShowWindowMenuCallback = OnShowWindowMenu;
	static readonly Meta.Internal.PluginClassData.ShowWindowMenuForRectCallback ShowWindowMenuForRectCallback = OnShowWindowMenuForRect;
	static readonly Meta.Internal.PluginClassData.KillWindowEffectsCallback KillWindowEffectsCallback = OnKillWindowEffects;
	static readonly Meta.Internal.PluginClassData.KillSwitchWorkspaceCallback KillSwitchWorkspaceCallback = OnKillSwitchWorkspace;
	static readonly Meta.Internal.PluginClassData.KeybindingFilterCallback KeybindingFilterCallback = OnKeybindingFilter;
	static readonly Meta.Internal.PluginClassData.ConfirmDisplayChangeCallback ConfirmDisplayChangeCallback = OnConfirmDisplayChange;
	static readonly Meta.Internal.PluginClassData.CreateCloseDialogCallback CreateCloseDialogCallback = OnCreateCloseDialog;
	static readonly Meta.Internal.PluginClassData.CreateInhibitShortcutsDialogCallback CreateInhibitShortcutsDialogCallback = OnCreateInhibitShortcutsDialog;
	static readonly Meta.Internal.PluginClassData.LocatePointerCallback LocatePointerCallback = OnLocatePointer;

	static readonly GObject.Type RegisteredType = RegisterType();
	static IServiceProvider _services = default!;
	static ILogger<GnomeShellPlugin>? _logger;
	static ShellRuntimeState? _runtimeState;
	static Clutter.Actor? _shellUiGroup;
	static readonly Dictionary<SurfaceLayer, Clutter.Actor> _surfaceLayers = [];
	static Meta.MonitorManager? _monitorManager;
	static bool _monitorSignalsConnected;
	static IReadOnlyList<Type>? _surfacePluginTypes;
	static readonly Dictionary<Type, List<SKActor>> _skiaPlugins = [];
	static readonly Dictionary<Type, List<SKGLActor>> _skiaGlPlugins = [];
	static ActorInfo? _actorInfo;
	static readonly object EffectStateLock = new();
	static readonly HashSet<nint> MinimizePending = [];
	static readonly HashSet<nint> UnminimizePending = [];
	static readonly HashSet<nint> MapPending = [];
	static readonly HashSet<nint> DestroyPending = [];
	static readonly HashSet<nint> SizeChangePending = [];
	static bool _switchWorkspacePending;

	protected internal GnomeShellPlugin(Meta.Internal.PluginHandle handle)
		: base(handle)
	{
		_logger = _services.GetRequiredService<ILogger<GnomeShellPlugin>>();
		_logger.LogInformation("GnomeShellPlugin instance created.");
	}

	public static GObject.Type GetGType(IServiceProvider services)
	{
		ArgumentNullException.ThrowIfNull(services, nameof(services));
		_services = services;
		_runtimeState = services.GetRequiredService<ShellRuntimeState>();
		return RegisteredType;
	}

	public static object Create(IntPtr handle, bool ownsHandle)
	{
		var plugin = new GnomeShellPlugin(new Meta.Internal.PluginHandle(handle));
		GObject.Internal.InstanceCache.AddToggleRef(plugin);
		return plugin;
	}

	static void OnClassInit(IntPtr gClass, IntPtr classData)
	{
		var pluginClass = Marshal.PtrToStructure<Meta.Internal.PluginClassData>(gClass);
		pluginClass.Start = StartCallback;
		pluginClass.Minimize = MinimizeCallback;
		pluginClass.Unminimize = UnminimizeCallback;
		pluginClass.SizeChanged = SizeChangedCallback;
		pluginClass.SizeChange = SizeChangeCallback;
		pluginClass.Map = MapCallback;
		pluginClass.Destroy = DestroyCallback;
		pluginClass.SwitchWorkspace = SwitchWorkspaceCallback;
		pluginClass.ShowTilePreview = ShowTilePreviewCallback;
		pluginClass.HideTilePreview = HideTilePreviewCallback;
		pluginClass.ShowWindowMenu = ShowWindowMenuCallback;
		pluginClass.ShowWindowMenuForRect = ShowWindowMenuForRectCallback;
		pluginClass.KillWindowEffects = KillWindowEffectsCallback;
		pluginClass.KillSwitchWorkspace = KillSwitchWorkspaceCallback;
		pluginClass.KeybindingFilter = KeybindingFilterCallback;
		pluginClass.ConfirmDisplayChange = ConfirmDisplayChangeCallback;
		pluginClass.CreateCloseDialog = CreateCloseDialogCallback;
		pluginClass.CreateInhibitShortcutsDialog = CreateInhibitShortcutsDialogCallback;
		pluginClass.LocatePointer = LocatePointerCallback;
		Marshal.StructureToPtr(pluginClass, gClass, false);
	}

	static void OnStart(IntPtr pluginPtr)
	{
		// /home/czirok/Dev/gnomesurface/gnomesurface/src/plugin_helye.txt

		try
		{
			_logger?.LogInformation("[Plugin.Start] Initializing stage and setting background color #68217a.");

			var plugin = (Meta.Plugin)GObject.Internal.InstanceWrapper.WrapHandle<Meta.Plugin>(pluginPtr, false);
			var display = plugin.GetDisplay();
			var compositor = display.GetCompositor();
			var stage = compositor.GetStage();

			RegisterMonitorChangeHandler(display, compositor, stage);
			ApplyShellLayout(display, compositor, stage);

			stage.Show();
			stage.QueueRedraw();
			_runtimeState?.SetShellReady(true);
			_logger?.LogInformation("[Plugin.Start] Stage is visible.");
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "[Plugin.Start] Failed while setting up stage.");
			throw;
		}
	}

	static void RegisterMonitorChangeHandler(Meta.Display display, Meta.Compositor compositor, Clutter.Stage stage)
	{
		if (_monitorSignalsConnected)
			return;

		try
		{
			_monitorManager = display.GetContext().GetBackend().GetMonitorManager();
			_monitorManager.OnMonitorsChanged += (_, _) =>
			{
				_logger?.LogInformation("[Plugin.MonitorsChanged] Mutter monitor configuration changed.");
				ApplyShellLayout(display, compositor, stage);
			};

			_monitorSignalsConnected = true;
			_logger?.LogInformation("[Plugin.Start] Connected Mutter monitors-changed handler.");
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "[Plugin.Start] Failed to connect Mutter monitors-changed handler.");
		}
	}

	static void ApplyShellLayout(Meta.Display display, Meta.Compositor compositor, Clutter.Stage stage)
	{
		Clutter.Actor? shellUiGroup = null;
		IReadOnlyList<MonitorLayout> monitorLayouts = [];
		var layoutScope = ShellDrawingGate.BeginLayoutUpdate();
		bool redrawPending;

		try
		{
			UpdateDisplayConfiguration(display, stage);
			shellUiGroup = EnsureShellUiGroup(compositor, stage);

			if (Cogl.Color.FromString(out var gircolor, "#68217a"))
			{
				stage.SetBackgroundColor(gircolor);
				_logger?.LogInformation("[Plugin.Layout] Requested stage background color: #68217a.");
			}
			else
			{
				_logger?.LogWarning("[Plugin.Layout] Failed to parse color #68217a, stage background unchanged.");
			}

			monitorLayouts = GetMonitorLayouts();
			EnsureSurfacePluginActors(stage, monitorLayouts);
			_actorInfo ??= new ActorInfo(stage, _services.GetRequiredService<ILogger<ActorInfo>>());

			ShellDrawingGate.RequestRedraw();
		}
		finally
		{
			redrawPending = layoutScope.DisposeAndConsumeRedrawPending();
		}

		if (redrawPending && shellUiGroup is not null)
		{
			_logger?.LogInformation("[Plugin.Layout] Flushing pending shell redraw after layout update.");
			EnsureSurfacePluginActors(stage, monitorLayouts);
			shellUiGroup.QueueRedraw();
			stage.QueueRedraw();
		}
	}

	static Clutter.Actor EnsureShellUiGroup(Meta.Compositor compositor, Clutter.Stage stage)
	{
		var width = 0;
		var height = 0;
		var snapshot = _runtimeState?.GetDisplayConfiguration();
		if (snapshot is not null)
		{
			foreach (var monitor in snapshot.Monitors)
			{
				width = Math.Max(width, monitor.X + monitor.Width);
				height = Math.Max(height, monitor.Y + monitor.Height);
			}
		}

		if (width <= 0 || height <= 0)
		{
			stage.GetSize(out var stageWidth, out var stageHeight);
			width = stageWidth > 0 ? (int)stageWidth : DisplayMonitorState.Fallback.Width;
			height = stageHeight > 0 ? (int)stageHeight : DisplayMonitorState.Fallback.Height;
			_logger?.LogWarning(
				"[Plugin.Start] Shell UI group using fallback stage size={Width}x{Height}.",
				width,
				height);
		}

		_shellUiGroup ??= Clutter.Actor.New();
		_shellUiGroup.SetName("GnomeSurfaceShellUiGroup");
		_shellUiGroup.SetPosition(0, 0);
		_shellUiGroup.SetSize(width, height);
		_shellUiGroup.SetClip(0, 0, width, height);
		_shellUiGroup.SetClipToAllocation(true);
		_shellUiGroup.Show();
		EnsureShellLayers(_shellUiGroup, width, height);

		var windowGroup = compositor.GetWindowGroup();
		if (_shellUiGroup.GetParent() is null)
		{
			stage.InsertChildBelow(_shellUiGroup, windowGroup);
			_logger?.LogInformation(
				"[Plugin.Start] Shell UI group inserted below Mutter window_group. size={Width}x{Height}",
				width,
				height);
		}
		else
		{
			stage.SetChildBelowSibling(_shellUiGroup, windowGroup);
			_logger?.LogInformation(
				"[Plugin.Start] Shell UI group moved below Mutter window_group. size={Width}x{Height}",
				width,
				height);
		}

		_shellUiGroup.QueueRedraw();
		stage.QueueRedraw();
		return _shellUiGroup;
	}

	static void EnsureShellLayers(Clutter.Actor shellUiGroup, int width, int height)
	{
		EnsureShellLayer(shellUiGroup, SurfaceLayer.Foundation, width, height, 0);
		EnsureShellLayer(shellUiGroup, SurfaceLayer.Ambient, width, height, 1);
		EnsureShellLayer(shellUiGroup, SurfaceLayer.Workspace, width, height, 2);
		EnsureShellLayer(shellUiGroup, SurfaceLayer.Interactive, width, height, 3);
		EnsureShellLayer(shellUiGroup, SurfaceLayer.Overlay, width, height, 4);
		ArrangeShellLayers(shellUiGroup);
	}

	static Clutter.Actor EnsureShellLayer(Clutter.Actor shellUiGroup, SurfaceLayer surfaceLayer, int width, int height, int index)
	{
		if (!_surfaceLayers.TryGetValue(surfaceLayer, out var layer))
		{
			layer = Clutter.Actor.New();
			_surfaceLayers.Add(surfaceLayer, layer);
		}

		layer.SetName($"GnomeSurface{surfaceLayer}Layer");
		layer.SetPosition(0, 0);
		layer.SetSize(width, height);
		layer.SetClip(0, 0, width, height);
		layer.SetClipToAllocation(true);
		layer.SetReactive(false);
		layer.Show();

		if (layer.GetParent() is null)
			shellUiGroup.InsertChildAtIndex(layer, index);

		layer.QueueRedraw();
		return layer;
	}

	static void ArrangeShellLayers(Clutter.Actor shellUiGroup)
	{
		shellUiGroup.SetChildBelowSibling(GetSurfaceLayer(SurfaceLayer.Foundation), GetSurfaceLayer(SurfaceLayer.Ambient));
		shellUiGroup.SetChildBelowSibling(GetSurfaceLayer(SurfaceLayer.Ambient), GetSurfaceLayer(SurfaceLayer.Workspace));
		shellUiGroup.SetChildBelowSibling(GetSurfaceLayer(SurfaceLayer.Workspace), GetSurfaceLayer(SurfaceLayer.Interactive));
		shellUiGroup.SetChildBelowSibling(GetSurfaceLayer(SurfaceLayer.Interactive), GetSurfaceLayer(SurfaceLayer.Overlay));
	}

	static Clutter.Actor GetSurfaceLayer(SurfaceLayer layer)
	{
		return _surfaceLayers.TryGetValue(layer, out var actor)
			? actor
			: throw new InvalidOperationException($"Surface layer {layer} is not initialized.");
	}

	static void EnsureSurfacePluginActors(Clutter.Stage stage, IReadOnlyList<MonitorLayout> monitorLayouts)
	{
		try
		{
			var pluginTypes = GetSurfacePluginTypes();
			foreach (var pluginType in pluginTypes.Where(type => typeof(SKActor).IsAssignableFrom(type)))
				EnsureSkiaPluginActors(pluginType, stage, monitorLayouts);

			foreach (var pluginType in pluginTypes.Where(type => typeof(SKGLActor).IsAssignableFrom(type)))
				EnsureSkiaGlPluginActors(pluginType, stage, monitorLayouts);

			_logger?.LogInformation("[Plugin.Layout] Surface plugin actors updated. plugins={PluginCount} monitors={MonitorCount}", pluginTypes.Count, monitorLayouts.Count);
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "[Plugin.Layout] Failed to update surface plugin actors.");
		}
	}

	static void EnsureSkiaPluginActors(Type pluginType, Clutter.Stage stage, IReadOnlyList<MonitorLayout> monitorLayouts)
	{
		var actors = GetOrCreateActorList(_skiaPlugins, pluginType);
		while (actors.Count > monitorLayouts.Count && !ShellDrawingGate.IsDrawingSuspended)
		{
			var actor = actors[^1];
			actors.RemoveAt(actors.Count - 1);
			actor.Destroy();
		}

		for (var i = 0; i < monitorLayouts.Count; i++)
		{
			if (i >= actors.Count)
			{
				var actor = CreateSurfacePlugin<SKActor>(pluginType, stage, GetSurfaceLayer(SurfaceLayer.Workspace));
				if (actor is null)
					return;

				actor.SetParentActor(GetSurfaceLayer(actor.Layer));
				actors.Add(actor);
				_logger?.LogInformation(
					"[Plugin.Layout] CPU Skia plugin actor created. plugin={PluginType} layer={Layer} monitorIndex={MonitorIndex}",
					pluginType.FullName,
					actor.Layer,
					i);
			}

			actors[i].EnsureVisible(monitorLayouts[i]);
		}
	}

	static void EnsureSkiaGlPluginActors(Type pluginType, Clutter.Stage stage, IReadOnlyList<MonitorLayout> monitorLayouts)
	{
		var actors = GetOrCreateActorList(_skiaGlPlugins, pluginType);
		while (actors.Count > 1 && !ShellDrawingGate.IsDrawingSuspended)
		{
			var actor = actors[^1];
			actors.RemoveAt(actors.Count - 1);
			actor.Destroy();
		}

		if (monitorLayouts.Count != 1)
		{
			ShellDrawingGate.RequestRedraw();
			_logger?.LogInformation(
				"[Plugin.Layout] GPU Skia plugin {PluginType} deferred until monitor layout is stable. monitors={MonitorCount}",
				pluginType.FullName,
				monitorLayouts.Count);
			return;
		}

		if (actors.Count == 0)
		{
			var actor = CreateSurfacePlugin<SKGLActor>(pluginType, stage, GetSurfaceLayer(SurfaceLayer.Workspace));
			if (actor is null)
				return;

			actor.SetParentActor(GetSurfaceLayer(actor.Layer));
			actors.Add(actor);
		}

		actors[0].EnsureVisible(monitorLayouts[0]);
	}

	static IReadOnlyList<Type> GetSurfacePluginTypes()
	{
		_surfacePluginTypes ??= _services
			.GetRequiredService<GnomeSurfacePluginLoader>()
			.DiscoverPluginTypes()
			.ToArray();

		return _surfacePluginTypes;
	}

	static List<TActor> GetOrCreateActorList<TActor>(Dictionary<Type, List<TActor>> actorsByPluginType, Type pluginType)
	{
		if (!actorsByPluginType.TryGetValue(pluginType, out var actors))
		{
			actors = [];
			actorsByPluginType.Add(pluginType, actors);
		}

		return actors;
	}

	static TActor? CreateSurfacePlugin<TActor>(Type pluginType, Clutter.Stage stage, Clutter.Actor shellUiGroup)
		where TActor : class
	{
		try
		{
			var instance = ActivatorUtilities.CreateInstance(_services, pluginType, stage, shellUiGroup);
			if (instance is TActor actor)
				return actor;

			_logger?.LogError(
				"[Plugin.Layout] Failed to create surface plugin actor. plugin={PluginType} expectedActor={ExpectedType} actualType={ActualType}",
				pluginType.FullName,
				typeof(TActor).FullName,
				instance.GetType().FullName);
		}
		catch (Exception ex)
		{
			_logger?.LogError(
				ex,
				"[Plugin.Layout] Surface plugin constructor failed. plugin={PluginType} expectedActor={ExpectedType}",
				pluginType.FullName,
				typeof(TActor).FullName);
		}

		return null;
	}

	static IReadOnlyList<MonitorLayout> GetMonitorLayouts()
	{
		var snapshot = _runtimeState?.GetDisplayConfiguration();
		if (snapshot is null || snapshot.Monitors.Length == 0)
			return [new MonitorLayout(0, 0, 1400, 54)];

		var layouts = new List<MonitorLayout>(snapshot.Monitors.Length);
		foreach (var monitor in snapshot.Monitors)
		{
			layouts.Add(new MonitorLayout(
				monitor.X,
				monitor.Y,
				Math.Max(320, monitor.Width),
				Math.Max(54, monitor.Height)));
		}

		return layouts;
	}

	static void UpdateDisplayConfiguration(Meta.Display display, Clutter.Stage stage)
	{
		try
		{
			var monitorCount = display.GetNMonitors();
			display.GetSize(out var displayWidth, out var displayHeight);
			_logger?.LogInformation(
				"[Plugin.Start] Display snapshot source=Meta.Display monitors={MonitorCount} size={Width}x{Height}.",
				monitorCount,
				displayWidth,
				displayHeight);

			var monitors = ReadMetaMonitorStates(display);
			for (var i = 0; i < monitors.Length; i++)
			{
				_logger?.LogInformation(
					"[Plugin.Start] Monitor snapshot index={Index} connector={Connector} vendor={Vendor} product={Product} serial={Serial} geometry={X},{Y} {Width}x{Height} scale={Scale} primary={Primary}.",
					monitors[i].Index,
					monitors[i].Connector,
					monitors[i].Vendor,
					monitors[i].Product,
					monitors[i].Serial,
					monitors[i].X,
					monitors[i].Y,
					monitors[i].Width,
					monitors[i].Height,
					monitors[i].Scale,
					monitors[i].IsPrimary);
			}

			if (monitors.Length == 0)
			{
				stage.GetSize(out var stageWidth, out var stageHeight);
				_logger?.LogWarning(
					"[Plugin.Start] Meta.Display returned no monitors; using stage fallback size={Width}x{Height}.",
					stageWidth,
					stageHeight);

				monitors =
				[
					DisplayMonitorState.Fallback with
					{
							Width = stageWidth > 0 ? (int) stageWidth : DisplayMonitorState.Fallback.Width,
							Height = stageHeight > 0 ? (int) stageHeight : DisplayMonitorState.Fallback.Height
						}
					];
			}

			var snapshot = _runtimeState?.UpdateDisplayConfiguration(monitors);
			if (snapshot is not null)
			{
				_logger?.LogInformation(
					"[Plugin.Start] Runtime display snapshot updated. serial={Serial} monitors={MonitorCount}.",
					snapshot.Serial,
					snapshot.Monitors.Length);
			}
			else
			{
				_logger?.LogWarning("[Plugin.Start] Runtime state is unavailable; display snapshot was not stored.");
			}
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "[Plugin.Start] Failed to update display configuration snapshot.");
		}
	}

	static DisplayMonitorState[] ReadMetaMonitorStates(Meta.Display display)
	{
		var monitorManager = display.GetContext().GetBackend().GetMonitorManager();
		var logicalMonitors = monitorManager.GetLogicalMonitors();
		if (logicalMonitors is not null)
		{
			var logicalCount = (int)GLib.List.Length(logicalMonitors);
			var displayMonitorCount = display.GetNMonitors();
			var monitors = new List<DisplayMonitorState>(Math.Max(logicalCount, 0));

			for (uint i = 0; i < logicalCount; i++)
			{
				var logicalPtr = GLib.List.NthData(logicalMonitors, i);
				if (logicalPtr == IntPtr.Zero)
					continue;

				var logicalMonitor = Meta.LogicalMonitor.NewFromPointer(logicalPtr, ownsHandle: false);
				var logicalIndex = logicalMonitor.GetNumber();
				if (logicalIndex < 0 || logicalIndex >= displayMonitorCount)
					logicalIndex = (int)i;

				var geometry = GetMonitorGeometryOrFallback(display, logicalIndex);
				var scale = logicalIndex >= 0 && logicalIndex < displayMonitorCount
					? display.GetMonitorScale(logicalIndex)
					: 1.0;
				var physicalMonitors = logicalMonitor.GetMonitors();
				var physicalCount = (int)GLib.List.Length(physicalMonitors);
				for (uint physicalIndex = 0; physicalIndex < physicalCount; physicalIndex++)
				{
					var monitorPtr = GLib.List.NthData(physicalMonitors, physicalIndex);
					if (monitorPtr == IntPtr.Zero)
						continue;

					var monitor = Meta.Monitor.NewFromPointer(monitorPtr, ownsHandle: false);
					monitors.Add(new DisplayMonitorState(
						Index: logicalIndex,
						Connector: monitor.GetConnector(),
						Vendor: monitor.GetVendor(),
						Product: monitor.GetProduct(),
						Serial: monitor.GetSerial(),
						X: geometry.X,
						Y: geometry.Y,
						Width: geometry.Width > 0 ? geometry.Width : DisplayMonitorState.Fallback.Width,
						Height: geometry.Height > 0 ? geometry.Height : DisplayMonitorState.Fallback.Height,
						Scale: scale > 0 ? scale : 1.0,
						RefreshRate: 60.0,
						IsPrimary: monitor.IsPrimary()));
				}
			}

			if (monitors.Count > 0)
				return [.. monitors];
		}

		return ReadPhysicalMonitorFallback(display, monitorManager);
	}

	static Mtk.Rectangle GetMonitorGeometryOrFallback(Meta.Display display, int logicalIndex)
	{
		if (logicalIndex >= 0 && logicalIndex < display.GetNMonitors())
		{
			display.GetMonitorGeometry(logicalIndex, out var geometry);
			return geometry;
		}

		return new Mtk.Rectangle
		{
			X = DisplayMonitorState.Fallback.X,
			Y = DisplayMonitorState.Fallback.Y,
			Width = DisplayMonitorState.Fallback.Width,
			Height = DisplayMonitorState.Fallback.Height
		};
	}

	static DisplayMonitorState[] ReadPhysicalMonitorFallback(Meta.Display display, Meta.MonitorManager monitorManager)
	{
		var metaMonitors = monitorManager.GetMonitors();
		if (metaMonitors is null)
			return [];

		var count = Math.Min((int)GLib.List.Length(metaMonitors), display.GetNMonitors());
		var monitors = new List<DisplayMonitorState>(Math.Max(count, 0));
		for (uint i = 0; i < count; i++)
		{
			var monitorPtr = GLib.List.NthData(metaMonitors, i);
			if (monitorPtr == IntPtr.Zero)
				continue;

			var monitor = Meta.Monitor.NewFromPointer(monitorPtr, ownsHandle: false);
			display.GetMonitorGeometry((int)i, out var geometry);
			var scale = display.GetMonitorScale((int)i);
			monitors.Add(new DisplayMonitorState(
				Index: (int)i,
				Connector: monitor.GetConnector(),
				Vendor: monitor.GetVendor(),
				Product: monitor.GetProduct(),
				Serial: monitor.GetSerial(),
				X: geometry.X,
				Y: geometry.Y,
				Width: geometry.Width > 0 ? geometry.Width : DisplayMonitorState.Fallback.Width,
				Height: geometry.Height > 0 ? geometry.Height : DisplayMonitorState.Fallback.Height,
				Scale: scale > 0 ? scale : 1.0,
				RefreshRate: 60.0,
				IsPrimary: monitor.IsPrimary()));
		}

		return [.. monitors];
	}

	static void OnMinimize(IntPtr pluginPtr, IntPtr actorPtr)
	{
		BeginEffect(MinimizePending, actorPtr);

		var plugin = (Meta.Plugin)GObject.Internal.InstanceWrapper.WrapHandle<Meta.Plugin>(pluginPtr, false);
		var actor = (Meta.WindowActor)GObject.Internal.InstanceWrapper.WrapHandle<Meta.WindowActor>(actorPtr, false);
		CompleteEffect(MinimizePending, actorPtr, () => plugin.MinimizeCompleted(actor));
	}

	static void OnUnminimize(IntPtr pluginPtr, IntPtr actorPtr)
	{
		BeginEffect(UnminimizePending, actorPtr);

		var plugin = (Meta.Plugin)GObject.Internal.InstanceWrapper.WrapHandle<Meta.Plugin>(pluginPtr, false);
		var actor = (Meta.WindowActor)GObject.Internal.InstanceWrapper.WrapHandle<Meta.WindowActor>(actorPtr, false);
		CompleteEffect(UnminimizePending, actorPtr, () => plugin.UnminimizeCompleted(actor));
	}

	static void OnSizeChanged(IntPtr pluginPtr, IntPtr actorPtr)
	{
		_logger?.LogDebug("[Plugin.SizeChanged] Window size-changed event.");
	}

	static void OnSizeChange(IntPtr pluginPtr, IntPtr actorPtr, Meta.SizeChange whichChange, IntPtr oldFrameRectPtr, IntPtr oldBufferRectPtr)
	{
		BeginEffect(SizeChangePending, actorPtr);

		var plugin = (Meta.Plugin)GObject.Internal.InstanceWrapper.WrapHandle<Meta.Plugin>(pluginPtr, false);
		var actor = (Meta.WindowActor)GObject.Internal.InstanceWrapper.WrapHandle<Meta.WindowActor>(actorPtr, false);
		CompleteEffect(SizeChangePending, actorPtr, () => plugin.SizeChangeCompleted(actor));
	}

	static void OnMap(IntPtr pluginPtr, IntPtr actorPtr)
	{
		BeginEffect(MapPending, actorPtr);

		var plugin = (Meta.Plugin)GObject.Internal.InstanceWrapper.WrapHandle<Meta.Plugin>(pluginPtr, false);
		var actor = (Meta.WindowActor)GObject.Internal.InstanceWrapper.WrapHandle<Meta.WindowActor>(actorPtr, false);
		MoveWindowToRemoteMonitorIfNeeded(actor);
		UpdateWindowSnapshot(actor, "Map");
		CompleteEffect(MapPending, actorPtr, () => plugin.MapCompleted(actor));
	}

	static void OnDestroy(IntPtr pluginPtr, IntPtr actorPtr)
	{
		BeginEffect(DestroyPending, actorPtr);

		var plugin = (Meta.Plugin)GObject.Internal.InstanceWrapper.WrapHandle<Meta.Plugin>(pluginPtr, false);
		var actor = (Meta.WindowActor)GObject.Internal.InstanceWrapper.WrapHandle<Meta.WindowActor>(actorPtr, false);
		RemoveWindowSnapshot(actor, "Destroy");
		CompleteEffect(DestroyPending, actorPtr, () => plugin.DestroyCompleted(actor));
	}

	static void OnSwitchWorkspace(IntPtr pluginPtr, int from, int to, Meta.MotionDirection direction)
	{
		lock (EffectStateLock)
		{
			_switchWorkspacePending = true;
		}

		var plugin = (Meta.Plugin)GObject.Internal.InstanceWrapper.WrapHandle<Meta.Plugin>(pluginPtr, false);
		plugin.SwitchWorkspaceCompleted();

		lock (EffectStateLock)
		{
			_switchWorkspacePending = false;
		}
	}

	static void OnShowTilePreview(IntPtr pluginPtr, IntPtr windowPtr, IntPtr tileRectPtr, int tileMonitorNumber)
	{
		_logger?.LogDebug("[Plugin.ShowTilePreview] monitor={Monitor}", tileMonitorNumber);
	}

	static void OnHideTilePreview(IntPtr pluginPtr)
	{
		_logger?.LogDebug("[Plugin.HideTilePreview]");
	}

	static void OnShowWindowMenu(IntPtr pluginPtr, IntPtr windowPtr, Meta.WindowMenuType menu, int x, int y)
	{
		_logger?.LogDebug("[Plugin.ShowWindowMenu] type={MenuType} at {X},{Y}", menu, x, y);
	}

	static void OnShowWindowMenuForRect(IntPtr pluginPtr, IntPtr windowPtr, Meta.WindowMenuType menu, IntPtr rectPtr)
	{
		_logger?.LogDebug("[Plugin.ShowWindowMenuForRect] type={MenuType}", menu);
	}

	static void OnKillWindowEffects(IntPtr pluginPtr, IntPtr actorPtr)
	{
		var plugin = (Meta.Plugin)GObject.Internal.InstanceWrapper.WrapHandle<Meta.Plugin>(pluginPtr, false);
		var actor = (Meta.WindowActor)GObject.Internal.InstanceWrapper.WrapHandle<Meta.WindowActor>(actorPtr, false);

		CompleteIfPending(MinimizePending, actorPtr, () => plugin.MinimizeCompleted(actor));
		CompleteIfPending(UnminimizePending, actorPtr, () => plugin.UnminimizeCompleted(actor));
		CompleteIfPending(MapPending, actorPtr, () => plugin.MapCompleted(actor));
		CompleteIfPending(DestroyPending, actorPtr, () => plugin.DestroyCompleted(actor));
		CompleteIfPending(SizeChangePending, actorPtr, () => plugin.SizeChangeCompleted(actor));
	}

	static void OnKillSwitchWorkspace(IntPtr pluginPtr)
	{
		var plugin = (Meta.Plugin)GObject.Internal.InstanceWrapper.WrapHandle<Meta.Plugin>(pluginPtr, false);

		bool shouldComplete;
		lock (EffectStateLock)
		{
			shouldComplete = _switchWorkspacePending;
			_switchWorkspacePending = false;
		}

		if (shouldComplete)
			plugin.SwitchWorkspaceCompleted();
	}

	static bool OnKeybindingFilter(IntPtr pluginPtr, IntPtr bindingPtr)
	{
		return false;
	}

	static void OnConfirmDisplayChange(IntPtr pluginPtr)
	{
		var plugin = (Meta.Plugin)GObject.Internal.InstanceWrapper.WrapHandle<Meta.Plugin>(pluginPtr, false);
		plugin.CompleteDisplayChange(true);
	}

	static IntPtr OnCreateCloseDialog(IntPtr pluginPtr, IntPtr windowPtr)
	{
		return IntPtr.Zero;
	}

	static IntPtr OnCreateInhibitShortcutsDialog(IntPtr pluginPtr, IntPtr windowPtr)
	{
		return IntPtr.Zero;
	}

	static void OnLocatePointer(IntPtr pluginPtr)
	{
		_logger?.LogDebug("[Plugin.LocatePointer]");
	}

	static GObject.Type RegisterType()
	{
		GObject.Functions.TypeQuery(Meta.Plugin.GetGType(), out var query);

		using var typeName = GLib.Internal.NonNullableUtf8StringOwnedHandle.Create("DotnetGnomeShellPlugin");
		var typeId = GObject.Internal.Functions.TypeRegisterStaticSimple(
			Meta.Plugin.GetGType(),
			typeName,
			query.Handle.GetClassSize(),
			ClassInit,
			query.Handle.GetInstanceSize(),
			InstanceInit,
			GObject.TypeFlags.None);

		if (typeId == 0)
			throw new InvalidOperationException("Failed to register DotnetGnomeShellPlugin GType.");

		var type = new GObject.Type(typeId);
		GObject.Internal.DynamicInstanceFactory.Register(type, Create);
		return type;
	}

	static void UpdateWindowSnapshot(Meta.WindowActor actor, string source)
	{
		try
		{
			var window = actor.GetMetaWindow();
			if (window is null)
			{
				_logger?.LogWarning("[Plugin.{Source}] WindowActor has no Meta.Window.", source);
				return;
			}

			if (!IsEligibleForIntrospection(window))
			{
				_logger?.LogInformation(
					"[Plugin.{Source}] Window skipped for introspection. id={WindowId} type={WindowType} override_redirect={OverrideRedirect}",
					source,
					window.GetId(),
					window.GetWindowType(),
					window.IsOverrideRedirect());
				return;
			}

			window.GetFrameRect(out var rect);
			var monitor = window.GetMonitor();
			var appId = GetAppId(window);
			var snapshot = new WindowIntrospectionState(
				Id: window.GetId(),
				AppId: appId,
				Title: NullIfEmpty(window.GetTitle()),
				WmClass: NullIfEmpty(window.GetWmClass()),
				ClientType: (uint)window.GetClientType(),
				IsHidden: window.IsHidden(),
				HasFocus: window.HasFocus(),
				Width: rect.Width > 0 ? (uint)rect.Width : 0,
				Height: rect.Height > 0 ? (uint)rect.Height : 0,
				SandboxedAppId: NullIfEmpty(window.GetSandboxedAppId()));

			_runtimeState?.UpsertWindow(snapshot);
			_logger?.LogInformation(
				"[Plugin.{Source}] Window introspection snapshot updated. id={WindowId} app_id={AppId} title={Title} geometry={X},{Y} {Width}x{Height} monitor={Monitor} focus={HasFocus}",
				source,
				snapshot.Id,
				snapshot.AppId,
				snapshot.Title ?? "<none>",
				rect.X,
				rect.Y,
				snapshot.Width,
				snapshot.Height,
				monitor,
				snapshot.HasFocus);
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "[Plugin.{Source}] Failed to update window introspection snapshot.", source);
		}
	}

	static void MoveWindowToRemoteMonitorIfNeeded(Meta.WindowActor actor)
	{
		try
		{
			var window = actor.GetMetaWindow();
			if (window is null || !IsEligibleForIntrospection(window))
				return;

			var targetMonitor = GetRemoteMonitorIndex();
			if (targetMonitor is null)
				return;

			var currentMonitor = window.GetMonitor();
			if (currentMonitor == targetMonitor.Value)
				return;

			window.MoveToMonitor(targetMonitor.Value);
			_logger?.LogInformation(
				"[Plugin.Map] Window moved to RDP monitor. id={WindowId} app_id={AppId} from_monitor={CurrentMonitor} to_monitor={TargetMonitor}",
				window.GetId(),
				GetAppId(window),
				currentMonitor,
				targetMonitor.Value);
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "[Plugin.Map] Failed to move window to RDP monitor.");
		}
	}

	static int? GetRemoteMonitorIndex()
	{
		var snapshot = _runtimeState?.GetDisplayConfiguration();
		if (snapshot is null)
			return null;

		foreach (var monitor in snapshot.Monitors)
		{
			if (string.Equals(monitor.Product, "Virtual remote monitor", StringComparison.Ordinal))
				return monitor.Index;
		}

		return null;
	}

	static void RemoveWindowSnapshot(Meta.WindowActor actor, string source)
	{
		try
		{
			var window = actor.GetMetaWindow();
			if (window is null)
			{
				_logger?.LogWarning("[Plugin.{Source}] Destroyed WindowActor has no Meta.Window.", source);
				return;
			}

			var id = window.GetId();
			_runtimeState?.RemoveWindow(id);
			_logger?.LogInformation("[Plugin.{Source}] Window introspection snapshot removed. id={WindowId}", source, id);
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "[Plugin.{Source}] Failed to remove window introspection snapshot.", source);
		}
	}

	static bool IsEligibleForIntrospection(Meta.Window window)
	{
		if (window.IsOverrideRedirect())
			return false;

		return window.GetWindowType() is Meta.WindowType.Normal
			or Meta.WindowType.Dialog
			or Meta.WindowType.ModalDialog
			or Meta.WindowType.Utility;
	}

	static string GetAppId(Meta.Window window)
	{
		return NullIfEmpty(window.GetGtkApplicationId())
			?? NullIfEmpty(window.GetSandboxedAppId())
			?? NullIfEmpty(window.GetWmClass())
			?? $"pid-{window.GetPid()}";
	}

	static string? NullIfEmpty(string? value) =>
		string.IsNullOrWhiteSpace(value) ? null : value;

	static void BeginEffect(HashSet<nint> pendingSet, nint actorPtr)
	{
		if (actorPtr == nint.Zero)
			return;

		lock (EffectStateLock)
		{
			pendingSet.Add(actorPtr);
		}
	}

	static void CompleteEffect(HashSet<nint> pendingSet, nint actorPtr, Action complete)
	{
		try
		{
			complete();
		}
		finally
		{
			if (actorPtr != nint.Zero)
			{
				lock (EffectStateLock)
				{
					pendingSet.Remove(actorPtr);
				}
			}
		}
	}

	static void CompleteIfPending(HashSet<nint> pendingSet, nint actorPtr, Action complete)
	{
		if (actorPtr == nint.Zero)
			return;

		bool shouldComplete;
		lock (EffectStateLock)
		{
			shouldComplete = pendingSet.Remove(actorPtr);
		}

		if (shouldComplete)
			complete();
	}

}
