using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

public static class Utilities
{
    public static string SanitizeFilename(string input)
    {
        var s = Regex.Replace(input, @"[^\w\d\-\.]", "_");
        return s;
    }

    public static string SanitizeKey(string input)
    {
        // Azure Table RowKey limits: max 1KB. Keep it simple:
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(input)).Replace("/", "_").Replace("+", "-");
    }

    public static IEnumerable<string> ChunkText(string text, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        int pos = 0;
        while (pos < text.Length)
        {
            var len = Math.Min(maxChars, text.Length - pos);
            yield return text.Substring(pos, len);
            pos += len;
        }
    }

    public static string IdForPath(string repoUrl, string path, int chunkIndex)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(repoUrl + "|" + path + "|" + chunkIndex));
        return Convert.ToBase64String(bytes).Replace("/", "_").Replace("+", "-").Substring(0, 32);
    }
}
