using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace ZapretGuiWpf.Helpers;

public static class DnsHelper
{
    // Filters out virtual/VPN adapters so DNS changes hit the real network adapter.
    public const string AdapterFilterPs =
        "$adapter = Get-NetAdapter | Where-Object {$_.Status -eq 'Up' " +
        "-and $_.InterfaceDescription -notlike '*Virtual*' -and $_.InterfaceDescription -notlike '*Hyper-V*' " +
        "-and $_.InterfaceDescription -notlike '*WSL*' -and $_.Name -notlike '*vEthernet*' " +
        "-and $_.InterfaceDescription -notlike '*TAP*' -and $_.InterfaceDescription -notlike '*TUN*' " +
        "-and $_.InterfaceDescription -notlike '*VPN*'} | Select-Object -First 1; ";

    private const string DohResolverIp = "1.1.1.1";

    // (test domain, known-clean CDN IP prefix)
    private static readonly (string Domain, string SafePrefix)[] PoisonTestDomains =
    {
        ("updates.discord.com", "162.159"),
        ("discord.com", "162.159"),
    };

    private static readonly HashSet<string> KnownPoisonIps = new() { "195.175.254.2" };

    private static readonly HttpClient DohClient = new(new HttpClientHandler())
    {
        Timeout = TimeSpan.FromSeconds(5),
        BaseAddress = new Uri($"https://{DohResolverIp}/"),
    };

    /// <summary>
    /// Resolves a domain via Cloudflare's DoH endpoint by IP, bypassing the
    /// local/ISP resolver entirely (so the result can't be affected by DNS
    /// poisoning). Returns null if the DoH request itself fails/is blocked.
    /// </summary>
    private static async Task<List<string>> ResolveViaDohAsync(string domain)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, $"dns-query?name={Uri.EscapeDataString(domain)}&type=A");
            req.Headers.Add("accept", "application/dns-json");
            using var resp = await DohClient.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;

            using var stream = await resp.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            if (!doc.RootElement.TryGetProperty("Answer", out var answers)) return null;

            var ips = new List<string>();
            foreach (var a in answers.EnumerateArray())
            {
                if (a.TryGetProperty("type", out var t) && t.GetInt32() == 1 && a.TryGetProperty("data", out var d))
                    ips.Add(d.GetString());
            }
            return ips.Count > 0 ? ips : null;
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveLocal(string domain)
    {
        try
        {
            var addr = Dns.GetHostAddresses(domain).FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            return addr?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static async Task<bool> DomainPoisonVoteAsync(string domain, string safePrefix)
    {
        string localIp = ResolveLocal(domain);
        var dohIps = await ResolveViaDohAsync(domain);

        if (dohIps != null)
        {
            if (localIp != null && (dohIps.Contains(localIp) || localIp.StartsWith(safePrefix)))
                return false;
            return true;
        }

        // DoH unreachable: fall back to the older heuristic.
        if (localIp == null) return true;
        if (localIp.StartsWith(safePrefix)) return false;
        if (KnownPoisonIps.Contains(localIp)) return true;
        return true; // neither known-clean nor known-poisoned: be cautious
    }

    /// <summary>
    /// True = DNS poisoning suspected. Votes across multiple test domains;
    /// majority "poisoned" wins. A tie is treated as "not poisoned" to avoid
    /// false positives locking the user out unnecessarily.
    /// </summary>
    public static async Task<bool> CheckDnsPoisoningSilentAsync()
    {
        var votes = new List<bool>();
        foreach (var (domain, prefix) in PoisonTestDomains)
            votes.Add(await DomainPoisonVoteAsync(domain, prefix));

        int poisoned = votes.Count(v => v);
        return poisoned > votes.Count / 2.0;
    }

    public static void SetSystemDns(IEnumerable<string> servers)
    {
        string serverList = string.Join(", ", servers.Select(ProcessHelper.PsQuote));
        string script = AdapterFilterPs +
            $"Set-DnsClientServerAddress -InterfaceAlias $adapter.Name -ServerAddresses {serverList}; ipconfig /flushdns;";
        ProcessHelper.PsRun(script);
    }

    public static void ResetSystemDns()
    {
        string script = AdapterFilterPs +
            "Set-DnsClientServerAddress -InterfaceAlias $adapter.Name -ResetServerAddresses; ipconfig /flushdns;";
        ProcessHelper.PsRun(script);
    }

    public static void SetDnsLocalhost()
    {
        string script = AdapterFilterPs +
            "Set-DnsClientServerAddress -InterfaceAlias $adapter.Name -ServerAddresses '127.0.0.1','::1'; ipconfig /flushdns;";
        ProcessHelper.PsRun(script);
    }

    public static bool CheckDnscryptStatus()
    {
        var (_, scOut) = ProcessHelper.RunCaptured("sc", new[] { "query", "dnscrypt-proxy" });
        string script = AdapterFilterPs +
            "if ($adapter) { (Get-DnsClientServerAddress -InterfaceAlias $adapter.Name).ServerAddresses -join ',' }";
        var (_, dnsOut) = ProcessHelper.PsRun(script);
        return scOut.Contains("RUNNING") && dnsOut.Contains("127.0.0.1");
    }

    public static bool HasFirewallRule(string exePath)
    {
        string script =
            $"(Get-NetFirewallApplicationFilter -Program {ProcessHelper.PsQuote(exePath)} -ErrorAction SilentlyContinue " +
            "| Get-NetFirewallRule | Where-Object { $_.Action -eq 'Allow' -and $_.Enabled -eq 'True' } " +
            "| Measure-Object).Count";
        var (_, output) = ProcessHelper.PsRun(script);
        return int.TryParse(output.Trim(), out int count) && count > 0;
    }
}
