// Directive scope harness.
// Compares the directive allowlist against git status and reports out-of-scope files without modifying them.
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

internal static class ScopeCheckCli
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // scope-check entry. exit 0=all changed files allowed, 1=out-of-scope files exist, 2=usage or malformed directive.
    internal static int Run(string[] args)
    {
        try
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("{\"error\":\"usage: scope-check <directivePath|diId>\"}");
                return 2;
            }

            var root = GitTools.FindRepoRoot();
            var directive = ResolveDirective(root, args[1]);
            if (directive is null)
            {
                Console.Error.WriteLine($"{{\"error\":\"directive not found: {args[1]}\"}}");
                return 2;
            }

            var allowlist = ParseAllowlist(directive);
            if (allowlist.Count == 0)
            {
                Console.Error.WriteLine($"{{\"error\":\"allowlist not found or empty: {Path.GetRelativePath(root, directive).Replace('\\', '/')}\"}}");
                return 2;
            }

            var changed = GetChangedFiles(root);
            var outOfScope = new JsonArray();
            foreach (var file in changed)
            {
                if (!allowlist.Any(pattern => GlobMatches(pattern, file)))
                    outOfScope.Add(file);
            }

            var report = new JsonObject
            {
                ["harness"] = "scope-check",
                ["directive"] = Path.GetRelativePath(root, directive).Replace('\\', '/'),
                ["allowlistCount"] = allowlist.Count,
                ["changedFileCount"] = changed.Count,
                ["outOfScopeCount"] = outOfScope.Count,
                ["verdict"] = outOfScope.Count == 0 ? "PASS" : "FAIL",
                ["allowlist"] = new JsonArray(allowlist.Select(p => (JsonNode)p).ToArray()),
                ["outOfScopeFiles"] = outOfScope,
                ["note"] = "Read-only check. Out-of-scope files must be handled by the orchestrator or implementer.",
            };

            Console.WriteLine(report.ToJsonString(JsonOptions));
            return outOfScope.Count == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{{\"error\":\"scope-check failed: {ex.Message}\"}}");
            return 2;
        }
    }

    // Resolves either a path or a directive id such as ORCH01 / HOOK01.
    private static string? ResolveDirective(string root, string input)
    {
        var direct = Path.GetFullPath(Path.IsPathRooted(input) ? input : Path.Combine(root, input));
        if (File.Exists(direct)) return direct;

        var key = Normalize(input);
        var dirs = new[]
        {
            Path.Combine(root, "docs", "handoff", "queue"),
            Path.Combine(root, "docs", "directives"),
        };

        foreach (var dir in dirs.Where(Directory.Exists))
        {
            var found = Directory.EnumerateFiles(dir, "*.md")
                .FirstOrDefault(path => Normalize(Path.GetFileNameWithoutExtension(path)).Contains(key));
            if (found is not null) return found;
        }

        return null;
    }

    // Extracts bullet glob patterns under a markdown heading containing "allowlist".
    private static List<string> ParseAllowlist(string directive)
    {
        var patterns = new List<string>();
        var inSection = false;

        foreach (var raw in File.ReadLines(directive))
        {
            var line = raw.Trim();
            if (line.StartsWith("## "))
            {
                if (inSection) break;
                inSection = line.Contains("allowlist", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inSection || !line.StartsWith("- ")) continue;
            var pattern = line[2..].Trim().Trim('`').Replace('\\', '/');
            if (pattern.Length > 0) patterns.Add(pattern);
        }

        return patterns.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    // Reads changed files from git status. Rename entries use the destination path.
    private static List<string> GetChangedFiles(string root)
    {
        var status = GitTools.RunGitText(root, "status --porcelain") ?? "";
        var files = new List<string>();
        foreach (var raw in status.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (raw.Length < 4) continue;
            var file = raw[3..].Trim().Trim('"').Replace('\\', '/');
            var renameIndex = file.LastIndexOf(" -> ", StringComparison.Ordinal);
            if (renameIndex >= 0) file = file[(renameIndex + 4)..].Trim().Trim('"');
            if (file.Length > 0) files.Add(file);
        }

        return files.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // Minimal glob matcher for directive allowlists: *, **, and literal paths.
    private static bool GlobMatches(string pattern, string file)
    {
        var p = pattern.Trim().Replace('\\', '/');
        var f = file.Trim().Replace('\\', '/');
        if (p == "**" || p == "**/*") return true;
        if (p.EndsWith("/**", StringComparison.Ordinal))
        {
            var prefix = p[..^3];
            return f.Equals(prefix.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
                || f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        var regex = "^" + Regex.Escape(p)
            .Replace("\\*\\*", ".*")
            .Replace("\\*", "[^/]*") + "$";
        return Regex.IsMatch(f, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string Normalize(string value)
        => Regex.Replace(value, "[^A-Za-z0-9]", "").ToLowerInvariant();
}
