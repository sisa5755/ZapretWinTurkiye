using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ZapretGuiWpf.Helpers;
using ZapretGuiWpf.Models;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace ZapretGuiWpf;

public enum StatusLevel
{
    Neutral,
    Warning,
    Error,
    Success,
}

public partial class MainWindow : Window
{
    private const string ServiceName = "ZapretService";
    private const string WfTcpPorts = "80,443";
    private const string WfUdpPorts = "443,19294-19344,49152-65535";

    private readonly AppPaths _paths;
    private readonly AppConfig _cfg;
    private readonly Dictionary<string, Dictionary<string, string>> _strategies;
    private readonly Dictionary<string, List<string>> _strategyOrder;

    private string _currentEngine;
    private bool _isZapretRunning;
    private Process _zapretProc;
    private Process _pcapProc;
    private bool _dnsChangedByApp;
    private bool _serviceStoppedWarned;
    private bool _dnscryptActive;
    private bool _lanShareActive;
    private bool _exiting;
    private bool _suppressEvents = true;
    private bool _monitorTickRunning;

    private WinForms.NotifyIcon _trayIcon;
    private System.Windows.Threading.DispatcherTimer _monitorTimer;

    private List<Control> _criticalControls;
    private List<Control> _otherControls;

    private sealed record BlockcheckResult(string StrategyFound, bool NoBypassNeeded, string ErrorMessage, string ErrorTitle);

    public MainWindow()
    {
        InitializeComponent();

        string rootDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        _paths = new AppPaths(rootDir);
        _paths.Ensure();
        _cfg = new AppConfig(_paths.ConfigIni);
        _strategies = Strategies.Load(_paths.ConfigDir);
        _strategyOrder = new Dictionary<string, List<string>>
        {
            ["Zapret2"] = new List<string> { "Analiz Sonucu" }.Concat(_strategies["Zapret2"].Keys).ToList(),
            ["Zapret1"] = new List<string> { "Analiz Sonucu" }.Concat(_strategies["Zapret1"].Keys).ToList(),
        };

        _currentEngine = _cfg.Get("ActiveEngine", "0") == "1" ? "Zapret1" : "Zapret2";
        _dnscryptActive = _cfg.Get("DnscryptInstalled", "0") == "1";

        BuildUiInitial();
        _suppressEvents = false;

        Loaded += async (_, _) =>
        {
            await PreflightChecksAsync();
            await StartupSequenceAsync();
        };

        _monitorTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _monitorTimer.Tick += async (_, _) => await MonitorTickAsync();
        _monitorTimer.Start();
    }

    // ------------------------------------------------------------------
    // UI setup
    // ------------------------------------------------------------------
    private void BuildUiInitial()
    {
        CmbEngine.Items.Add("Zapret2 (Yeni LUA Motoru)");
        CmbEngine.Items.Add("Zapret (Eski Klasik Motor)");
        CmbEngine.SelectedIndex = _currentEngine == "Zapret1" ? 1 : 0;

        string activeStrategyLabel = _cfg.Get("ActiveStrategy", "Analiz Sonucu");
        foreach (var s in _strategyOrder[_currentEngine]) CmbStrategy.Items.Add(s);
        CmbStrategy.SelectedItem = _strategyOrder[_currentEngine].Contains(activeStrategyLabel)
            ? activeStrategyLabel
            : "Analiz Sonucu";

        CmbFilter.Items.Add("Kapalı (Tüm trafik)");
        CmbFilter.Items.Add("Auto (Zapret oluşturur)");
        CmbFilter.Items.Add("Manuel ('hostlist.txt' İçeriği)");
        string filterMode = _cfg.Get("FilterMode", "1");
        CmbFilter.SelectedIndex = filterMode switch { "0" => 0, "2" => 2, _ => 1 };

        BtnDnscrypt.Content = _dnscryptActive ? "[ ✓ ] Dnscrypt-proxy Aktif / Kaldır" : "Dnscrypt-proxy servisi kur";
        LblFooter.Text = _currentEngine == "Zapret1" ? "Zapret v72.12" : "Zapret2 v1.0.2";

        // ThemeManager.Current was already set in App.OnStartup from the saved
        // config, before this window (and its DynamicResource bindings) was
        // even constructed, so this just syncs the checkbox to match it.
        ChkDarkMode.IsChecked = ThemeManager.Current == AppTheme.Dark;

        _criticalControls = new List<Control> { BtnRun, BtnAnalyze, BtnInstallService, CmbEngine, CmbStrategy, CmbFilter, BtnDnscrypt };
        _otherControls = new List<Control> { BtnAnalyze, BtnInstallService, BtnRemoveService, CmbEngine, CmbStrategy, CmbFilter, BtnDnscrypt };
    }

    private static void SetControlsEnabled(IEnumerable<Control> controls, bool enabled)
    {
        foreach (var c in controls) c.IsEnabled = enabled;
    }

    private void SetStatus(string text, StatusLevel level = StatusLevel.Neutral)
    {
        LblStatus.Text = text;
        string key = level switch
        {
            StatusLevel.Warning => "StatusWarningBrush",
            StatusLevel.Error => "StatusErrorBrush",
            StatusLevel.Success => "StatusSuccessBrush",
            _ => "StatusNeutralBrush",
        };
        // SetResourceReference (rather than a one-off lookup) keeps this a live
        // dynamic-resource binding, so it re-colors itself automatically if the
        // theme is toggled while this status text is showing.
        LblStatus.SetResourceReference(TextBlock.ForegroundProperty, key);
    }

    /// <summary>
    /// Locks the UI into a consistent "DNS poisoned" state. Previously this
    /// was repeated at three separate call sites; centralizing it avoids the
    /// UI going inconsistent when one call site is updated and another isn't.
    /// </summary>
    private void EnterDnsPoisonedState()
    {
        SetControlsEnabled(_criticalControls, false);
        BtnDnscrypt.IsEnabled = true;
        SetStatus("DNS ZEHİRLENMESİ SAPTANDI!", StatusLevel.Error);
    }

    // ------------------------------------------------------------------
    // Preflight: GoodbyeDPI conflict (admin elevation is handled by app.manifest)
    // ------------------------------------------------------------------
    private async Task PreflightChecksAsync()
    {
        bool exists = await Task.Run(() => ProcessHelper.ProcessExists("goodbyedpi.exe"));
        if (!exists) return;

        var answer = MessageBox.Show(
            "GoodbyeDPI aktif gözüküyor, Zapret'in çalışması için GoodbyeDPI sonlandırılmalı.\n\n" +
            "GoodbyeDPI'ı kapatmak ve varsa servisini kaldırmak istiyor musunuz?",
            "Çakışma Saptandı", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (answer == MessageBoxResult.Yes)
        {
            await Task.Run(async () =>
            {
                ProcessHelper.KillProcess("goodbyedpi.exe");
                int attempts = 0;
                while (ProcessHelper.ProcessExists("goodbyedpi.exe") && attempts < 50)
                {
                    await Task.Delay(100);
                    attempts++;
                }
                ProcessHelper.ScControl(new[] { "GoodbyeDPI", "WinDivert", "WinDivert14" });
            });
        }
        else
        {
            Application.Current.Shutdown();
        }
    }

    // ------------------------------------------------------------------
    // Startup: cleanup + DNS poisoning check + initial state detection
    // ------------------------------------------------------------------
    private async Task StartupSequenceAsync()
    {
        await Task.Run(() => ProcessHelper.ScControl(new[] { "WinDivert", "WinDivert14", "monkey" }));
        SetStatus("DNS KONTROL EDİLİYOR...", StatusLevel.Warning);

        while (true)
        {
            bool poisoned = await DnsHelper.CheckDnsPoisoningSilentAsync();
            if (poisoned)
            {
                EnterDnsPoisonedState();

                if (_dnsChangedByApp)
                {
                    MessageBox.Show(
                        "Düz DNS değişikliği işe yaramadı, port 53'e müdahale var.\n\n" +
                        "Yapabilecekleriniz:\n" +
                        "1- Program içinden dnscrypt-proxy servisi kurmak\n" +
                        "2- YogaDNS gibi harici bir DNS istemcisi kullanmak",
                        "Kritik Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    break;
                }

                var answer = MessageBox.Show(
                    "ISS tarafından DNS zehirlenmesi saptandı!\n\n" +
                    "Sistem DNS adresiniz Cloudflare (1.1.1.1 / 1.0.0.1) olarak ayarlansın mı?",
                    "DNS Zehirlenmesi Saptandı", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (answer == MessageBoxResult.Yes)
                {
                    SetStatus("DNS AYARLANIYOR...", StatusLevel.Warning);
                    await Task.Run(() => DnsHelper.SetSystemDns(new[] { "1.1.1.1", "1.0.0.1" }));
                    _dnsChangedByApp = true;
                    SetStatus("DNS TEST EDİLİYOR...", StatusLevel.Warning);
                    await Task.Delay(1000);
                    continue;
                }
                break;
            }

            await CheckServiceStatusAsync();
            BtnDnscrypt.IsEnabled = true;
            RefreshStrategyLabel();
            break;
        }

        await CheckDnscryptStatusUiAsync();
    }

    // ------------------------------------------------------------------
    // Engine / strategy helpers
    // ------------------------------------------------------------------
    private async void CmbEngine_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        string sel = CmbEngine.SelectedItem as string ?? "";
        _currentEngine = sel.Contains("Eski") ? "Zapret1" : "Zapret2";

        CmbStrategy.Items.Clear();
        foreach (var s in _strategyOrder[_currentEngine]) CmbStrategy.Items.Add(s);
        CmbStrategy.SelectedItem = "Analiz Sonucu";

        LblFooter.Text = _currentEngine == "Zapret1" ? "Zapret v72.12" : "Zapret2 v1.0.2";
        RefreshStrategyLabel();
        await CheckServiceStatusAsync();
    }

    private void CmbStrategy_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        RefreshStrategyLabel();
    }

    private string GetActiveStrategyString()
    {
        string sel = CmbStrategy.SelectedItem as string ?? "";
        if (sel == "Analiz Sonucu" || string.IsNullOrEmpty(sel))
        {
            string f = _paths.StrategyFile(_currentEngine);
            return File.Exists(f) ? File.ReadAllText(f).Trim() : "";
        }
        return _strategies[_currentEngine].TryGetValue(sel, out var v) ? v : "";
    }

    private bool HasValidStrategy()
    {
        string f = _paths.StrategyFile(_currentEngine);
        return File.Exists(f) && File.ReadAllText(f).Trim().Length > 0;
    }

    private void RefreshStrategyLabel()
    {
        string sel = CmbStrategy.SelectedItem as string ?? "";
        if (sel == "Analiz Sonucu")
        {
            string state = HasValidStrategy() ? "Mevcut" : "Mevcut Değil (Analiz Gerekli)";
            LblStrategyInfo.Text = $"Strateji Durumu: {state}";
        }
        else
        {
            LblStrategyInfo.Text = $"Strateji Durumu: Hazır Profil ({sel})";
        }
    }

    private string FilterModeSelection() => CmbFilter.SelectedItem as string ?? "";

    // ------------------------------------------------------------------
    // winws.exe argument building
    // ------------------------------------------------------------------
    private (string Exe, List<string> Args, string Error) BuildWinwsCommand()
    {
        string strategy = GetActiveStrategyString();
        if (string.IsNullOrEmpty(strategy))
            return (null, null, "Strateji bulunamadı. Önce analiz yapın.");

        string exe = _paths.WinwsExe(_currentEngine);
        if (!File.Exists(exe))
            return (null, null, $"WinWS exe dosyası bulunamadı:\n{exe}");

        var strategyTokens = StrategyParser.TokenizeStrategy(strategy);

        static string WfFlagName(string tok)
        {
            int eq = tok.IndexOf('=');
            return eq > 0 ? tok[..eq] : tok;
        }
        var strategyWfFlags = new HashSet<string>(strategyTokens.Where(t => t.StartsWith("--wf-")).Select(WfFlagName));

        var args = new List<string>();
        if (_currentEngine == "Zapret2")
        {
            string luaDir = Path.Combine(_paths.WinwsDir, "lua");
            var defaultWf = new (string Flag, string Val)[]
            {
                ("--wf-l3", "--wf-l3=ipv4"),
                ("--wf-tcp-out", $"--wf-tcp-out={WfTcpPorts}"),
                ("--wf-udp-out", $"--wf-udp-out={WfUdpPorts}"),
            };
            args.AddRange(defaultWf.Where(d => !strategyWfFlags.Contains(d.Flag)).Select(d => d.Val));
            args.Add($"--hostlist-exclude={_paths.ExcludeList}");
            args.AddRange(strategyTokens);
            args.Add($"--lua-init=@{luaDir}\\zapret-lib.lua");
            args.Add($"--lua-init=@{luaDir}\\zapret-antidpi.lua");
            args.Add($"--lua-init=@{luaDir}\\zapret-auto.lua");
        }
        else
        {
            var defaultWf = new (string Flag, string Val)[]
            {
                ("--wf-tcp", $"--wf-tcp={WfTcpPorts}"),
                ("--wf-udp", $"--wf-udp={WfUdpPorts}"),
            };
            args.AddRange(defaultWf.Where(d => !strategyWfFlags.Contains(d.Flag)).Select(d => d.Val));
            args.Add($"--hostlist-exclude={_paths.ExcludeList}");
            args.AddRange(strategyTokens);
        }

        string filt = FilterModeSelection();
        if (filt.Contains("Auto"))
        {
            if (!File.Exists(_paths.HostlistAuto)) File.WriteAllText(_paths.HostlistAuto, "");
            args.Add($"--hostlist-auto={_paths.HostlistAuto}");
        }
        else if (filt.Contains("Manuel"))
        {
            if (!File.Exists(_paths.HostlistManual))
                File.WriteAllText(_paths.HostlistManual, "discord.com\nupdates.discord.com");
            args.Add($"--hostlist={_paths.HostlistManual}");
        }

        return (exe, args, null);
    }

    // ------------------------------------------------------------------
    // Start / stop zapret
    // ------------------------------------------------------------------
    private async void BtnRun_Click(object sender, RoutedEventArgs e)
    {
        if (!_isZapretRunning) await StartZapretAsync();
        else await StopZapretAsync();
    }

    private async Task StartZapretAsync()
    {
        var (exe, args, err) = BuildWinwsCommand();
        if (err != null)
        {
            MessageBox.Show(err, "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _zapretProc = ProcessHelper.RunBackground(exe, args.ToArray(), _paths.WinwsDir);
        if (_zapretProc == null)
        {
            MessageBox.Show("WinWS exe dosyası bulunamadı veya çalıştırılamadı!", "Dosya Bulunamadı",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _isZapretRunning = true;
        BtnRun.Content = "DURDUR";
        SetStatus("MANUEL MOD AKTİF", StatusLevel.Warning);
        SetControlsEnabled(_otherControls, false);

        await Task.Delay(600);
        if (_zapretProc.HasExited)
            await OnZapretFailedToStartAsync();
    }

    private async Task OnZapretFailedToStartAsync()
    {
        _isZapretRunning = false;
        _zapretProc = null;
        BtnRun.Content = "ZAPRET'İ BAŞLAT";
        await CheckServiceStatusAsync();
        MessageBox.Show(
            "Zapret motoru başlatılamadı veya anında çöktü!\n\n" +
            "Sisteminizdeki Anti-Cheat yazılımları Zapret'i tehdit olarak algılayıp kapatmış olabilir.",
            "Başlatma Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private async Task StopZapretAsync()
    {
        if (_zapretProc != null && !_zapretProc.HasExited)
        {
            try { _zapretProc.Kill(true); } catch { /* already gone */ }
        }

        await Task.Run(async () =>
        {
            foreach (var name in new[] { "winws2.exe", "winws.exe" })
                await ProcessHelper.WaitUntilGoneAsync(name);
            ProcessHelper.ScControl(new[] { "WinDivert", "WinDivert14", "monkey" });
        });

        _isZapretRunning = false;
        _zapretProc = null;
        BtnRun.Content = "ZAPRET'İ BAŞLAT";
        await CheckServiceStatusAsync();
    }

    // ------------------------------------------------------------------
    // Service install / remove / status
    // ------------------------------------------------------------------
    private async void BtnInstallService_Click(object sender, RoutedEventArgs e)
    {
        var (exe, args, err) = BuildWinwsCommand();
        if (err != null)
        {
            MessageBox.Show(err, "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string binPathCmdline = CommandLine.ArgvToCommandLine(new[] { exe }.Concat(args));

        int code = await Task.Run(() =>
        {
            RemoveServiceRaw();
            Thread.Sleep(500);
            var (c, _) = ProcessHelper.RunCaptured("sc",
                new[] { "create", ServiceName, "binPath=", binPathCmdline, "start=", "auto" });
            if (c == 0) ProcessHelper.RunCaptured("sc", new[] { "start", ServiceName });
            return c;
        });

        if (code == 0)
        {
            MessageBox.Show("Seçilen profil servis olarak kuruldu.", "Başarılı",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            Logger.Warning($"Servis kurulamadı (sc create dönüş kodu={code}).");
            MessageBox.Show("Servis kurulamadı. Yönetici izinlerini kontrol edin.", "Hata",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        await CheckServiceStatusAsync();
    }

    private static void RemoveServiceRaw() =>
        ProcessHelper.ScControl(new[] { ServiceName, "WinDivert", "WinDivert14", "monkey" });

    private async void BtnRemoveService_Click(object sender, RoutedEventArgs e)
    {
        await Task.Run(RemoveServiceRaw);

        if (File.Exists(_paths.DnscryptExe))
        {
            await Task.Run(() =>
            {
                ProcessHelper.RunCaptured(_paths.DnscryptExe, new[] { "-service", "stop" }, _paths.DnscryptDir);
                ProcessHelper.RunCaptured(_paths.DnscryptExe, new[] { "-service", "uninstall" }, _paths.DnscryptDir);
            });
        }
        _dnscryptActive = false;
        BtnDnscrypt.Content = "Dnscrypt-proxy servisi kur";
        _cfg.Set("DnscryptInstalled", "0");
        _cfg.Save();

        SetStatus("SİSTEM SIFIRLANIYOR...", StatusLevel.Warning);
        await Task.Run(() => DnsHelper.ResetSystemDns());

        bool poisoned = await DnsHelper.CheckDnsPoisoningSilentAsync();
        if (!poisoned) await CheckServiceStatusAsync();
        RefreshStrategyLabel();
        MessageBox.Show("WinDivert, Zapret, Dnscrypt temizlendi ve DNS ayarları otomatiğe (DHCP) alındı.",
            "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);

        if (poisoned) EnterDnsPoisonedState();
    }

    private async Task CheckServiceStatusAsync()
    {
        var (_, outText) = await Task.Run(() => ProcessHelper.RunCaptured("sc", new[] { "query", ServiceName }));

        if (outText.Contains("SERVICE_NAME"))
        {
            var (_, cfgOut) = await Task.Run(() => ProcessHelper.RunCaptured("sc", new[] { "qc", ServiceName }));
            if (cfgOut.Contains("--hostlist-auto")) CmbFilter.SelectedItem = "Auto (Zapret oluşturur)";
            else if (cfgOut.Contains("--hostlist=")) CmbFilter.SelectedItem = "Manuel ('hostlist.txt' İçeriği)";
            else CmbFilter.SelectedItem = "Kapalı (Tüm trafik)";

            BtnRun.IsEnabled = false;
            SetControlsEnabled(_criticalControls, false);
            BtnDnscrypt.IsEnabled = true;

            if (outText.Contains("RUNNING"))
            {
                SetStatus("SERVİS MODU AKTİF", StatusLevel.Success);
            }
            else
            {
                SetStatus("SERVİS YÜKLÜ - DURDU", StatusLevel.Error);
                if (!_serviceStoppedWarned)
                {
                    _serviceStoppedWarned = true;
                    MessageBox.Show(
                        "Zapret servisi sisteme yüklü ancak şu anda DURMUŞ durumda!\n\n" +
                        "Sisteminizdeki Anti-Cheat yazılımları Zapret'i tehdit olarak algılayıp kapatmış olabilir.",
                        "Uyarı: Servis Durdurulmuş", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
        else
        {
            BtnRun.IsEnabled = true;
            SetControlsEnabled(_criticalControls, true);
            SetStatus("SİSTEM HAZIR", StatusLevel.Neutral);
        }
    }

    // ------------------------------------------------------------------
    // DNSCrypt-proxy
    // ------------------------------------------------------------------
    private async void BtnDnscrypt_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(_paths.DnscryptExe))
        {
            MessageBox.Show($"DNSCrypt dosyası bulunamadı!\n{_paths.DnscryptExe}", "Hata",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        bool currentlyActive = _dnscryptActive;

        if (!currentlyActive)
        {
            SetStatus("DNSCRYPT KURULUYOR...", StatusLevel.Warning);
            await Task.Run(() =>
            {
                ProcessHelper.RunCaptured(_paths.DnscryptExe, new[] { "-service", "install" }, _paths.DnscryptDir);
                ProcessHelper.RunCaptured(_paths.DnscryptExe, new[] { "-service", "start" }, _paths.DnscryptDir);
                DnsHelper.SetDnsLocalhost();
            });
            _dnscryptActive = true;
            BtnDnscrypt.Content = "[ ✓ ] Dnscrypt-proxy Aktif / Kaldır";
            _cfg.Set("DnscryptInstalled", "1");
        }
        else
        {
            SetStatus("DNSCRYPT KALDIRILIYOR...", StatusLevel.Warning);
            await Task.Run(() =>
            {
                ProcessHelper.RunCaptured(_paths.DnscryptExe, new[] { "-service", "stop" }, _paths.DnscryptDir);
                ProcessHelper.RunCaptured(_paths.DnscryptExe, new[] { "-service", "uninstall" }, _paths.DnscryptDir);
                DnsHelper.ResetSystemDns();
            });
            _dnscryptActive = false;
            BtnDnscrypt.Content = "Dnscrypt-proxy servisi kur";
            _cfg.Set("DnscryptInstalled", "0");
        }
        _cfg.Save();

        SetStatus("DNS TEST EDİLİYOR...", StatusLevel.Warning);
        await Task.Delay(1000);
        bool poisoned = await DnsHelper.CheckDnsPoisoningSilentAsync();
        if (poisoned)
        {
            EnterDnsPoisonedState();
        }
        else
        {
            await CheckServiceStatusAsync();
            BtnDnscrypt.IsEnabled = true;
            RefreshStrategyLabel();
        }
    }

    private async Task CheckDnscryptStatusUiAsync()
    {
        bool active = await Task.Run(DnsHelper.CheckDnscryptStatus);
        _dnscryptActive = active;
        BtnDnscrypt.Content = active ? "[ ✓ ] Dnscrypt-proxy Aktif / Kaldır" : "Dnscrypt-proxy servisi kur";
        BtnDnscrypt.IsEnabled = true;
        _cfg.Set("DnscryptInstalled", active ? "1" : "0");
        _cfg.Save();
    }

    // ------------------------------------------------------------------
    // LAN sharing (go-pcap2socks)
    // ------------------------------------------------------------------
    private async void BtnLanShare_Click(object sender, RoutedEventArgs e)
    {
        bool currentlyActive = _lanShareActive;

        if (!currentlyActive)
        {
            string npcapDll = Path.Combine(
                Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows", "System32", "Packet.dll");
            if (!File.Exists(npcapDll))
            {
                MessageBox.Show(
                    "HATA: Sistemde Npcap sürücüsü bulunamadı!\n\n" +
                    "go-pcap2socks motorunun çalışabilmesi için Npcap kurulmalıdır.\n" +
                    "Lütfen npcap.com adresinden indirip kurun.",
                    "Eksik Bileşen", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (!File.Exists(_paths.PcapExe))
            {
                MessageBox.Show($"go-pcap2socks.exe bulunamadı:\n{_paths.PcapExe}", "Eksik Bileşen",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            bool hasRule = await Task.Run(() => DnsHelper.HasFirewallRule(_paths.PcapExe));
            if (!hasRule)
            {
                await Task.Run(() =>
                {
                    ProcessHelper.RunCaptured("powershell", new[]
                    {
                        "-NoProfile", "-Command",
                        $"Remove-NetFirewallRule -Program {ProcessHelper.PsQuote(_paths.PcapExe)} -ErrorAction SilentlyContinue"
                    });
                    ProcessHelper.RunCaptured("netsh", new[]
                    {
                        "advfirewall", "firewall", "delete", "rule", "name=all", $"program=\"{_paths.PcapExe}\""
                    });
                });
                MessageBox.Show(
                    "go-pcap2socks için ağ izni gerekiyor.\n\n" +
                    "1. Birazdan Windows Güvenlik Duvarı uyarısı gelecektir.\n" +
                    "2. Lütfen gelen ekranda kutucukları işaretleyip 'Erişime izin ver' (Allow) butonuna basın.",
                    "Güvenlik Duvarı İzni", MessageBoxButton.OK, MessageBoxImage.Information);

                var tmp = ProcessHelper.RunBackground(_paths.PcapExe, Array.Empty<string>(), _paths.PcapDir);
                MessageBox.Show("Güvenlik duvarı iznini onayladıysanız Tamam'a basın.", "Onay",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                if (tmp != null) { try { tmp.Kill(true); } catch { /* ignore */ } }
            }

            if (!ProcessHelper.ProcessExists("go-pcap2socks.exe"))
                _pcapProc = ProcessHelper.RunBackground(_paths.PcapExe, Array.Empty<string>(), _paths.PcapDir);

            await Task.Delay(500);
            if (!ProcessHelper.ProcessExists("go-pcap2socks.exe"))
            {
                _lanShareActive = false;
                BtnLanShare.Content = "Ağdaki Cihazlarla Paylaş";
                _pcapProc = null;
                MessageBox.Show(
                    "HATA: go-pcap2socks başlatılamadı!\n\n" +
                    "• Sanal bir ağ kartı oluşturan VPN vb. program kullanıyorsanız kapatın.",
                    "Başlatma Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _lanShareActive = true;
            BtnLanShare.Content = "[ ✓ ] Paylaşım Aktif / Durdur";
            MessageBox.Show(
                "Ağdaki diğer cihazınızda şu IP ayarlarını giriniz:\n\n" +
                "• IP Aralığı: 172.24.2.10 - 172.24.2.255\n" +
                "• Ağ Geçidi (Gateway): 172.24.2.1\n" +
                "• Alt Ağ Maskesi (Maske): 255.255.0.0\n" +
                "• DNS 1: 1.1.1.1\n" +
                "• DNS 2: 8.8.8.8",
                "go-pcap2socks Ağ Yapılandırması", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            if (ProcessHelper.ProcessExists("go-pcap2socks.exe")) ProcessHelper.KillProcess("go-pcap2socks.exe");
            _pcapProc = null;
            _lanShareActive = false;
            BtnLanShare.Content = "Ağdaki Cihazlarla Paylaş";
        }
    }

    // ------------------------------------------------------------------
    // ISS analysis (blockcheck)
    // ------------------------------------------------------------------
    private async void BtnAnalyze_Click(object sender, RoutedEventArgs e)
    {
        var res = MessageBox.Show("Analiz işlemi 5-10 dakika sürebilir.\nLütfen bitene kadar bekleyin.",
            "Bilgi", MessageBoxButton.OKCancel, MessageBoxImage.Information);
        if (res != MessageBoxResult.OK) return;

        SetControlsEnabled(_criticalControls, false);
        BtnRemoveService.IsEnabled = false;
        SetStatus("ANALİZ YAPILIYOR...", StatusLevel.Warning);
        LblStrategyInfo.Text = "";
        Progress.Visibility = Visibility.Visible;

        await RunBlockcheckAsync(_currentEngine);

        Progress.Visibility = Visibility.Collapsed;
        SetControlsEnabled(_criticalControls, true);
        BtnRemoveService.IsEnabled = true;
        await CheckServiceStatusAsync();
        SetStatus("ANALİZ TAMAMLANDI", StatusLevel.Neutral);
        RefreshStrategyLabel();
    }

    private async Task RunBlockcheckAsync(string engine)
    {
        string logPath = _paths.BlockcheckLog(engine);
        string scriptPath = _paths.BlockcheckScript(engine);

        if (File.Exists(logPath))
        {
            try { File.Delete(logPath); }
            catch (IOException) { Logger.Warning($"Eski blockcheck log dosyası silinemedi: {logPath}"); }
        }

        if (!File.Exists(_paths.CygwinBash) || !File.Exists(scriptPath))
        {
            MessageBox.Show("Blockcheck betikleri bulunamadı (cygwin/bash.exe veya blog.sh).", "Hata",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var proc = ProcessHelper.RunHiddenConsole(_paths.CygwinBash, new[] { "-i", scriptPath }, _paths.BlockcheckDir);
        var result = await RunBlockcheckWorkerAsync(engine, logPath, proc);

        if (result.ErrorMessage != null)
        {
            MessageBox.Show(result.ErrorMessage, result.ErrorTitle, MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (!string.IsNullOrEmpty(result.StrategyFound))
        {
            File.WriteAllText(_paths.StrategyFile(engine), result.StrategyFound);
            MessageBox.Show($"Analiz tamamlandı. {engine} için strateji ayıklandı.", "Başarılı",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else if (result.NoBypassNeeded)
        {
            MessageBox.Show("İnternet hattınızda herhangi bir DPI / Sansür kısıtlaması saptanmadı!",
                $"Bilgi: {engine} Analiz Tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(
                File.Exists(logPath) ? "Strateji bulunamadı veya zaman aşımı." : "Log dosyası oluşturulamadı.",
                "Başarısız", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Runs cygwin's "bash -i blog.sh" and polls its log file. Liveness is
    /// checked via BOTH our own Process handle AND process-name lookup,
    /// because cygwin's bash -i frequently hands the real work off to a
    /// different Windows PID than the one we captured with Process.Start -
    /// relying on only one signal can make this return "finished" before the
    /// log ever gets its "AVAILABLE" line written.
    /// </summary>
    private static async Task<BlockcheckResult> RunBlockcheckWorkerAsync(string engine, string logPath, Process proc)
    {
        if (proc == null)
            return new BlockcheckResult("", false, "Blockcheck süreci başlatılamadı.", "Hata");

        bool BlockcheckAlive() => !proc.HasExited || ProcessHelper.ProcessExists("bash.exe");
        static string ReadLog(string p) { try { return File.ReadAllText(p); } catch { return ""; } }

        string strategyFound = "";
        bool noBypassNeeded = false;

        var waitStart = DateTime.UtcNow;
        while (!File.Exists(logPath))
        {
            if (!BlockcheckAlive()) break;
            await Task.Delay(200);
            if ((DateTime.UtcNow - waitStart).TotalSeconds > 10) break;
        }

        var start = DateTime.UtcNow;
        while (BlockcheckAlive())
        {
            await Task.Delay(1000);
            string content = ReadLog(logPath);
            if (!string.IsNullOrEmpty(content))
            {
                if (content.Contains("working without bypass") || content.Contains("not blocked"))
                    noBypassNeeded = true;

                if (engine == "Zapret2")
                {
                    string filtered = content.Replace("iana.org", "IGNORE");
                    if (filtered.Contains("!!!!! AVAILABLE !!!!!"))
                    {
                        strategyFound = StrategyParser.GetLastStrategyFromText(content);
                        if (!string.IsNullOrEmpty(strategyFound)) break;
                    }
                }
                else if (noBypassNeeded)
                {
                    break;
                }
            }
            if ((DateTime.UtcNow - start).TotalSeconds > 600)
            {
                Logger.Warning($"{engine} blockcheck zaman aşımına uğradı.");
                break;
            }
        }

        if (!proc.HasExited)
        {
            try { proc.Kill(true); }
            catch (Exception ex) { Logger.Exception("Blockcheck süreci sonlandırılamadı.", ex); }
        }

        // The process can finish in the narrow window between the last
        // "still alive?" check and this point; re-read the log once more
        // before killing anything, to close that race.
        string finalContent = ReadLog(logPath);
        if (!string.IsNullOrEmpty(finalContent))
        {
            if (!noBypassNeeded && (finalContent.Contains("working without bypass") || finalContent.Contains("not blocked")))
                noBypassNeeded = true;
            if (string.IsNullOrEmpty(strategyFound) && engine == "Zapret2")
            {
                string filtered = finalContent.Replace("iana.org", "IGNORE");
                if (filtered.Contains("!!!!! AVAILABLE !!!!!"))
                    strategyFound = StrategyParser.GetLastStrategyFromText(finalContent);
            }
        }

        foreach (var name in new[] { "bash.exe", "sh.exe", "tee.exe", "winws.exe", "winws2.exe" })
            ProcessHelper.KillProcess(name);

        if (string.IsNullOrEmpty(strategyFound) && engine == "Zapret1" && !noBypassNeeded)
        {
            if (!File.Exists(logPath))
                return new BlockcheckResult("", false, "Log dosyası oluşturulamadı.", "Başarısız");
            strategyFound = StrategyParser.ExtractSummaryZapret1(File.ReadAllLines(logPath));
        }

        return new BlockcheckResult(strategyFound, noBypassNeeded, null, null);
    }

    // ------------------------------------------------------------------
    // Monitor loop (checks winws liveness every second)
    // ------------------------------------------------------------------
    private async Task MonitorTickAsync()
    {
        if (_monitorTickRunning) return;
        _monitorTickRunning = true;
        try
        {
            bool alive = await Task.Run(() =>
                ProcessHelper.ProcessExists("winws.exe") || ProcessHelper.ProcessExists("winws2.exe"));

            if (alive)
            {
                if (!BtnLanShare.IsEnabled) BtnLanShare.IsEnabled = true;
            }
            else
            {
                if (BtnLanShare.IsEnabled)
                {
                    BtnLanShare.IsEnabled = false;
                    if ((BtnLanShare.Content as string)?.Contains("Aktif") == true)
                    {
                        BtnLanShare.Content = "Ağdaki Cihazlarla Paylaş";
                        _lanShareActive = false;
                        if (ProcessHelper.ProcessExists("go-pcap2socks.exe"))
                            ProcessHelper.KillProcess("go-pcap2socks.exe");
                    }
                }

                if (_isZapretRunning)
                {
                    await StopZapretAsync();
                    MessageBox.Show(
                        "Zapret motoru beklenmedik bir şekilde çöktü veya dışarıdan sonlandırıldı!\n\n" +
                        "Sisteminizdeki Anti-Cheat yazılımları Zapret'i tehdit olarak algılayıp kapatmış olabilir.",
                        "Uyarı: Zapret Kapatıldı", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                if (LblStatus.Text == "SERVİS MODU AKTİF")
                    await CheckServiceStatusAsync();
            }
        }
        finally
        {
            _monitorTickRunning = false;
        }
    }

    // ------------------------------------------------------------------
    // Window / tray behavior
    // ------------------------------------------------------------------
    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (_exiting) return;
        if (WindowState == WindowState.Minimized) HideToTray();
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        // The X button really quits (minimizing still sends it to the tray).
        if (_exiting) return;
        e.Cancel = true;
        ExitApp();
    }

    private void HideToTray()
    {
        Hide();
        EnsureTrayIcon();
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void EnsureTrayIcon()
    {
        if (_trayIcon != null)
        {
            _trayIcon.Icon = MakeTrayIcon();
            _trayIcon.Visible = true;
            return;
        }

        _trayIcon = new WinForms.NotifyIcon
        {
            Icon = MakeTrayIcon(),
            Visible = true,
            Text = "Zapret Windows Türkiye",
        };
        _trayIcon.DoubleClick += (_, _) => ShowFromTray();

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Göster", null, (_, _) => ShowFromTray());
        menu.Items.Add("Zapret'i Başlat/Durdur", null, async (_, _) =>
        {
            if (!_isZapretRunning) await StartZapretAsync();
            else await StopZapretAsync();
        });
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Kapat", null, (_, _) => ExitApp());
        _trayIcon.ContextMenuStrip = menu;
    }

    private Drawing.Icon MakeTrayIcon()
    {
        if (File.Exists(_paths.IconIco))
        {
            try { return new Drawing.Icon(_paths.IconIco); }
            catch { /* fall through to the drawn fallback */ }
        }

        using var bmp = new Drawing.Bitmap(32, 32);
        using (var g = Drawing.Graphics.FromImage(bmp))
        {
            g.Clear(Drawing.Color.Transparent);
            var color = _isZapretRunning ? Drawing.Color.FromArgb(39, 174, 96) : Drawing.Color.FromArgb(44, 62, 80);
            using var brush = new Drawing.SolidBrush(color);
            g.FillEllipse(brush, 2, 2, 28, 28);
            using var font = new Drawing.Font("Segoe UI", 12, Drawing.FontStyle.Bold);
            g.DrawString("Z", font, Drawing.Brushes.White, 9, 6);
        }
        return Drawing.Icon.FromHandle(bmp.GetHicon());
    }

    private void ExitApp()
    {
        if (_exiting) return;
        _exiting = true;

        SaveConfig();
        if (_isZapretRunning) _ = StopZapretAsync(); // best effort during shutdown, not awaited
        if (ProcessHelper.ProcessExists("go-pcap2socks.exe")) ProcessHelper.KillProcess("go-pcap2socks.exe");

        _monitorTimer?.Stop();
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }

        Application.Current.Shutdown();
    }

    private void ChkDarkMode_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        bool dark = ChkDarkMode.IsChecked == true;
        ThemeManager.Apply(dark ? AppTheme.Dark : AppTheme.Light);
        _cfg.Set("DarkMode", dark ? "1" : "0");
        _cfg.Save();
    }

    private void SaveConfig()
    {
        _cfg.Set("ActiveEngine", _currentEngine == "Zapret1" ? "1" : "0");
        _cfg.Set("ActiveStrategy", CmbStrategy.SelectedItem as string ?? "Analiz Sonucu");
        string filt = FilterModeSelection();
        _cfg.Set("FilterMode", filt.Contains("Auto") ? "1" : (filt.Contains("Manuel") ? "2" : "0"));
        _cfg.Set("DnscryptInstalled", _dnscryptActive ? "1" : "0");
        _cfg.Save();
    }
}
