using System.Text;

namespace KerkenezSpeech.Core;

public class FocusManager : IDisposable
{
    private IntPtr _hook = IntPtr.Zero;
    private readonly NativeWin32.WinEventDelegate _procDelegate;
    private IntPtr _lastTargetWindow = IntPtr.Zero;
    private readonly uint _currentProcessId;
    private readonly object _lock = new();

    public IntPtr LastTargetWindow
    {
        get
        {
            lock (_lock) return _lastTargetWindow;
        }
    }

    public FocusManager()
    {
        _currentProcessId = (uint)Environment.ProcessId;
        _procDelegate = WinEventProc;

        try
        {
            _hook = NativeWin32.SetWinEventHook(
                NativeWin32.EVENT_SYSTEM_FOREGROUND,
                NativeWin32.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero,
                _procDelegate,
                0,
                0,
                NativeWin32.WINEVENT_OUTOFCONTEXT | NativeWin32.WINEVENT_SKIPOWNPROCESS
            );
        }
        catch { }

        IntPtr current = NativeWin32.GetForegroundWindow();
        if (IsValidTargetWindow(current))
        {
            _lastTargetWindow = current;
        }
    }

    private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hWnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (eventType == NativeWin32.EVENT_SYSTEM_FOREGROUND && hWnd != IntPtr.Zero)
        {
            if (IsValidTargetWindow(hWnd))
            {
                lock (_lock)
                {
                    _lastTargetWindow = hWnd;
                }
            }
        }
    }

    public bool IsValidTargetWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero || !NativeWin32.IsWindow(hWnd)) return false;

        NativeWin32.GetWindowThreadProcessId(hWnd, out uint pid);
        if (pid == _currentProcessId) return false;

        StringBuilder sb = new(256);
        NativeWin32.GetClassName(hWnd, sb, sb.Capacity);
        string className = sb.ToString();

        if (className is "Shell_TrayWnd" or "NotifyIconOverflowWindow" or "Windows.UI.Core.CoreWindow" or "Progman" or "WorkerW")
        {
            return false;
        }

        return true;
    }

    public void RestoreTargetFocus()
    {
        IntPtr targetHwnd;
        lock (_lock)
        {
            targetHwnd = _lastTargetWindow;
        }

        if (targetHwnd != IntPtr.Zero && NativeWin32.IsWindow(targetHwnd))
        {
            ForceForegroundWindow(targetHwnd);
        }
    }

    public static void ForceForegroundWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero || !NativeWin32.IsWindow(hWnd)) return;

        IntPtr currentForeground = NativeWin32.GetForegroundWindow();
        if (currentForeground == hWnd) return;

        uint currentThreadId = NativeWin32.GetCurrentThreadId();
        uint targetThreadId = NativeWin32.GetWindowThreadProcessId(hWnd, out _);
        uint foregroundThreadId = currentForeground != IntPtr.Zero ? NativeWin32.GetWindowThreadProcessId(currentForeground, out _) : 0;

        try
        {
            if (currentThreadId != targetThreadId && targetThreadId != 0)
            {
                NativeWin32.AttachThreadInput(currentThreadId, targetThreadId, true);
            }
            if (foregroundThreadId != targetThreadId && foregroundThreadId != 0)
            {
                NativeWin32.AttachThreadInput(foregroundThreadId, targetThreadId, true);
            }

            NativeWin32.AllowSetForegroundWindow(NativeWin32.ASFW_ANY);
            NativeWin32.SetForegroundWindow(hWnd);
        }
        finally
        {
            if (currentThreadId != targetThreadId && targetThreadId != 0)
            {
                NativeWin32.AttachThreadInput(currentThreadId, targetThreadId, false);
            }
            if (foregroundThreadId != targetThreadId && foregroundThreadId != 0)
            {
                NativeWin32.AttachThreadInput(foregroundThreadId, targetThreadId, false);
            }
        }
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeWin32.UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
