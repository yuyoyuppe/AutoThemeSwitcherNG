using Microsoft.Win32;
using System;

namespace AutoThemeSwitcherNG;

internal partial class SystemThemeWatcher : IDisposable {
    private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string RegistryValueName = "AppsUseLightTheme";
    private const bool DefaultIsDarkMode = false;

    private readonly RegistryWatcher _watcher;

    private readonly object _eventLock = new();
    private bool _cachedDarkModeState;
    private bool _isDisposed;

    private event EventHandler<ThemeChangedEventArgs>? _changed;

    internal event EventHandler<ThemeChangedEventArgs> Changed {
        add {
            ThrowIfDisposed();
            lock (_eventLock) { _changed += value; }
        }
        remove {
            lock (_eventLock) { _changed -= value; }
        }
    }

    internal SystemThemeWatcher() {
        _watcher = new RegistryWatcher(RegistryKeyPath);
        _watcher.Changed += OnRegistryChanged;
        _cachedDarkModeState = IsDarkMode;
    }

    internal bool IsDarkMode {
        get {
            ThrowIfDisposed();

            try {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
                if (key is null) {
                    Log.Warning("Registry key not found: {0}. Defaulting to {1} mode.",
                        RegistryKeyPath, DefaultIsDarkMode ? "dark" : "light");
                    return DefaultIsDarkMode;
                }

                object? value = key.GetValue(RegistryValueName);
                if (value is null) {
                    Log.Warning("Registry value not found: {0}. Defaulting to {1} mode.",
                        RegistryValueName, DefaultIsDarkMode ? "dark" : "light");
                    return DefaultIsDarkMode;
                }

                if (value is not int intValue) {
                    Log.Warning("Unexpected value type for {0}: {1}. Expected int. Defaulting to {2} mode.",
                        RegistryValueName, value.GetType().Name, DefaultIsDarkMode ? "dark" : "light");
                    return DefaultIsDarkMode;
                }

                // 0 = Dark theme, 1 = Light theme
                return intValue == 0;
            } catch (Exception ex) {
                Log.Error(ex, "Error reading theme from registry. Defaulting to {0} mode.",
                    DefaultIsDarkMode ? "dark" : "light");
                return DefaultIsDarkMode;
            }
        }
    }

    private void OnRegistryChanged(object? sender, RegistryChangedEventArgs e) {
        try {
            bool currentDarkMode = IsDarkMode;
            if (_cachedDarkModeState != currentDarkMode) {
                _cachedDarkModeState = currentDarkMode;
                OnChanged(new ThemeChangedEventArgs { IsDarkMode = currentDarkMode });
            }
        } catch (Exception ex) {
            Log.Error(ex, "Error handling registry change event");
        }
    }

    protected virtual void OnChanged(ThemeChangedEventArgs e) {
        EventHandler<ThemeChangedEventArgs>? handler;
        lock (_eventLock) {
            handler = _changed;
        }

        handler?.Invoke(this, e);
    }

    private void UnregisterEvents() => _watcher.Changed -= OnRegistryChanged;

    private void ThrowIfDisposed() {
        if (_isDisposed) {
            throw new ObjectDisposedException(nameof(SystemThemeWatcher));
        }
    }

    protected virtual void Dispose(bool disposing) {
        if (!_isDisposed) {
            if (disposing) {
                UnregisterEvents();
                _watcher.Dispose();
            }

            _isDisposed = true;
        }
    }

    public void Dispose() {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    ~SystemThemeWatcher() {
        Dispose(disposing: false);
    }
}

internal class ThemeChangedEventArgs : EventArgs {
    internal required bool IsDarkMode { get; init; }
}
