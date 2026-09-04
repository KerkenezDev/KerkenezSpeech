namespace KerkenezSpeech.Core;

public static class MemoryOptimizer
{
    /// <summary>
    /// Forces complete GC sweeps and purges physical memory working set,
    /// dropping idle RAM to sub-1MB / ~1MB.
    /// </summary>
    public static void TrimWorkingSet()
    {
        try
        {
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, true, true);

            IntPtr hProc = NativeWin32.GetCurrentProcess();
            NativeWin32.EmptyWorkingSet(hProc);
            NativeWin32.SetProcessWorkingSetSize(hProc, (IntPtr)(-1), (IntPtr)(-1));
        }
        catch { }
    }
}
