using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZapretGuiWpf.Helpers;

public static class ProcessHelper
{
    /// <summary>
    /// Runs a command hidden (no console window at all - equivalent of the
    /// Python CREATE_NO_WINDOW path), waits for it, and returns (exitCode, output).
    /// </summary>
    public static (int Code, string Output) RunCaptured(string fileName, string[] args, string workingDir = null, int timeoutMs = 20000)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = workingDir ?? "",
            };
            foreach (var a in args) psi.ArgumentList.Add(a);

            using var proc = Process.Start(psi);
            var sb = new StringBuilder();
            proc.OutputDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            if (!proc.WaitForExit(timeoutMs))
            {
                try { proc.Kill(true); } catch { /* ignore */ }
                return (-1, sb.ToString());
            }
            return (proc.ExitCode, sb.ToString());
        }
        catch (Exception ex)
        {
            Logger.Warning($"Komut çalıştırılamadı ({fileName} {string.Join(' ', args)}): {ex.Message}");
            return (-1, ex.Message);
        }
    }

    /// <summary>
    /// Starts a process in the background (not waited on) - equivalent of
    /// run_hidden(..., wait=False). Used for winws.exe / go-pcap2socks.exe.
    /// </summary>
    public static Process RunBackground(string fileName, string[] args, string workingDir = null)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDir ?? "",
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            return Process.Start(psi);
        }
        catch (Exception ex)
        {
            Logger.Exception($"Komut arka planda başlatılamadı: {fileName}", ex);
            return null;
        }
    }

    /// <summary>
    /// Starts a process with a hidden-but-real console window - equivalent of
    /// run_hidden(new_console=True), used only for cygwin's interactive
    /// "bash -i" (which needs a real console/tty, unlike winws.exe/sc.exe).
    /// </summary>
    public static Process RunHiddenConsole(string fileName, string[] args, string workingDir = null)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = workingDir ?? "",
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            return Process.Start(psi);
        }
        catch (Exception ex)
        {
            Logger.Exception($"Blockcheck süreci başlatılamadı: {fileName}", ex);
            return null;
        }
    }

    public static bool ProcessExists(string exeName)
    {
        string name = Path.GetFileNameWithoutExtension(exeName);
        try
        {
            return Process.GetProcessesByName(name).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public static void KillProcess(string exeName)
    {
        string name = Path.GetFileNameWithoutExtension(exeName);
        foreach (var p in Process.GetProcessesByName(name))
        {
            try { p.Kill(true); } catch { /* already gone */ }
        }
    }

    public static async Task WaitUntilGoneAsync(string exeName, int maxAttempts = 25, int delayMs = 200)
    {
        int attempts = 0;
        while (ProcessExists(exeName) && attempts < maxAttempts)
        {
            KillProcess(exeName);
            await Task.Delay(delayMs);
            attempts++;
        }
        if (ProcessExists(exeName))
            Logger.Warning($"{exeName} sonlandırılamadı (5 sn içinde).");
    }

    public static (int Code, string Output) PsRun(string script, int timeoutMs = 20000) =>
        RunCaptured("powershell", new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", script }, timeoutMs: timeoutMs);

    public static string PsQuote(string value) => "'" + value.Replace("'", "''") + "'";

    /// <summary>Stops/deletes one or more Windows services (equivalent of sc_control()).</summary>
    public static void ScControl(string[] serviceNames, string[] actions = null)
    {
        actions ??= new[] { "stop", "delete" };
        foreach (var name in serviceNames)
            foreach (var action in actions)
                RunCaptured("sc", new[] { action, name });
    }
}
