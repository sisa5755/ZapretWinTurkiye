using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ZapretGuiWpf.Helpers;

public static class StrategyParser
{
    /// <summary>
    /// Turns "--param=value" chunks into "--param="value"" form (needed for
    /// values containing spaces/commas when they're re-tokenized later).
    /// </summary>
    public static string FormatStrategyQuotes(string raw)
    {
        var outParts = new List<string>();
        foreach (var p in raw.Split(' '))
        {
            if (p.Length == 0) continue;
            int eq = p.IndexOf('=');
            if (eq > 0)
            {
                string name = p[..(eq + 1)];
                string value = p[(eq + 1)..];
                outParts.Add($"{name}\"{value}\"");
            }
            else
            {
                outParts.Add(p);
            }
        }
        return string.Join(" ", outParts).Trim();
    }

    /// <summary>
    /// Splits on whitespace while keeping quoted spans together, equivalent
    /// to Python's shlex.split(strategy, posix=False) (quote characters are
    /// preserved in the raw split, then stripped below - this mirrors
    /// backslashes in Windows paths not being mangled the way posix mode would).
    /// </summary>
    private static List<string> SplitPreservingQuotes(string s)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;
        bool any = false;

        foreach (char c in s)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                current.Append(c);
                any = true;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (any) { tokens.Add(current.ToString()); current.Clear(); any = false; }
            }
            else
            {
                current.Append(c);
                any = true;
            }
        }
        if (any) tokens.Add(current.ToString());
        return tokens;
    }

    /// <summary>
    /// Converts a strategy string into tokens ready to pass straight into
    /// ProcessStartInfo.ArgumentList (no shell involved, so no quoting/
    /// injection risk - .NET escapes each argv element itself).
    /// </summary>
    public static List<string> TokenizeStrategy(string strategy)
    {
        if (string.IsNullOrWhiteSpace(strategy)) return new List<string>();

        var tokens = SplitPreservingQuotes(strategy);
        var cleaned = new List<string>();
        foreach (var tok in tokens)
        {
            string t = tok;
            if (t.Length >= 2 && t[0] == '"' && t[^1] == '"')
            {
                t = t[1..^1];
            }
            else
            {
                int eq = t.IndexOf('=');
                if (eq > 0 && t.Length >= eq + 3 && t[eq + 1] == '"' && t[^1] == '"')
                    t = t[..(eq + 1)] + t[(eq + 2)..^1];
            }
            cleaned.Add(t);
        }
        return cleaned;
    }

    /// <summary>Extracts the last successful strategy from Zapret2 blockcheck (blog.sh) output.</summary>
    public static string GetLastStrategyFromText(string text)
    {
        var lines = text.Replace("\r", "").Split('\n');
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            if (lines[i].Contains("iana.org")) continue;
            if (lines[i].Contains("!!!!! AVAILABLE !!!!!"))
            {
                if (i > 0)
                {
                    string prev = lines[i - 1];
                    const string target = "--wf-tcp-out=443";
                    int pos = prev.IndexOf(target, StringComparison.Ordinal);
                    if (pos >= 0)
                    {
                        string raw = prev[(pos + target.Length)..].Trim();
                        return FormatStrategyQuotes(raw);
                    }
                }
            }
        }
        return "";
    }

    /// <summary>Extracts the --dpi... strategy line from a Zapret1 blockcheck SUMMARY section.</summary>
    public static string ExtractSummaryZapret1(IReadOnlyList<string> logLines)
    {
        for (int i = logLines.Count - 1; i >= 0; i--)
        {
            if (logLines[i].Contains("SUMMARY") && i + 1 < logLines.Count)
            {
                string fullLine = logLines[i + 1];
                int pos = fullLine.IndexOf("--dpi", StringComparison.Ordinal);
                if (pos >= 0)
                    return FormatStrategyQuotes(fullLine[pos..].Trim());
            }
        }
        return "";
    }
}
