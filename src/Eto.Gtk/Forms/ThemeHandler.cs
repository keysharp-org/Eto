using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Eto.GtkSharp.Forms;

/// <summary>
/// Represents a GTK theme style setting (System default, Light, or Dark).
/// </summary>
public enum GtkThemeStyle
{
	/// <summary>Follow the system/OS theme preference.</summary>
	System,
	/// <summary>Force light theme.</summary>
	Light,
	/// <summary>Force dark theme.</summary>
	Dark
}

public class ThemeHandler : WidgetHandler<GtkThemeStyle?, Theme>, Theme.IHandler
{
	// Cache the original GTK theme name so Light/Dark can select matching variants
	static string s_systemThemeName;
	static bool s_systemDefaultsCaptured;

	// When set, this is a named GTK theme (e.g. "Adwaita", "Yaru-dark")
	string _gtkThemeName;

	internal static void EnsureSystemDefaults()
	{
		if (!s_systemDefaultsCaptured)
		{
			var settings = Gtk.Settings.Default;
			s_systemThemeName = settings.ThemeName;
			s_systemDefaultsCaptured = true;
		}
	}
	
	static ThemeHandler()
	{
		EnsureSystemDefaults();
	}

	static string GetLightThemeName(string themeName)
	{
		if (string.IsNullOrEmpty(themeName))
			return themeName;

		if (themeName.EndsWith("-dark", StringComparison.OrdinalIgnoreCase))
			return themeName.Substring(0, themeName.Length - "-dark".Length);
		if (themeName.EndsWith(":dark", StringComparison.OrdinalIgnoreCase))
			return themeName.Substring(0, themeName.Length - ":dark".Length);
		return themeName;
	}

	static string GetDarkThemeName(string themeName)
	{
		if (string.IsNullOrEmpty(themeName))
			return null;

		var lightName = GetLightThemeName(themeName);
		return ThemeExists(lightName + "-dark") ? lightName + "-dark" :
			ThemeExists(lightName + ":dark") ? lightName + ":dark" : null;
	}

	static readonly string[] s_themeSearchDirs = GetThemeSearchDirs();

	static string[] GetThemeSearchDirs()
	{
		var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		return new[]
		{
			Path.Combine(homeDir, ".themes"),
			Path.Combine(homeDir, ".local", "share", "themes"),
			Path.Combine("/usr", "share", "themes"),
			Path.Combine("/usr", "local", "share", "themes"),
		};
	}

	/// <summary>
	/// Checks if a GTK3 theme exists by looking for it in the standard theme directories.
	/// </summary>
	static bool ThemeExists(string themeName)
	{
		if (string.IsNullOrEmpty(themeName))
			return false;

		foreach (var dir in s_themeSearchDirs)
		{
			var themeDir = Path.Combine(dir, themeName, "gtk-3.0");
			if (Directory.Exists(themeDir))
				return true;
		}
		return false;
	}

	/// <summary>
	/// Enumerates all installed GTK3 theme names by scanning the standard theme directories.
	/// </summary>
	internal static IEnumerable<string> GetInstalledThemeNames()
	{
		var seen = new HashSet<string>(StringComparer.Ordinal);
		foreach (var dir in s_themeSearchDirs)
		{
			if (!Directory.Exists(dir))
				continue;
			foreach (var themeDir in Directory.EnumerateDirectories(dir))
			{
				var gtk3Dir = Path.Combine(themeDir, "gtk-3.0");
				if (!Directory.Exists(gtk3Dir))
					continue;
				var themeName = Path.GetFileName(themeDir);
				if (seen.Add(themeName))
					yield return themeName;
			}
		}
	}

	/// <summary>
	/// Creates a handler for a System/Light/Dark style.
	/// </summary>
	public ThemeHandler(GtkThemeStyle? style = GtkThemeStyle.System)
	{
		Control = style;
		if (style == GtkThemeStyle.Dark)
			_gtkThemeName = GetDarkThemeName(s_systemThemeName);
		if (style == GtkThemeStyle.Light)
			_gtkThemeName = GetLightThemeName(s_systemThemeName);
	}

	/// <summary>
	/// Creates a handler for a specific named GTK theme (e.g. "Adwaita", "Yaru-dark").
	/// </summary>
	public ThemeHandler(string gtkThemeName)
	{
		_gtkThemeName = gtkThemeName;
		// Classify by naming convention
		if (gtkThemeName.EndsWith("-dark", StringComparison.OrdinalIgnoreCase)
			|| gtkThemeName.EndsWith(":dark", StringComparison.OrdinalIgnoreCase))
			Control = GtkThemeStyle.Dark;
		else
			Control = GtkThemeStyle.Light;
	}

	public string Name => _gtkThemeName ?? Control switch
	{
		GtkThemeStyle.System => "System",
		GtkThemeStyle.Light => "Light",
		GtkThemeStyle.Dark => "Dark",
		_ => Control.ToString()
	};

	public ThemeStyle ThemeStyle => Control == GtkThemeStyle.System
		? GetSystemThemeStyle(ApplicationHandler.Instance?.PortalColorScheme)
		: IsDarkTheme() ? ThemeStyle.Dark : ThemeStyle.Light;

	internal static ThemeStyle ResolveSystemThemeStyle(uint? portalColorScheme,
		bool applicationPreferDarkTheme, string themeName)
	{
		if (portalColorScheme == 1)
			return ThemeStyle.Dark;
		if (portalColorScheme == 2)
			return ThemeStyle.Light;
		return applicationPreferDarkTheme || IsDarkThemeName(themeName) ? ThemeStyle.Dark : ThemeStyle.Light;
	}

	internal static ThemeStyle GetSystemThemeStyle(uint? portalColorScheme)
	{
		var settings = Gtk.Settings.Default;
		return ResolveSystemThemeStyle(portalColorScheme,
			settings.ApplicationPreferDarkTheme, settings.ThemeName);
	}

	internal static uint? NormalizePortalColorScheme(uint? colorScheme) =>
		colorScheme == 1 || colorScheme == 2 ? colorScheme : null;

	static bool IsDarkThemeName(string themeName) =>
		!string.IsNullOrEmpty(themeName) && (themeName.EndsWith("-dark", StringComparison.OrdinalIgnoreCase)
			|| themeName.EndsWith(":dark", StringComparison.OrdinalIgnoreCase));

	bool IsDarkTheme()
	{
		if (Control == GtkThemeStyle.Dark)
			return true;
		if (Control == GtkThemeStyle.Light)
			return false;
		if (Control == GtkThemeStyle.System && Gtk.Settings.Default.ApplicationPreferDarkTheme)
			return true;
		string themeName;
		if (Control == GtkThemeStyle.System)
			themeName = Gtk.Settings.Default.ThemeName;
		else
			themeName = _gtkThemeName;
		return IsDarkThemeName(themeName);
	}

	public void SetTheme() => SetTheme(null);

	internal void SetTheme(uint? portalColorScheme)
	{
		var settings = Gtk.Settings.Default;

		if (_gtkThemeName != null)
		{
			// Named theme — apply it directly
			settings.ThemeName = _gtkThemeName;
			settings.ApplicationPreferDarkTheme = Control == GtkThemeStyle.Dark;
			return;
		}

		switch (Control)
		{
			case GtkThemeStyle.System:
				settings.ResetProperty("gtk-theme-name");
				settings.ResetProperty("gtk-application-prefer-dark-theme");
				// With no portal preference, leave this reset so it continues to follow the GTK session value.
				if (portalColorScheme != null)
					settings.ApplicationPreferDarkTheme = portalColorScheme == 1;
				break;
			case GtkThemeStyle.Light:
				settings.ApplicationPreferDarkTheme = false;
				var lightName = GetLightThemeName(settings.ThemeName);
				if (lightName != settings.ThemeName && ThemeExists(lightName))
					settings.ThemeName = lightName;
				break;
			case GtkThemeStyle.Dark:
				settings.ApplicationPreferDarkTheme = true;
				var darkName = GetDarkThemeName(settings.ThemeName);
				if (darkName != null && darkName != settings.ThemeName)
					settings.ThemeName = darkName;
				break;
		}
	}

	public override bool Equals(object obj)
	{
		if (obj is ThemeHandler other)
			return Control == other.Control && _gtkThemeName == other._gtkThemeName;
		return base.Equals(obj);
	}

	public override int GetHashCode()
	{
		if (_gtkThemeName != null)
			return _gtkThemeName.GetHashCode();
		return Control.GetHashCode();
	}
}

public class ThemesHandler : Themes.IHandler
{
	Theme _system;
	Theme _light;
	Theme _dark;

	public Theme System => _system ??= new Theme(new ThemeHandler(GtkThemeStyle.System));
	public Theme Light => _light ??= new Theme(new ThemeHandler(GtkThemeStyle.Light));
	public Theme Dark => _dark ??= new Theme(new ThemeHandler(GtkThemeStyle.Dark));

	List<Theme> _themes;
	public IEnumerable<Theme> GetThemes() => _themes ??= EnumerateThemes().ToList();
	
	IEnumerable<Theme> EnumerateThemes()
	{
		yield return System;

		foreach (var themeName in ThemeHandler.GetInstalledThemeNames())
		{
			yield return new Theme(new ThemeHandler(themeName));
		}
	}
}
