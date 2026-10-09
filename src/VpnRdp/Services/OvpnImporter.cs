using System.Text.RegularExpressions;

namespace VpnRdp.Services;

public sealed record OvpnInfo(string ImportedPath, bool NeedsUserPass, string? Remote, IReadOnlyList<string> MissingFiles);

/// <summary>
/// Copies an .ovpn (plus any cert/key files it references by relative path) into
/// %LOCALAPPDATA%\VpnRdp\configs\&lt;profile-id&gt;\ so the profile keeps working even if the
/// original download is deleted.
/// </summary>
public static partial class OvpnImporter
{
    // Directives whose first argument is a file path (unless given inline as <ca>...</ca>).
    private static readonly HashSet<string> FileDirectives = new(StringComparer.OrdinalIgnoreCase)
    {
        "ca", "cert", "key", "pkcs12", "tls-auth", "tls-crypt", "tls-crypt-v2", "secret",
        "crl-verify", "extra-certs", "dh", "auth-user-pass", "http-proxy-user-pass",
    };

    public static OvpnInfo Import(string sourcePath, string profileId)
    {
        var srcDir = Path.GetDirectoryName(Path.GetFullPath(sourcePath))!;
        var destDir = Path.Combine(AppPaths.OvpnDir, profileId);
        Directory.CreateDirectory(destDir);
        var destPath = Path.Combine(destDir, SafeFileName(Path.GetFileName(sourcePath)));

        var info = Inspect(sourcePath);
        var missing = new List<string>();
        foreach (var rel in info.ReferencedFiles)
        {
            if (Path.IsPathRooted(rel) || rel.Contains(".."))
                continue; // absolute paths keep working as-is; refuse to walk out of the folder
            var src = Path.Combine(srcDir, rel);
            if (File.Exists(src))
            {
                var dst = Path.Combine(destDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.Copy(src, dst, overwrite: true);
            }
            else
            {
                missing.Add(rel);
            }
        }

        if (!string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destPath), StringComparison.OrdinalIgnoreCase))
            File.Copy(sourcePath, destPath, overwrite: true);

        Log.Info($"Imported {sourcePath} -> {destPath} (user/pass: {info.NeedsUserPass}, remote: {info.Remote})");
        return new OvpnInfo(destPath, info.NeedsUserPass, info.Remote, missing);
    }

    internal sealed record Inspection(bool NeedsUserPass, string? Remote, List<string> ReferencedFiles);

    internal static Inspection Inspect(string path) => InspectText(File.ReadAllLines(path));

    internal static Inspection InspectText(IEnumerable<string> lines)
    {
        var needsUserPass = false;
        string? remote = null;
        var files = new List<string>();
        var inInlineBlock = false;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is '#' or ';')
                continue;
            if (InlineOpen().IsMatch(line)) { inInlineBlock = true; continue; }
            if (InlineClose().IsMatch(line)) { inInlineBlock = false; continue; }
            if (inInlineBlock)
                continue;

            var parts = Tokenize(line);
            if (parts.Count == 0)
                continue;
            var directive = parts[0].TrimStart('-');

            if (directive.Equals("auth-user-pass", StringComparison.OrdinalIgnoreCase))
                needsUserPass = parts.Count == 1; // with a file argument openvpn reads creds itself
            if (directive.Equals("remote", StringComparison.OrdinalIgnoreCase) && remote is null && parts.Count > 1)
                remote = parts.Count > 2 ? $"{parts[1]}:{parts[2]}" : parts[1];

            if (FileDirectives.Contains(directive) && parts.Count > 1 &&
                !parts[1].Equals("[inline]", StringComparison.OrdinalIgnoreCase))
                files.Add(parts[1].Replace('/', '\\'));
        }
        return new Inspection(needsUserPass, remote, files);
    }

    /// <summary>Splits an openvpn config line, honoring "double quotes" and 'single quotes'.</summary>
    internal static List<string> Tokenize(string line)
    {
        var result = new List<string>();
        var cur = new System.Text.StringBuilder();
        char? quote = null;
        foreach (var ch in line)
        {
            if (quote is not null)
            {
                if (ch == quote) quote = null;
                else cur.Append(ch);
            }
            else if (ch is '"' or '\'')
                quote = ch;
            else if (char.IsWhiteSpace(ch))
            {
                if (cur.Length > 0) { result.Add(cur.ToString()); cur.Clear(); }
            }
            else
                cur.Append(ch);
        }
        if (cur.Length > 0)
            result.Add(cur.ToString());
        return result;
    }

    private static string SafeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        // The Interactive Service rejects forward slashes; keep the name simple.
        return name;
    }

    [GeneratedRegex(@"^<[a-z0-9-]+>$", RegexOptions.IgnoreCase)]
    private static partial Regex InlineOpen();

    [GeneratedRegex(@"^</[a-z0-9-]+>$", RegexOptions.IgnoreCase)]
    private static partial Regex InlineClose();
}
