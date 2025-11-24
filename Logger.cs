using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using Task = System.Threading.Tasks.Task;

namespace AutoThemeSwitcherNG;

internal static class Log {
    private static Guid _paneGuid = new("9de8e201-fb24-4acd-a2d8-1562127a291c");
    private static readonly string _paneName = "AutoThemeSwitcherNG";
    private static IVsOutputWindowPane? _pane;

    public static async Task InitializeAsync(IServiceProvider serviceProvider) {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        IVsOutputWindow output = (IVsOutputWindow)serviceProvider.GetService(typeof(SVsOutputWindow));

        if (output != null) {
            output.CreatePane(ref _paneGuid, _paneName, 1, 1);
            output.GetPane(ref _paneGuid, out _pane);
        }
    }

    public static void Warning(string message, params object[] args) => WriteLine($"WARNING: {string.Format(message, args)}");

    public static void Error(Exception ex, string message, params object[] args) => WriteLine($"ERROR: {string.Format(message, args)}. Exception: {ex}");

    private static void WriteLine(string message) => ThreadHelper.JoinableTaskFactory.Run(async delegate {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        _pane?.OutputStringThreadSafe($"{DateTime.Now}: {message}{Environment.NewLine}");
    });
}

