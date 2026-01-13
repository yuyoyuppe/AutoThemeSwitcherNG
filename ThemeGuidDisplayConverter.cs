using Microsoft.VisualStudio.Shell;
using System;
using System.ComponentModel;
using System.Globalization;

namespace AutoThemeSwitcherNG;

/// <summary>
/// Displays theme GUIDs as theme names in the Options UI, while still persisting the underlying GUID.
/// </summary>
internal sealed class ThemeGuidDisplayConverter : GuidConverter {
    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType) {
        if (destinationType == typeof(string) && value is Guid id) {
            // When VS persists settings it often calls the converter without context.
            // In that case we must return a stable GUID string so settings round-trip correctly.
            if (context is null) {
                return id.ToString("D");
            }

            // For UI display, show the friendly theme name.
            return id == Guid.Empty
                ? "(not set)"
                : RunOnUIThread(() => {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    return ThemeManager.TryGetThemeName(id) ?? id.ToString("D");
                });
        }

        return base.ConvertTo(context, culture, value, destinationType);
    }

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) {
        if (value is string s) {
            if (Guid.TryParse(s, out Guid id))
                return id;

            // If we ever get a non-GUID string, treat it as unset.
            return Guid.Empty;
        }

        return base.ConvertFrom(context, culture, value);
    }

    private static T RunOnUIThread<T>(Func<T> func) => ThreadHelper.CheckAccess()
            ? func()
            : ThreadHelper.JoinableTaskFactory.Run(async () => {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                return func();
            });
}
