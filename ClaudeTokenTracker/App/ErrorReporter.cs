using System.IO;

namespace ClaudeTokenTracker.App;

/// <summary>
/// Last-resort handler for otherwise-unhandled exceptions. Appends to a log file
/// and shows a dialog so failures in the tray app aren't completely invisible.
/// </summary>
internal static class ErrorReporter
{
    private static readonly object Gate = new();

    public static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClaudeTokenTracker",
        "error.log");

    public static void Report(Exception? ex)
    {
        if (ex is null)
            return;

        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, $"[{DateTimeOffset.Now:O}] {ex}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch
        {
            // Nothing more we can do if even logging fails.
        }

        MessageBox.Show(
            $"An unexpected error occurred:\n\n{ex.Message}\n\nDetails were written to:\n{LogPath}",
            "Claude Token Tracker",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }
}
