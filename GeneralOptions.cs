using Microsoft.VisualStudio.Shell;
using System.ComponentModel;

namespace AutoThemeSwitcherNG;

public class GeneralOptions : DialogPage {
    [Category("Theme Settings")]
    [DisplayName("Light Theme Name")]
    [Description("The name of the theme to use when switching to Light mode (e.g. 'Light', 'Blue').")]
    [DefaultValue("Light")]
    public string LightThemeName { get; set; } = "Light";

    [Category("Theme Settings")]
    [DisplayName("Dark Theme Name")]
    [Description("The name of the theme to use when switching to Dark mode (e.g. 'Dark').")]
    [DefaultValue("Dark")]
    public string DarkThemeName { get; set; } = "Dark";
}

