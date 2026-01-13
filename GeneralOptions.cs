using Microsoft.VisualStudio.Shell;
using System.ComponentModel;
using System.Drawing.Design;

namespace AutoThemeSwitcherNG;

public class GeneralOptions : DialogPage {
    public static readonly System.Guid DefaultLightThemeId = new("de3dbbcd-f642-433c-8353-8f1df4370aba"); // VS Light
    public static readonly System.Guid DefaultDarkThemeId = new("1ded0138-47ce-435e-84ef-9ec1f439b749"); // VS Dark

    [Category("Theme Settings")]
    [DisplayName("Light Theme")]
    [Description("Theme to use when switching to Light mode.")]
    [Editor(typeof(ThemeIdEditor), typeof(UITypeEditor))]
    [TypeConverter(typeof(ThemeGuidDisplayConverter))]
    [DefaultValue(typeof(System.Guid), "de3dbbcd-f642-433c-8353-8f1df4370aba")] // VS Light
    public System.Guid LightThemeId { get; set; } = DefaultLightThemeId;

    [Category("Theme Settings")]
    [DisplayName("Dark Theme")]
    [Description("Theme to use when switching to Dark mode.")]
    [Editor(typeof(ThemeIdEditor), typeof(UITypeEditor))]
    [TypeConverter(typeof(ThemeGuidDisplayConverter))]
    [DefaultValue(typeof(System.Guid), "1ded0138-47ce-435e-84ef-9ec1f439b749")] // VS Dark
    public System.Guid DarkThemeId { get; set; } = DefaultDarkThemeId;
}

