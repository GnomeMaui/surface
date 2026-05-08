using System;

namespace GnomeSurfaceCanvas;

/// <summary>
/// Encapsulates the current GNOME desktop theme and accent color settings.
/// </summary>
public class ThemeInfo
{
	/// <summary>
	/// The current application theme preference.
	/// Can be overridden programmatically via <see cref="ThemeDetector.SetTheme"/>.
	/// </summary>
	public AppTheme Theme { get; set; }

	/// <summary>
	/// <see langword="true"/> if the current theme preference is dark.
	/// </summary>
	public bool IsDark => Theme == AppTheme.Dark;

	/// <summary>
	/// <see langword="true"/> if the current theme preference is not dark.
	/// </summary>
	public bool IsLight => !IsDark;

	/// <summary>
	/// The user-selected GNOME accent color.
	/// </summary>
	public GnomeAccentColor AccentColor { get; set; }

	/// <summary>
	/// The raw GSettings value for the accent color.
	/// </summary>
	public string AccentColorName { get; set; } = "blue";
}

/// <summary>
/// Defines the application theme preference as exposed by org.gnome.desktop.interface color-scheme.
/// </summary>
public enum AppTheme
{
	Unspecified,
	Light,
	Dark
}

/// <summary>
/// Defines the GNOME accent color values exposed by org.gnome.desktop.interface accent-color.
/// </summary>
public enum GnomeAccentColor
{
	Unknown,
	Blue,
	Teal,
	Green,
	Yellow,
	Orange,
	Red,
	Pink,
	Purple,
	Slate
}

/// <summary>
/// Detects the current GNOME desktop theme and accent color via Gio.Settings.
/// </summary>
public sealed class ThemeDetector : IDisposable
{
	const string InterfaceSchema = "org.gnome.desktop.interface";
	const string ColorSchemeKey = "color-scheme";
	const string AccentColorKey = "accent-color";

	readonly Gio.Settings _settings;
	uint _themeChangedSourceId;
	bool _isDisposed;

	/// <summary>
	/// Fires when either the color scheme or accent color changes.
	/// </summary>
	public event Action<ThemeInfo>? ThemeChanged;

	/// <summary>
	/// The current theme state read from GSettings.
	/// </summary>
	public ThemeInfo Current
	{
		get
		{
			var accentColorName = GetAccentColorName();

			return new ThemeInfo
			{
				Theme = GetTheme(),
				AccentColor = ParseAccentColor(accentColorName),
				AccentColorName = accentColorName
			};
		}
	}

	public ThemeDetector()
	{
		_settings = Gio.Settings.New(InterfaceSchema);
		_settings.OnChanged += OnSettingsChanged;
	}

	void OnSettingsChanged(Gio.Settings sender, Gio.Settings.ChangedSignalArgs args)
	{
		if (args.Key is ColorSchemeKey or AccentColorKey)
			QueueThemeChanged();
	}

	void QueueThemeChanged()
	{
		if (_isDisposed || _themeChangedSourceId != 0)
			return;

		_themeChangedSourceId = GLib.Functions.IdleAdd(GLib.Constants.PRIORITY_DEFAULT_IDLE, () =>
		{
			_themeChangedSourceId = 0;

			if (_isDisposed)
				return GLib.Constants.SOURCE_REMOVE;

			ThemeChanged?.Invoke(Current);
			return GLib.Constants.SOURCE_REMOVE;
		});
	}

	AppTheme GetTheme() => _settings.GetString(ColorSchemeKey) switch
	{
		"prefer-dark" => AppTheme.Dark,
		"prefer-light" => AppTheme.Light,
		_ => AppTheme.Unspecified
	};

	string GetAccentColorName() => _settings.GetString(AccentColorKey);

	/// <summary>
	/// Sets the GNOME color-scheme preference. Passing <see cref="AppTheme.Unspecified"/> restores the default preference.
	/// </summary>
	public bool SetTheme(AppTheme theme) => _settings.SetString(ColorSchemeKey, theme switch
	{
		AppTheme.Dark => "prefer-dark",
		AppTheme.Light => "prefer-light",
		AppTheme.Unspecified => "default",
		_ => "default"
	});

	/// <summary>
	/// Sets the GNOME accent color.
	/// </summary>
	public bool SetAccentColor(GnomeAccentColor accentColor)
	{
		var value = ToAccentColorName(accentColor);
		return value is not null && _settings.SetString(AccentColorKey, value);
	}

	static GnomeAccentColor ParseAccentColor(string accentColor) => accentColor switch
	{
		"blue" => GnomeAccentColor.Blue,
		"teal" => GnomeAccentColor.Teal,
		"green" => GnomeAccentColor.Green,
		"yellow" => GnomeAccentColor.Yellow,
		"orange" => GnomeAccentColor.Orange,
		"red" => GnomeAccentColor.Red,
		"pink" => GnomeAccentColor.Pink,
		"purple" => GnomeAccentColor.Purple,
		"slate" => GnomeAccentColor.Slate,
		_ => GnomeAccentColor.Unknown
	};

	static string? ToAccentColorName(GnomeAccentColor accentColor) => accentColor switch
	{
		GnomeAccentColor.Blue => "blue",
		GnomeAccentColor.Teal => "teal",
		GnomeAccentColor.Green => "green",
		GnomeAccentColor.Yellow => "yellow",
		GnomeAccentColor.Orange => "orange",
		GnomeAccentColor.Red => "red",
		GnomeAccentColor.Pink => "pink",
		GnomeAccentColor.Purple => "purple",
		GnomeAccentColor.Slate => "slate",
		_ => null
	};

	public void Dispose()
	{
		if (_isDisposed)
			return;

		_isDisposed = true;
		if (GLib.Functions.SourceRemove(_themeChangedSourceId))
		{
			_themeChangedSourceId = 0;
		}

		_settings.OnChanged -= OnSettingsChanged;
		_settings.Dispose();
	}
}
