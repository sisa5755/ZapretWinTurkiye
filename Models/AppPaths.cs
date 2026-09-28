using System.IO;

namespace ZapretGuiWpf.Models;

/// <summary>
/// Mirrors the Python "Paths" class. Folder layout is identical to the
/// original zapret-win-turkey package, so this app can just be dropped
/// into the same root folder (next to bin/ and config/) as a replacement
/// for zapret_gui.py.
/// </summary>
public sealed class AppPaths
{
    public string Root { get; }
    public string ConfigDir { get; }
    public string BinDir { get; }

    public string ConfigIni { get; }
    public string HostlistAuto { get; }
    public string HostlistManual { get; }
    public string ExcludeList { get; }

    public string ZapretRoot { get; }
    public string WinwsDir { get; }
    public string BlockcheckDir { get; }
    public string CygwinBash { get; }

    public string DnscryptDir { get; }
    public string DnscryptExe { get; }

    public string PcapDir { get; }
    public string PcapExe { get; }

    public string IconIco { get; }

    public AppPaths(string root)
    {
        Root = root;
        ConfigDir = Path.Combine(root, "config");
        BinDir = Path.Combine(root, "bin");

        ConfigIni = Path.Combine(ConfigDir, "config.ini");
        HostlistAuto = Path.Combine(ConfigDir, "autohostlist.txt");
        HostlistManual = Path.Combine(ConfigDir, "hostlist.txt");
        ExcludeList = Path.Combine(ConfigDir, "excludelist.txt");

        ZapretRoot = Path.Combine(BinDir, "zapret");
        WinwsDir = Path.Combine(ZapretRoot, "zapret-winws");
        BlockcheckDir = Path.Combine(ZapretRoot, "blockcheck");
        CygwinBash = Path.Combine(ZapretRoot, "cygwin", "bin", "bash.exe");

        DnscryptDir = Path.Combine(BinDir, "dnscrypt-proxy");
        DnscryptExe = Path.Combine(DnscryptDir, "dnscrypt-proxy.exe");

        PcapDir = Path.Combine(BinDir, "go-pcap2socks");
        PcapExe = Path.Combine(PcapDir, "go-pcap2socks.exe");

        IconIco = Path.Combine(WinwsDir, "winws2.ico");
    }

    public void Ensure() => Directory.CreateDirectory(ConfigDir);

    public string StrategyFile(string engine) =>
        Path.Combine(ConfigDir, engine == "Zapret2" ? "strategy2.txt" : "strategy.txt");

    public string WinwsExe(string engine) =>
        Path.Combine(WinwsDir, engine == "Zapret2" ? "winws2.exe" : "winws.exe");

    public string WinwsProcessName(string engine) =>
        engine == "Zapret2" ? "winws2.exe" : "winws.exe";

    public string BlockcheckScript(string engine)
    {
        string sub = engine == "Zapret2" ? "zapret2" : "zapret";
        return Path.Combine(BlockcheckDir, sub, "blog.sh");
    }

    public string BlockcheckLog(string engine) =>
        Path.Combine(BlockcheckDir, engine == "Zapret2" ? "blockcheck2.log" : "blockcheck.log");
}
