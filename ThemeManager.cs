using Microsoft.VisualStudio.Shell;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace AutoThemeSwitcherNG;

internal static class ThemeManager {
    internal readonly record struct ThemeInfo(Guid Id, string Name);

    private static readonly object _gate = new();
    private static IReadOnlyList<ThemeInfo>? _cachedThemes;
    private static DateTime _cachedAtUtc;

    // We keep a short cache because the Options dropdown may query often.
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(5);

    public static IReadOnlyList<ThemeInfo> GetAvailableThemes() {
        ThreadHelper.ThrowIfNotOnUIThread();

        lock (_gate) {
            if (_cachedThemes is not null && (DateTime.UtcNow - _cachedAtUtc) < CacheTtl) {
                return _cachedThemes;
            }
        }

        IReadOnlyList<ThemeInfo> themes = LoadThemesUncached();

        lock (_gate) {
            _cachedThemes = themes;
            _cachedAtUtc = DateTime.UtcNow;
        }

        return themes;
    }

    public static string? TryGetThemeName(Guid id) {
        ThreadHelper.ThrowIfNotOnUIThread();

        foreach (ThemeInfo t in GetAvailableThemes()) {
            if (t.Id == id) return t.Name;
        }

        return null;
    }

    public static bool TrySetTheme(Guid themeId, bool persist = true) {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (themeId == Guid.Empty) return false;

        if (!TryGetThemeService(out object? service, out object? themesObj)) {
            return false;
        }

        object? themeObj = FindThemeObjectById(themesObj!, themeId);
        if (themeObj is null) {
            return false;
        }

        MethodInfo? setCurrentTheme = FindSetCurrentThemeMethod(service!);
        if (setCurrentTheme is null) {
            return false;
        }

        // On VS2026: SetCurrentTheme(IVsColorTheme, bool, bool)
        try {
            _ = setCurrentTheme.Invoke(service, new object?[] { themeObj, persist, true });
            return true;
        } catch {
            return false;
        }
    }

    public static Guid? TryGetCurrentThemeId() {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!TryGetThemeService(out object? service, out _)) {
            return null;
        }

        object? currentTheme = TryGetMember(service!, "CurrentTheme");
        return currentTheme is null
            ? null
            : TryGetGuid(currentTheme, "ThemeId", out Guid id) || TryGetGuid(currentTheme, "Id", out id) ? id : null;
    }

    private static IReadOnlyList<ThemeInfo> LoadThemesUncached() {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!TryGetThemeService(out object? service, out object? themesObj)) {
            return Array.Empty<ThemeInfo>();
        }

        List<ThemeInfo> list = [];

        foreach (object? theme in EnumerateAny(themesObj!)) {
            if (theme is null) continue;

            if (!TryGetGuid(theme, "ThemeId", out Guid id) && !TryGetGuid(theme, "Id", out id)) {
                continue;
            }

            string name =
                TryGetString(theme, "Name") ??
                TryGetString(theme, "LocalizedName") ??
                TryGetString(theme, "DisplayName") ??
                id.ToString("D");

            list.Add(new ThemeInfo(id, name));
        }

        list.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return list;
    }

    private static bool TryGetThemeService(out object? service, out object? themesObj) {
        ThreadHelper.ThrowIfNotOnUIThread();

        service = null;
        themesObj = null;

        // Prefer a direct type lookup (works even if the assembly isn't loaded yet).
        // VS tends to ship interop assemblies with versioned names, so try a few.
        Type? svsColorThemeService =
            Type.GetType("Microsoft.VisualStudio.Shell.Interop.SVsColorThemeService, Microsoft.VisualStudio.Shell.Interop", throwOnError: false) ??
            Type.GetType("Microsoft.VisualStudio.Shell.Interop.SVsColorThemeService, Microsoft.VisualStudio.Shell.Interop.17.0", throwOnError: false) ??
            Type.GetType("Microsoft.VisualStudio.Shell.Interop.SVsColorThemeService, Microsoft.VisualStudio.Shell.Interop.16.0", throwOnError: false) ??
            Type.GetType("Microsoft.VisualStudio.Shell.Interop.SVsColorThemeService, Microsoft.VisualStudio.Shell.Interop.15.0", throwOnError: false) ??
            FindLoadedTypeByFullName("Microsoft.VisualStudio.Shell.Interop.SVsColorThemeService") ??
            FindLoadedTypeByName("SVsColorThemeService");

        if (svsColorThemeService is null) return false;

        service = Package.GetGlobalService(svsColorThemeService);
        if (service is null) return false;

        themesObj =
            TryGetMember(service, "Themes") ??
            TryGetMember(service, "AllThemes") ??
            TryInvoke(service, "GetThemes") ??
            TryInvoke(service, "GetAllThemes") ??
            TryInvoke(service, "GetInstalledThemes");

        return themesObj is not null;
    }

    private static MethodInfo? FindSetCurrentThemeMethod(object service) {
        ThreadHelper.ThrowIfNotOnUIThread();

        // VS2026: SetCurrentTheme(Microsoft.Internal.VisualStudio.Shell.Interop.IVsColorTheme, bool, bool)
        Type t = service.GetType();
        foreach (MethodInfo m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) {
            if (!string.Equals(m.Name, "SetCurrentTheme", StringComparison.Ordinal) &&
                !m.Name.EndsWith(".SetCurrentTheme", StringComparison.Ordinal)) {
                continue;
            }

            ParameterInfo[] ps = m.GetParameters();
            if (ps.Length != 3) continue;
            if (ps[1].ParameterType != typeof(bool) || ps[2].ParameterType != typeof(bool)) continue;

            string p0Name = ps[0].ParameterType.FullName ?? ps[0].ParameterType.Name;
            if (p0Name.EndsWith("IVsColorTheme", StringComparison.Ordinal)) {
                return m;
            }
        }

        return null;
    }

    private static object? FindThemeObjectById(object themesObj, Guid themeId) {
        ThreadHelper.ThrowIfNotOnUIThread();

        foreach (object? theme in EnumerateAny(themesObj)) {
            if (theme is null) continue;

            if (TryGetGuid(theme, "ThemeId", out Guid id) || TryGetGuid(theme, "Id", out id)) {
                if (id == themeId) return theme;
            }
        }

        return null;
    }

    private static IEnumerable<object?> EnumerateAny(object collection) {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (collection is IEnumerable enumerable) {
            foreach (object? item in enumerable) yield return item;
            yield break;
        }

        MethodInfo? getEnumerator;
        try {
            getEnumerator = collection.GetType().GetMethod("GetEnumerator", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
        } catch {
            getEnumerator = null;
        }

        if (getEnumerator is not null) {
            object? enumeratorObj;
            try {
                enumeratorObj = getEnumerator.Invoke(collection, Array.Empty<object>());
            } catch {
                enumeratorObj = null;
            }

            if (enumeratorObj is IEnumerator ie) {
                while (true) {
                    bool movedNext;
                    try { movedNext = ie.MoveNext(); } catch { break; }

                    if (!movedNext) break;
                    yield return ie.Current;
                }

                yield break;
            }
        }
    }

    private static object? TryGetMember(object target, string name) {
        ThreadHelper.ThrowIfNotOnUIThread();

        try {
            Type t = target.GetType();
            PropertyInfo prop = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
            if (prop is not null && prop.GetIndexParameters().Length == 0) return prop.GetValue(target);

            FieldInfo field = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
            if (field is not null) return field.GetValue(target);
        } catch {
            // ignore
        }

        return null;
    }

    private static object? TryInvoke(object target, string name) {
        ThreadHelper.ThrowIfNotOnUIThread();
        try {
            MethodInfo? m = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            return m?.Invoke(target, Array.Empty<object>());
        } catch {
            return null;
        }
    }

    private static bool TryGetGuid(object target, string propertyName, out Guid value) {
        ThreadHelper.ThrowIfNotOnUIThread();
        value = Guid.Empty;

        try {
            object? raw = TryGetMember(target, propertyName);
            if (raw is Guid g) {
                value = g;
                return true;
            }

            if (raw is string s && Guid.TryParse(s, out Guid gs)) {
                value = gs;
                return true;
            }
        } catch {
            // ignore
        }

        return false;
    }

    private static string? TryGetString(object target, string propertyName) {
        ThreadHelper.ThrowIfNotOnUIThread();
        try {
            return TryGetMember(target, propertyName) as string;
        } catch {
            return null;
        }
    }

    private static Type? FindLoadedTypeByFullName(string fullName) {
        foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies()) {
            try {
                Type? t = asm.GetType(fullName, throwOnError: false, ignoreCase: false);
                if (t is not null) return t;
            } catch {
                // ignore
            }
        }

        return null;
    }

    private static Type? FindLoadedTypeByName(string name) {
        foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies()) {
            Type[] types;
            try {
                types = asm.GetTypes();
            } catch {
                continue;
            }

            foreach (Type t in types) {
                if (string.Equals(t.Name, name, StringComparison.Ordinal)) return t;
            }
        }

        return null;
    }
}

