using System;
using System.IO;

namespace AppLocate.Core.Models;

/// <summary>Common helpers for normalizing and validating filesystem paths.</summary>
public static class PathUtils
{
    /// <summary>
    /// Expands environment variables, trims surrounding quotes, converts forward slashes to backslashes,
    /// and removes trailing directory separators (except for root drives). Returns null for empty/whitespace input.
    /// </summary>
    public static string? NormalizePath(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        try
        {
            var s = Environment.ExpandEnvironmentVariables(raw.Trim().Trim('\"'));
                if (s.Length == 0)
                {
                    return null;
                }
            s = s.Replace('/', '\\');
            // Collapse duplicate backslashes (but keep leading \\ for UNC) 
            if (s.StartsWith("\\\\", StringComparison.Ordinal))
            {
                var unc = true; // preserve initial UNC designator
                var span = s.AsSpan();
                System.Text.StringBuilder sb = new();
                int i = 0; int len = span.Length;
                int backslashRun = 0;
                while (i < len)
                {
                    char c = span[i++];
                        if (c == '\\')
                    {
                        backslashRun++;
                            if (unc && backslashRun <= 2)
                            {
                                sb.Append('\\');
                            }
                            else if (!unc && backslashRun == 1)
                            {
                                sb.Append('\\');
                            }
                    }
                    else
                    {
                        unc = false;
                        backslashRun = 0;
                        sb.Append(c);
                    }
                }
                s = sb.ToString();
            }
            else
            {
                while (s.Contains("\\\\", StringComparison.Ordinal))
                {
                    s = s.Replace("\\\\", "\\");
                }
            }
            // Remove trailing slash unless root (e.g., C:\)
            if (s.Length > 3 && (s.EndsWith('\\') || s.EndsWith('/')))
            {
                s = s.TrimEnd('\\','/');
            }
            return s;
        }
        catch { return raw; }
    }
}
