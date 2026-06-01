using System.Threading;
using ClaudeTokenTracker.App;

namespace ClaudeTokenTracker;

internal static class Program
{
    private const string MutexName = "ClaudeTokenTracker_SingleInstance_{8F2A1B47-9C3D-4E5A-B6F1-2D7C8E9A0B11}";

    [STAThread]
    private static void Main()
    {
        // Only allow a single running instance so we don't poll twice or
        // stack multiple tray icons.
        using var singleInstance = new Mutex(initiallyOwned: true, MutexName, out bool isNewInstance);
        if (!isNewInstance)
        {
            MessageBox.Show(
                "Claude Token Tracker is already running.\nLook for its icon in the system tray (bottom-right of the taskbar).",
                "Claude Token Tracker",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();

        // Surface unexpected errors instead of silently dying in the tray.
        Application.ThreadException += (_, e) => ErrorReporter.Report(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ErrorReporter.Report(e.ExceptionObject as Exception);

        using var context = new TrayApplicationContext();
        Application.Run(context);

        GC.KeepAlive(singleInstance);
    }
}
