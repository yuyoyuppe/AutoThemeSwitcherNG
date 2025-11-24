using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Drawing;
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
            Color color = VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowBackgroundColorKey);
            double luminance = ((0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B)) / 255;
            bool vsIsDark = luminance < 0.5;

            // Only switch if the current VS theme doesn't match the system theme
            if (vsIsDark != systemIsDark) {
                GeneralOptions options = (GeneralOptions)GetDialogPage(typeof(GeneralOptions));
                string targetThemeKeyword = systemIsDark ? options.DarkThemeName : options.LightThemeName;

                string commandName = $"Tools.{targetThemeKeyword}";

                if (GetService(typeof(DTE)) is DTE dte) {
                    try {
                        dte.ExecuteCommand(commandName);
                    } catch (Exception ex) {
                        VsShellUtilities.ShowMessageBox(
                          this,
                          $"Could not execute command '{commandName}'. Error: {ex.Message}",
                          "Auto Theme Switcher",
                          OLEMSGICON.OLEMSGICON_WARNING,
                          OLEMSGBUTTON.OLEMSGBUTTON_OK,
                          OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                    }
                }
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

    #endregion
}
