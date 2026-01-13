using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using Task = System.Threading.Tasks.Task;

namespace AutoThemeSwitcherNG;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[Guid(AutoThemeSwitcherNGPackage.PackageGuidString)]
[ProvideAutoLoad(VSConstants.UICONTEXT.NoSolution_string, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExists_string, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideOptionPage(typeof(GeneralOptions), "Auto Theme Switcher NG", "General", 0, 0, true)]
public sealed class AutoThemeSwitcherNGPackage : AsyncPackage {
    public const string PackageGuidString = "7fa49da3-9846-41e9-b776-2bcbc0ed0fd3";

    private SystemThemeWatcher? _themeWatcher;

    #region Package Members

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress) {
        await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        try {
            await Log.InitializeAsync(this);
        } catch (Exception) {
            // If logging fails, we shouldn't crash the package load
        }

#if DEBUG
        await InitializeDebugCommandsAsync();
#endif

        try {
            _themeWatcher = new SystemThemeWatcher();
            _themeWatcher.Changed += OnSystemThemeChanged;
        } catch (Exception ex) {
            Log.Error(ex, "Failed to initialize SystemThemeWatcher");
        }

        if (_themeWatcher != null) {
            _ = JoinableTaskFactory.RunAsync(async () => {
                await JoinableTaskFactory.SwitchToMainThreadAsync();
                SyncTheme(_themeWatcher.IsDarkMode);
            });
        }
    }

    private void OnSystemThemeChanged(object sender, ThemeChangedEventArgs e) => _ = JoinableTaskFactory.RunAsync(async () => {
        await JoinableTaskFactory.SwitchToMainThreadAsync();
        SyncTheme(e.IsDarkMode);
    });

    private void SyncTheme(bool systemIsDark) {
        ThreadHelper.ThrowIfNotOnUIThread();

        try {
            GeneralOptions options = (GeneralOptions)GetDialogPage(typeof(GeneralOptions));

            bool changed = false;
            if (options.LightThemeId == Guid.Empty) {
                options.LightThemeId = GeneralOptions.DefaultLightThemeId;
                changed = true;
            }

            if (options.DarkThemeId == Guid.Empty) {
                options.DarkThemeId = GeneralOptions.DefaultDarkThemeId;
                changed = true;
            }

            if (changed) {
                options.SaveSettingsToStorage();
            }

            Guid targetThemeId = systemIsDark ? options.DarkThemeId : options.LightThemeId;

            if (targetThemeId == Guid.Empty) {
                return;
            }

            Guid? currentThemeId = ThemeManager.TryGetCurrentThemeId();
            if (currentThemeId is Guid cur && cur == targetThemeId) {
                return; // already on the desired theme
            }

            bool ok = ThemeManager.TrySetTheme(targetThemeId, persist: true);
            if (!ok) {
                VsShellUtilities.ShowMessageBox(
                  this,
                  $"Could not set theme to '{ThemeManager.TryGetThemeName(targetThemeId) ?? targetThemeId.ToString("D")}'.",
                  "Auto Theme Switcher",
                  OLEMSGICON.OLEMSGICON_WARNING,
                  OLEMSGBUTTON.OLEMSGBUTTON_OK,
                  OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }
        } catch (Exception ex) {
            VsShellUtilities.ShowMessageBox(
               this,
               "Error checking/switching theme: " + ex.Message,
               "Auto Theme Switcher",
               OLEMSGICON.OLEMSGICON_CRITICAL,
               OLEMSGBUTTON.OLEMSGBUTTON_OK,
               OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            _themeWatcher?.Dispose();
        }

        base.Dispose(disposing);
    }

#if DEBUG
    private static readonly Guid CommandSet = new("d0ce1988-39ec-4857-a950-031f55501660");
    private const int TestThemeCommandId = 0x0101;

    private async Task InitializeDebugCommandsAsync() {
        if (await GetServiceAsync(typeof(IMenuCommandService)) is not OleMenuCommandService mcs) {
            return;
        }

        CommandID cmdId = new(CommandSet, TestThemeCommandId);
        MenuCommand menuItem = new(OnTestTheme, cmdId);
        mcs.AddCommand(menuItem);
    }

    private void OnTestTheme(object sender, EventArgs e) {
        ThreadHelper.ThrowIfNotOnUIThread();
        IReadOnlyList<ThemeManager.ThemeInfo> themes = ThemeManager.GetAvailableThemes();

        Log.Warning("Available themes: {0}", themes.Count);
        int max = Math.Min(themes.Count, 100);
        for (int i = 0; i < max; i++) {
            ThemeManager.ThemeInfo t = themes[i];
            string line = $"[{i + 1:000}] {t.Name} ({t.Id:D})";
            System.Diagnostics.Debug.WriteLine(line);
            Log.Warning(line);
        }

        if (themes.Count > max) {
            Log.Warning("... truncated ({0} total themes)", themes.Count);
        }
    }
#endif
    #endregion
}
