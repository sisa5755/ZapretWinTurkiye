using System;
using System.IO;

namespace ZapretGuiWpf.Helpers;

/// <summary>
/// Minimal rotating-file logger. Mirrors the Python version's
/// RotatingFileHandler(maxBytes=512KB, backupCount=2) writing to
/// config/zapret_gui.log, so errors are never silently swallowed even
/// when the app has no console (this is the only diagnostic source for
/// a windowed exe).
/// </summary>
public static class Logger
{
    private static readonly object Lock = new();
    private static string _logPath = "";
    private const long MaxBytes = 512 * 1024;

    public static void Init(string configDir)
    {
        try
        {
            Directory.CreateDirectory(configDir);
            _logPath = Path.Combine(configDir, "zapret_gui.log");
        }
        catch
        {
            // Read-only folder etc: fall back to no-op logging, same as the Python try/except.
            _logPath = "";
        }
    }

    private static void Rotate()
    {
        try
        {
            if (!File.Exists(_logPath)) return;
            var info = new FileInfo(_logPath);
            if (info.Length < MaxBytes) return;

            string bak2 = _logPath + ".2";
            string bak1 = _logPath + ".1";
            if (File.Exists(bak2)) File.Delete(bak2);
            if (File.Exists(bak1)) File.Move(bak1, bak2);
            File.Move(_logPath, bak1);
        }
        catch
        {
            // best effort
        }
    }

    private static void Write(string level, string message, Exception ex = null)
    {
        if (string.IsNullOrEmpty(_logPath)) return;
        lock (Lock)
        {
            try
            {
                Rotate();
                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {Environment.CurrentManagedThreadId}: {message}";
                if (ex != null) line += Environment.NewLine + ex;
                File.AppendAllText(_logPath, line + Environment.NewLine);
            }
            catch
            {
                // best effort, never throw from logging
            }
        }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warning(string message) => Write("WARNING", message);
    public static void Debug(string message) => Write("DEBUG", message);
    public static void Error(string message, Exception ex = null) => Write("ERROR", message, ex);
    public static void Exception(string message, Exception ex) => Write("ERROR", message, ex);
}
