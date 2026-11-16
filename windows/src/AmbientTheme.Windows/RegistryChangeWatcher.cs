using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace AmbientTheme.Windows;

/// <summary>
/// Watches one HKCU key with <c>RegNotifyChangeKeyValue</c>, re-arming after every notification and coalescing
/// bursts (apps and system values are written in separate steps) into a single callback. No polling.
/// </summary>
public sealed class RegistryChangeWatcher : IDisposable
{
    private const int NotifyChangeLastSet = 0x00000004;
    private const int NotifyThreadAgnostic = 0x10000000;

    private readonly RegistryKey _key;
    private readonly Action _onChanged;
    private readonly TimeSpan _debounce;
    private readonly AutoResetEvent _changed = new(false);
    private readonly ManualResetEvent _stop = new(false);
    private readonly Thread _thread;

    public RegistryChangeWatcher(string subKeyPath, Action onChanged, TimeSpan debounce)
    {
        _key = Registry.CurrentUser.CreateSubKey(subKeyPath, writable: false);
        _onChanged = onChanged;
        _debounce = debounce;
        Arm();
        _thread = new Thread(Loop) { IsBackground = true, Name = "atc-registry-watcher" };
        _thread.Start();
    }

    public void Dispose()
    {
        _stop.Set();
        _thread.Join();
        _key.Dispose();
        _changed.Dispose();
        _stop.Dispose();
    }

    private void Loop()
    {
        WaitHandle[] handles = [_stop, _changed];
        while (WaitHandle.WaitAny(handles) == 1)
        {
            Arm();
            // Keep absorbing changes until the key has been quiet for the debounce period.
            while (WaitHandle.WaitAny(handles, _debounce) == 1)
            {
                Arm();
            }

            if (_stop.WaitOne(0))
            {
                return;
            }

            _onChanged();
        }
    }

    private void Arm()
    {
        var error = RegNotifyChangeKeyValue(_key.Handle, false, NotifyChangeLastSet | NotifyThreadAgnostic,
            _changed.SafeWaitHandle, true);
        if (error != 0)
        {
            throw new Win32Exception(error);
        }
    }

    [DllImport("advapi32.dll")]
    private static extern int RegNotifyChangeKeyValue(SafeRegistryHandle key, bool watchSubtree, int notifyFilter,
        SafeWaitHandle eventHandle, bool asynchronous);
}
