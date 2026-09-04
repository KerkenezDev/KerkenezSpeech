using KerkenezSpeech.Services;
using KerkenezSpeech.UI;

namespace KerkenezSpeech;

internal static class Program
{
    private static Mutex? _singleInstanceMutex;

    [STAThread]
    static void Main(string[] args)
    {
        // 1. Process CLI Arguments
        if (args.Length > 0)
        {
            string firstArg = args[0].ToLowerInvariant();
            if (firstArg is "--uninstall" or "-u" or "/uninstall")
            {
                bool isQuiet = args.Contains("--quiet") || args.Contains("-q");
                SetupEngine.Uninstall(isQuiet);
                return;
            }
        }

        // 2. Single Instance Check
        const string mutexName = "Local\\KerkenezSpeech_Nemotron_Optimized_Mutex";
        _singleInstanceMutex = new Mutex(true, mutexName, out bool isNewInstance);

        if (!isNewInstance)
        {
            return;
        }

        try
        {
            using var app = new NativeTrayApp();
            app.Run();
        }
        finally
        {
            _singleInstanceMutex.ReleaseMutex();
            _singleInstanceMutex.Dispose();
        }
    }
}
