using System.Globalization;
using EasyUIBinding.GirCore;
using GnomeSurface.Plugins;
using Microsoft.Extensions.Localization;

namespace GnomeSurface;

[GObject.Subclass<Adw.ApplicationWindow>]
public partial class MainWindow : IDisposable
{
	IStringLocalizer<I18N> _localizer = default!;
	InstalledTab _installedTab = default!;
	SearchTab _searchTab = default!;
	bool _isConfigured;

	InstalledTab InstalledTab => _installedTab ?? throw new InvalidOperationException("MainWindow has not been configured.");
	SearchTab SearchTab => _searchTab ?? throw new InvalidOperationException("MainWindow has not been configured.");
	IStringLocalizer<I18N> L => _localizer ?? throw new InvalidOperationException("MainWindow has not been configured.");

	partial void Initialize()
	{
		SetDefaultSize(1280, 720);
	}

	internal static MainWindow CreateWithProperties(InstalledTab installedTab, SearchTab searchTab, IStringLocalizer<I18N> localizer)
	{
		ArgumentNullException.ThrowIfNull(installedTab);
		ArgumentNullException.ThrowIfNull(searchTab);
		ArgumentNullException.ThrowIfNull(localizer);

		var instance = NewWithProperties([]);
		instance.Configure(installedTab, searchTab, localizer);
		return instance;
	}

	void Configure(InstalledTab installedTab, SearchTab searchTab, IStringLocalizer<I18N> localizer)
	{
		if (_isConfigured)
			throw new InvalidOperationException("MainWindow is already configured.");

		_localizer = localizer;
		_installedTab = installedTab;
		_searchTab = searchTab;
		_isConfigured = true;

		SetTitle(L["GNOME Surface Extensions"]);

		var viewSwitcher = Adw.ViewSwitcher.New();

		viewSwitcher.Stack = Adw.ViewStack.New();
		viewSwitcher.Stack.Vexpand = true;
		viewSwitcher.Stack.Hexpand = true;
		viewSwitcher.Policy = Adw.ViewSwitcherPolicy.Wide;

		var headerBar = Adw.HeaderBar.New();
		headerBar.SetTitleWidget(viewSwitcher);

		var toolbarView = Adw.ToolbarView.New();
		toolbarView.AddTopBar(headerBar);
		toolbarView.Content = viewSwitcher.Stack;

		viewSwitcher.Stack.AddTitledWithIcon(
			InstalledTab.Tab(),
			Guid.NewGuid().ToString(),
			"Installed",
			"system-run-symbolic"
		);

		viewSwitcher.Stack.AddTitledWithIcon(
			SearchTab.Tab(),
			Guid.NewGuid().ToString(),
			"Search",
			"system-search-symbolic"
		);

		Content = toolbarView;
	}

	void OnLanguageChanged()
	{
		if (CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft)
		{
			Gtk.Widget.SetDefaultDirection(Gtk.TextDirection.Rtl);
		}
		else
		{
			Gtk.Widget.SetDefaultDirection(Gtk.TextDirection.Ltr);
		}
		SetTitle(L["Yaml Localization"]);
	}

	public override void Dispose()
	{
		base.Dispose();
		GC.SuppressFinalize(this);
	}
}
