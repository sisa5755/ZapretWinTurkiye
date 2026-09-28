using System.Collections.Generic;
using System.Text;

namespace ZapretGuiWpf.Helpers;

/// <summary>
/// Builds a single Windows command-line string from an argv list, following
/// the same quoting rules CreateProcess/argv parsing expects - equivalent to
/// Python's subprocess.list2cmdline(), used when handing "sc create binPath="
/// a single string built from a real arg list (so no manual quote-escaping
/// is needed, and arguments containing spaces/quotes can't break out).
/// </summary>
public static class CommandLine
{
    public static string ArgvToCommandLine(IEnumerable<string> args)
    {
        var sb = new StringBuilder();
        bool first = true;
        foreach (var arg in args)
        {
            if (!first) sb.Append(' ');
            first = false;
            AppendQuoted(sb, arg);
        }
        return sb.ToString();
    }

    private static void AppendQuoted(StringBuilder sb, string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0)
        {
            sb.Append(arg);
            return;
        }

        sb.Append('"');
        for (int i = 0; i < arg.Length; i++)
        {
            int backslashes = 0;
            while (i < arg.Length && arg[i] == '\\')
            {
                backslashes++;
                i++;
            }

            if (i == arg.Length)
            {
                sb.Append('\\', backslashes * 2);
                break;
            }
            if (arg[i] == '"')
            {
                sb.Append('\\', backslashes * 2 + 1);
                sb.Append('"');
            }
            else
            {
                sb.Append('\\', backslashes);
                sb.Append(arg[i]);
            }
        }
        sb.Append('"');
    }
}
