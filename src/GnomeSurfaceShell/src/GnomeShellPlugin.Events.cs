using GnomeSurfaceCanvas;

namespace GnomeSurfaceShell;

public partial class GnomeShellPlugin
{
	static internal ThemeDetector ThemeDetector = new ThemeDetector();
	static internal ThemeInfo CurrentTheme = ThemeDetector.Current;
}
