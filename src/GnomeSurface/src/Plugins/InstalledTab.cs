
namespace GnomeSurface.Plugins;

class InstalledTab
{
	internal Gtk.Box Tab()
	{
		var box = Gtk.Box.New(Gtk.Orientation.Vertical, 20);
		box.MarginBottom = 20;
		box.MarginTop = 20;
		box.MarginStart = 20;
		box.MarginEnd = 20;
		return box;
	}
}
