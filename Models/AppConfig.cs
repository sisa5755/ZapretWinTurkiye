using System;
using System.Collections.Generic;
using System.IO;
using ZapretGuiWpf.Helpers;

namespace ZapretGuiWpf.Models;

/// <summary>
/// Tiny INI reader/writer, compatible with the Python version's
/// configparser-based config.ini ([Settings] section with key=value
/// pairs), so an existing config.ini from the Python app keeps working.
/// </summary>
public sealed class AppConfig
{
    private readonly string _path;
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    public AppConfig(string iniPath)
    {
        _path = iniPath;
        if (File.Exists(_path))
        {
            try
            {
                bool inSettings = false;
                foreach (var rawLine in File.ReadAllLines(_path))
                {
                    string line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        inSettings = string.Equals(line.Trim('[', ']'), "Settings", StringComparison.OrdinalIgnoreCase);
                        continue;
                    }
                    if (!inSettings) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line[..eq].Trim();
                    string val = line[(eq + 1)..].Trim();
                    _values[key] = val;
                }
            }
            catch (Exception ex)
            {
                Logger.Exception($"config.ini okunamadı, varsayılan ayarlarla devam edilecek: {_path}", ex);
            }
        }
    }

    public string Get(string key, string defaultValue = "") =>
        _values.TryGetValue(key, out var v) ? v : defaultValue;

    public void Set(string key, object value) => _values[key] = Convert.ToString(value) ?? "";

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            using var writer = new StreamWriter(_path, false);
            writer.WriteLine("[Settings]");
            foreach (var kv in _values)
                writer.WriteLine($"{kv.Key} = {kv.Value}");
        }
        catch (Exception ex)
        {
            Logger.Exception($"config.ini yazılamadı: {_path}", ex);
        }
    }
}
