namespace GnomeSurfaceCanvas.Plugin;

public enum SurfaceLayer
{
	Foundation,
	Ambient,
	Workspace,
	Interactive,
	Overlay
}

public interface IGnomeSurfacePlugin
{
	SurfaceLayer Layer { get; set; }
}
