using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ZapretGuiWpf.Helpers;

namespace ZapretGuiWpf.Models;

public static class Strategies
{
    /// <summary>
    /// Same defaults as Python's DEFAULT_STRATEGIES: engine -> (ISS/profile name -> winws args string).
    /// </summary>
    public static readonly Dictionary<string, Dictionary<string, string>> Default = new()
    {
        ["Zapret2"] = new Dictionary<string, string>
        {
            ["Turk Telekom"] = "--payload=tls_client_hello --lua-desync=multidisorder:pos=2:seqovl=1",
            ["Turk Telekom Alternatif 1"] = "--payload=tls_client_hello --lua-desync=multisplit:blob=fake_default_tls:ip_ttl=5:pos=1:nodrop:repeats=2",
            ["Superonline"] = "--payload=tls_client_hello --lua-desync=multidisorder:pos=2:seqovl=1",
            ["Vodafone"] = "--payload=tls_client_hello --lua-desync=multisplit:blob=fake_default_tls:ip_ttl=5:pos=2:nodrop:repeats=1",
            ["Turksat Kablonet"] = "--payload=tls_client_hello --lua-desync=multidisorder:pos=2:seqovl=1",
            ["Telekom Mobil"] = "--payload=tls_client_hello --lua-desync=fake:blob=0x00000000:ip_ttl=5:repeats=1",
            ["Turkcell Mobil"] = "--payload=tls_client_hello --lua-desync=multidisorder:pos=2:seqovl=1",
            ["Vodafone Mobil"] = "--payload=tls_client_hello --lua-desync=multidisorder:pos=2:seqovl=1",

            ["Standart"] = "--wf-l3=ipv4 --payload=tls_client_hello --lua-desync=fake:blob=fake_default_tls:tcp_md5:tls_mod=rnd,rndsni,dupsid",
            ["Alternatif 1"] = "--wf-l3=ipv4 --payload=tls_client_hello --lua-desync=multisplit:pos=1:seqovl=5:seqovl_pattern=0x1603030000",
            ["Alternatif 2"] = "--wf-l3=ipv4 --payload=tls_client_hello --in-range=-s1 --lua-desync=oob:urp=b",
            ["Alternatif 3"] = "--wf-l3=ipv4 --payload=tls_client_hello --lua-desync=multidisorder:pos=midsld:seqovl=midsld-1:repeats=2",
            ["Alternatif 4"] = "--wf-l3=ipv4 --payload=tls_client_hello --lua-desync=multidisorder:pos=1,midsld:seqovl=1",
            ["Alternatif 5"] = "--wf-l3=ipv4 --payload=tls_client_hello --lua-desync=multidisorder:pos=2:seqovl=1",
            ["Alternatif 6"] = "--wf-l3=ipv4 --payload=tls_client_hello --lua-desync=multisplit:blob=fake_default_tls:ip_ttl=6:pos=2:nodrop:repeats=2",
            ["Alternatif 7"] = "--wf-l3=ipv4 --payload=tls_client_hello --lua-desync=fake:blob=0x00000000:ip_ttl=5:repeats=2",
        },
        ["Zapret1"] = new Dictionary<string, string>
        {
            ["Turk Telekom"] = "--dpi-desync=fake --dpi-desync-ttl=4",
            ["TT Alternatif"] = "--dpi-desync=fake --dpi-desync-ttl=3",
            ["Superonline"] = "--dpi-desync=fake --dpi-desync-fooling=md5sig",
            ["SOL Alternatif"] = "--dpi-desync=fake --dpi-desync-fooling=md5sig --dpi-desync-ttl=3",
            ["Vodafone"] = "--dpi-desync=fake,multisplit --dpi-desync-fooling=badseq --dpi-desync-split-pos=1,midsld",
            ["Turkcell Mobil"] = "--dpi-desync=fake --dpi-desync-ttl=1 --dpi-desync-autottl=3",
            ["Vodafone Mobil"] = "--dpi-desync=multisplit --dpi-desync-split-pos=2",
            ["Telekom Mobil"] = "--dpi-desync=fake --dpi-desync-ttl=5",
        },
    };

    /// <summary>
    /// Reads config/strategies.json, creating it with defaults if missing, and
    /// back-filling any engine/profile keys that are missing (same merge
    /// behavior as the Python load_strategies()): user edits are never
    /// overwritten, but new built-in profiles introduced by an app update
    /// still show up even with an old strategies.json on disk.
    /// </summary>
    public static Dictionary<string, Dictionary<string, string>> Load(string configDir)
    {
        string path = Path.Combine(configDir, "strategies.json");

        if (File.Exists(path))
        {
            try
            {
                string json = File.ReadAllText(path);
                var data = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json);
                if (data != null && data.Count > 0)
                {
                    foreach (var (engine, defaults) in Default)
                    {
                        if (!data.TryGetValue(engine, out var engineDict) || engineDict == null || engineDict.Count == 0)
                        {
                            data[engine] = new Dictionary<string, string>(defaults);
                            continue;
                        }
                        foreach (var (name, value) in defaults)
                            if (!engineDict.ContainsKey(name))
                                engineDict[name] = value;
                    }
                    return data;
                }
                Logger.Warning("strategies.json geçerli bir profil listesi içermiyor, varsayılanlar kullanılacak.");
            }
            catch (Exception ex)
            {
                Logger.Exception("strategies.json okunamadı, varsayılan stratejiler kullanılacak.", ex);
            }
        }
        else
        {
            try
            {
                Directory.CreateDirectory(configDir);
                var opts = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(path, JsonSerializer.Serialize(Default, opts));
            }
            catch (Exception ex)
            {
                Logger.Exception("strategies.json oluşturulamadı (salt-okunur klasör olabilir).", ex);
            }
        }

        return Default;
    }
}
