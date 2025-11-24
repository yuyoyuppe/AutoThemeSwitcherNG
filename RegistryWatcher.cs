using Microsoft.Win32.SafeHandles;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace AutoThemeSwitcherNG;

internal class RegistryWatcher : IDisposable {
    private readonly SafeRegistryHandle _hKey;
    private readonly AutoResetEvent _waitHandle;
    private readonly Thread _watcherThread;
    private readonly object _eventLock = new();
    private EventHandler<RegistryChangedEventArgs>? _changed;
    private volatile bool _disposed;

    public event EventHandler<RegistryChangedEventArgs> Changed {
        add {
            ThrowIfDisposed();
            lock (_eventLock) { _changed += value; }
        }
        remove {
            lock (_eventLock) { _changed -= value; }
        }
    }

    public RegistryWatcher(string keyPath) {
        int result = NativeMethods.RegOpenKeyEx(
            NativeMethods.HKEY_CURRENT_USER,
            keyPath,
            0,
            NativeMethods.KEY_NOTIFY,
            out IntPtr hKeyRaw);

        if (result != NativeMethods.ERROR_SUCCESS)
            throw new Win32Exception(result, "RegOpenKeyEx failed");

        _hKey = new SafeRegistryHandle(hKeyRaw, true);

        if (_hKey.IsInvalid)
            throw new InvalidOperationException($"Unable to open the registry key '{keyPath}'");

        _waitHandle = new AutoResetEvent(false);
        _watcherThread = new Thread(WatchForChanges) {
            IsBackground = true
        };
        _watcherThread.Start();
    }

    private void WatchForChanges() {
        try {
            while (!_disposed) {
                int result = NativeMethods.RegNotifyChangeKeyValue(
                    _hKey,
                    false,
                    NativeMethods.REG_NOTIFY_CHANGE_LAST_SET,
                    _waitHandle.SafeWaitHandle,
                    true);

                if (result != NativeMethods.ERROR_SUCCESS)
                    throw new Win32Exception(result, "RegNotifyChangeKeyValue failed");

                // Wait for the event to be signaled (registry change) or timeout
                // We use a timeout to periodically check _disposed flag if signal doesn't come, 
                // although Set() in Dispose() handles that too.
                if (_waitHandle.WaitOne(TimeSpan.FromSeconds(3)) && !_disposed) {
                    try {
                        OnChanged(new RegistryChangedEventArgs());
                    } catch (Exception ex) {
                        Log.Error(ex, "Exception in RegistryWatcher.OnChanged");
                    }
                }
            }
        } catch (Exception ex) {
            if (!_disposed)
                Log.Error(ex, "Exception in RegistryWatcher.WatchForChanges");
        }
    }

    protected virtual void OnChanged(RegistryChangedEventArgs e) {
        EventHandler<RegistryChangedEventArgs>? handler;
        lock (_eventLock) {
            handler = _changed;
        }

        handler?.Invoke(this, e);
    }

    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing) {
        if (!_disposed) {
            _disposed = true;
            if (disposing) {
                // Signal the event to wake up the thread so it can exit the loop
                try { _waitHandle?.Set(); } catch { }

                if (_watcherThread.IsAlive) {
                    if (!_watcherThread.Join(TimeSpan.FromSeconds(5))) {
                        // Thread is stuck
                    }
                }

                _hKey?.Dispose();
                _waitHandle?.Dispose();
            }
        }
    }

    private void ThrowIfDisposed() {
        if (_disposed) {
            throw new ObjectDisposedException(nameof(RegistryWatcher));
        }
    }

    ~RegistryWatcher() {
        Dispose(false);
    }
}

internal class RegistryChangedEventArgs : EventArgs {
}

internal static class NativeMethods {
    public const int ERROR_SUCCESS = 0;
    public static readonly IntPtr HKEY_CURRENT_USER = new(unchecked((int)0x80000001));
    public const int KEY_NOTIFY = 0x0010;
    public const int REG_NOTIFY_CHANGE_LAST_SET = 0x00000004;

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern int RegOpenKeyEx(
        IntPtr hKey,
        string lpSubKey,
        int ulOptions,
        int samDesired,
        out IntPtr phkResult);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern int RegNotifyChangeKeyValue(
        SafeRegistryHandle hKey,
        bool bWatchSubtree,
        int dwNotifyFilter,
        SafeWaitHandle hEvent,
        bool fAsynchronous);
}
