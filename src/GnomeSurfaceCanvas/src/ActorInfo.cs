using System;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace GnomeSurfaceCanvas;

[UnsupportedOSPlatform("Windows")]
[UnsupportedOSPlatform("macOS")]
public class ActorInfo
{
    public class RenderingInfo(
        string Renderer,
        bool IsHardwareAccelerated,
        bool IsGpu,
        string? Driver,
        string? EglVendor,
        string? EglVersion,
        string? EglExtensions,
        string? ClientApis
    )
    {
        public override string ToString()
        {
            return $"Renderer: {Renderer}, IsHardwareAccelerated: {IsHardwareAccelerated}, IsGpu: {IsGpu}, Driver: {Driver}, EGL Vendor: {EglVendor}, EGL Version: {EglVersion}, EGL Extensions: {EglExtensions}, EGL Client APIs: {ClientApis}";
        }
    }

    readonly ILogger<ActorInfo> _logger;

    public ActorInfo(Clutter.Stage stage, ILogger<ActorInfo> logger)
    {
        _logger = logger;
        var info = GetRenderingInfo(stage);
        _logger.LogInformation($"[SkiaSharpActor] Rendering Info: {info.ToString()}");
    }

    public RenderingInfo GetRenderingInfo(Clutter.Stage stage)
    {
        try
        {
            var context = stage.GetContext();
            var backend = context.GetBackend();
            var coglContext = backend.GetCoglContext();
            var coglDisplay = coglContext.GetDisplay();

            var onscreen = Cogl.Onscreen.NewWithProperties(
            [
                new GObject.ConstructArgument(
                    Cogl.Framebuffer.ContextPropertyDefinition.UnmanagedName,
                    new GObject.Value(coglContext)),
                new GObject.ConstructArgument(
                    Cogl.Framebuffer.WidthPropertyDefinition.UnmanagedName,
                    new GObject.Value(1)),
                new GObject.ConstructArgument(
                    Cogl.Framebuffer.HeightPropertyDefinition.UnmanagedName,
                    new GObject.Value(1)),
            ]);


            var renderer = coglContext.GetRenderer();
            var driver = coglContext.GetDriver();

            // Determine CPU/GPU
            var isHardwareAccelerated = driver.IsHardwareAccelerated();
            var driverId = renderer.GetDriverId();
            var isGpu = driverId is not Cogl.DriverId.Nop and not Cogl.DriverId.Any;
            var driverName = GetDriverName(driverId);

            // Get EGL info if GPU
            string? eglVendor = null;
            string? eglVersion = null;
            string? eglExtensions = null;
            string? clientApis = null;

            if (isGpu)
                TryReadEglInfo(out eglVendor, out eglVersion, out eglExtensions, out clientApis);

            return new RenderingInfo(
                driverId.ToString(),
                isHardwareAccelerated,
                isGpu,
                driverName,
                eglVendor,
                eglVersion,
                eglExtensions,
                clientApis
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SkiaSharpActor] Error getting rendering info");
            throw;
        }
    }

    static string GetDriverName(Cogl.DriverId driverId)
    {
        return driverId switch
        {
            Cogl.DriverId.Nop => "Software Renderer (CPU)",
            Cogl.DriverId.Gl3 => "OpenGL 3.x (GPU)",
            Cogl.DriverId.Gles2 => "OpenGL ES 2.0 (GPU)",
            Cogl.DriverId.Any => "Auto-detect",
            _ => "Unknown"
        };
    }

    void TryReadEglInfo(
        out string? eglVendor,
        out string? eglVersion,
        out string? eglExtensions,
        out string? clientApis)
    {
        eglVendor = null;
        eglVersion = null;
        eglExtensions = null;
        clientApis = null;

        try
        {
            var eglDisplay = EGL.Functions.EglGetCurrentDisplay();
            if ((IntPtr)eglDisplay == IntPtr.Zero)
            {
                _logger.LogInformation("[SkiaSharpActor] EGL current display is not available; trying EGL default display.");
                eglDisplay = EGL.Functions.EglGetDisplay(IntPtr.Zero);
            }

            if ((IntPtr)eglDisplay == IntPtr.Zero)
            {
                _logger.LogInformation("[SkiaSharpActor] EGL display is not available. error=0x{EglError:X}", EGL.Functions.EglGetError());
                return;
            }

            eglVendor = QueryEglStringOrNull(eglDisplay, EGL.Constants.VENDOR, "VENDOR");
            eglVersion = QueryEglStringOrNull(eglDisplay, EGL.Constants.VERSION, "VERSION");
            eglExtensions = QueryEglStringOrNull(eglDisplay, EGL.Constants.EXTENSIONS, "EXTENSIONS");
            clientApis = QueryEglStringOrNull(eglDisplay, EGL.Constants.CLIENT_APIS, "CLIENT_APIS");
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "[SkiaSharpActor] EGL info query failed; continuing without EGL details.");
        }
    }

    string? QueryEglStringOrNull(EGL.EGLDisplay eglDisplay, EGL.EGLint name, string label)
    {
        var value = EGL.Internal.Functions.EglQueryString(eglDisplay, name);
        if (value.IsInvalid)
        {
            _logger.LogInformation("[SkiaSharpActor] EGL string is not available. name={EglStringName} error=0x{EglError:X}", label, EGL.Functions.EglGetError());
            return null;
        }

        return value.ConvertToString();
    }
}
