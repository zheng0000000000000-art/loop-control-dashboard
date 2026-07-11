// 지시서 Context Pack의 참조 파일 존재와 해시 일치를 검사하는 하네스 CLI.
// 해시 갱신이나 스탬핑 없이 선언된 입력만 읽어 검증한다.
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

internal static class ContextPackIntegrityCli
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // context-pack-integrity 진입점이다. exit 0=정상 또는 skipped만 있음, 1=missing/stale, 2=사용법 또는 하네스 오류.
    internal static int Run(string[] args)
    {
        try
        {
            var root = GitTools.FindRepoRoot();
            var directives = ResolveTargets(root, args.Skip(1).ToArray());
            if (directives.Count == 0)
            {
                Console.Error.WriteLine("{\"error\":\"no directive files found\"}");
                return 2;
            }

            var entries = new JsonArray();
            var missing = 0;
            var stale = 0;
            var ok = 0;
            var skipped = 0;
            var warnings = 0;

            foreach (var directive in directives)
            {
                var result = CheckDirective(root, directive);
                entries.Add(result.Report);
                missing += result.Missing;
                stale += result.Stale;
                ok += result.Ok;
                skipped += result.Skipped;
                warnings += result.Warnings;
            }

            var failureCount = missing + stale;
            var report = new JsonObject
            {
                ["harness"] = "context-pack-integrity",
                ["checkedDirectiveCount"] = directives.Count,
                ["okCount"] = ok,
                ["missingCount"] = missing,
                ["staleCount"] = stale,
                ["skippedCount"] = skipped,
                ["warningCount"] = warnings,
                ["failureCount"] = failureCount,
                ["verdict"] = failureCount == 0 ? "PASS" : "FAIL",
                ["directives"] = entries,
                ["note"] = "Read-only check. Context Pack hashes are supplied by directive authors, not by this harness.",
            };

            Console.WriteLine(report.ToJsonString(JsonOptions));
            return failureCount == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{{\"error\":\"context-pack-integrity failed: {ex.Message}\"}}");
            return 2;
        }
    }

    // 인자가 없으면 큐 지시서 전체를, 인자가 있으면 파일이나 디렉터리 대상을 해석한다.
    private static List<string> ResolveTargets(string root, string[] inputs)
    {
        if (inputs.Length == 0)
        {
            var queue = Path.Combine(root, "docs", "handoff", "queue");
            return Directory.Exists(queue)
                ? Directory.EnumerateFiles(queue, "*.md").OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList()
                : [];
        }

        var targets = new List<string>();
        foreach (var input in inputs.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var full = Path.GetFullPath(Path.IsPathRooted(input) ? input : Path.Combine(root, input));
            if (File.Exists(full))
            {
                targets.Add(full);
                continue;
            }

            if (Directory.Exists(full))
            {
                targets.AddRange(Directory.EnumerateFiles(full, "*.md")
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
            }
        }

        return targets.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    // 지시서 하나의 Context Pack을 읽고 requiredInputs를 검증한다.
    private static DirectiveCheckResult CheckDirective(string root, string directive)
    {
        var relativeDirective = DisplayPath(root, directive);
        var text = File.ReadAllText(directive);
        var packText = ExtractContextPack(text);
        if (packText is null)
        {
            return new DirectiveCheckResult(new JsonObject
            {
                ["directive"] = relativeDirective,
                ["verdict"] = "skipped",
                ["reason"] = "Context Pack block not found",
            }, 0, 0, 0, 1, 0);
        }

        JsonObject pack;
        try
        {
            pack = JsonNode.Parse(packText) as JsonObject
                ?? throw new JsonException("Context Pack JSON is not an object");
        }
        catch (JsonException ex)
        {
            return FailedDirective(relativeDirective, "malformed", ex.Message);
        }

        var requiredInputs = pack["requiredInputs"] as JsonArray;
        if (requiredInputs is null)
        {
            return FailedDirective(relativeDirective, "missing", "requiredInputs is absent or not an array");
        }

        var allowlist = ParseAllowlist(text);
        var inputs = new JsonArray();
        var warnings = new JsonArray();
        var missing = 0;
        var stale = 0;
        var ok = 0;

        foreach (var item in requiredInputs.OfType<JsonObject>())
        {
            var entry = CheckRequiredInput(root, item, allowlist, warnings);
            inputs.Add(entry.Report);
            missing += entry.Missing;
            stale += entry.Stale;
            ok += entry.Ok;
        }

        var failureCount = missing + stale;
        var report = new JsonObject
        {
            ["directive"] = relativeDirective,
            ["diId"] = ReadString(pack, "diId"),
            ["verdict"] = failureCount == 0 ? "ok" : "failed",
            ["requiredInputCount"] = requiredInputs.Count,
            ["okCount"] = ok,
            ["missingCount"] = missing,
            ["staleCount"] = stale,
            ["warningCount"] = warnings.Count,
            ["requiredInputs"] = inputs,
            ["warnings"] = warnings,
        };

        return new DirectiveCheckResult(report, missing, stale, ok, 0, warnings.Count);
    }

    // 실패한 지시서 판정을 공통 결과로 만든다.
    private static DirectiveCheckResult FailedDirective(string directive, string code, string message)
    {
        var report = new JsonObject
        {
            ["directive"] = directive,
            ["verdict"] = code,
            ["missingCount"] = code == "missing" ? 1 : 0,
            ["staleCount"] = 0,
            ["requiredInputs"] = new JsonArray(),
            ["warnings"] = new JsonArray(),
            ["error"] = message,
        };
        return new DirectiveCheckResult(report, code == "missing" ? 1 : 0, 0, 0, 0, 0);
    }

    // requiredInputs 항목 하나의 파일 존재와 sha256 값을 검사한다.
    private static RequiredInputCheckResult CheckRequiredInput(
        string root,
        JsonObject item,
        List<string> allowlist,
        JsonArray warnings)
    {
        var path = ReadString(item, "path").Replace('\\', '/');
        var expected = NormalizeHash(ReadString(item, "sha256"));
        var full = ResolveInputPath(root, path);
        var report = new JsonObject
        {
            ["path"] = path,
            ["verdict"] = "ok",
            ["expectedSha256"] = expected,
        };

        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(expected))
        {
            report["verdict"] = "missing";
            report["reason"] = "path or sha256 is absent";
            return new RequiredInputCheckResult(report, 1, 0, 0);
        }

        if (AllowlistOverlaps(path, allowlist))
        {
            warnings.Add(new JsonObject
            {
                ["path"] = path,
                ["code"] = "required-input-overlaps-allowlist",
                ["message"] = "requiredInputs should not overlap the directive allowlist",
            });
        }

        if (!File.Exists(full))
        {
            report["verdict"] = "missing";
            report["reason"] = "required input file does not exist";
            return new RequiredInputCheckResult(report, 1, 0, 0);
        }

        var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(full))).ToLowerInvariant();
        report["actualSha256"] = actual;
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            report["verdict"] = "stale";
            report["reason"] = "sha256 does not match current file content";
            return new RequiredInputCheckResult(report, 0, 1, 0);
        }

        return new RequiredInputCheckResult(report, 0, 0, 1);
    }

    // 지시서에서 context-pack 코드 펜스 내용을 추출한다.
    private static string? ExtractContextPack(string text)
    {
        var match = Regex.Match(
            text,
            @"(^|\n)```context-pack\s*\r?\n(?<json>.*?)\r?\n```",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["json"].Value : null;
    }

    // 지시서의 allowlist 섹션에서 쓰기 허용 경로 패턴을 읽는다.
    private static List<string> ParseAllowlist(string text)
    {
        var patterns = new List<string>();
        var inSection = false;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                if (inSection) break;
                inSection = line.Contains("allowlist", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inSection || !line.StartsWith("- ", StringComparison.Ordinal)) continue;
            var pattern = line[2..].Trim().Trim('`').Replace('\\', '/');
            if (pattern.Length > 0) patterns.Add(pattern);
        }

        return patterns.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    // requiredInputs 경로와 allowlist 패턴이 겹치는지 확인한다.
    private static bool AllowlistOverlaps(string path, List<string> allowlist)
    {
        var normalized = path.Replace('\\', '/');
        return allowlist.Any(pattern => GlobMatches(pattern, normalized));
    }

    // *, **, 리터럴 경로를 지원하는 최소 glob 대조를 수행한다.
    private static bool GlobMatches(string pattern, string file)
    {
        var p = pattern.Trim().Replace('\\', '/');
        var f = file.Trim().Replace('\\', '/');
        if (p == "**" || p == "**/*") return true;
        if (p.EndsWith("/**", StringComparison.Ordinal))
        {
            var prefix = p[..^3].TrimEnd('/');
            return f.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                || f.StartsWith($"{prefix}/", StringComparison.OrdinalIgnoreCase);
        }

        var regex = "^" + Regex.Escape(p)
            .Replace("\\*\\*", ".*")
            .Replace("\\*", "[^/]*") + "$";
        return Regex.IsMatch(f, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    // requiredInputs 경로를 저장소 루트 또는 절대 경로 기준으로 해석한다.
    private static string ResolveInputPath(string root, string path)
    {
        return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path));
    }

    // JSON 문자열 필드를 안전하게 읽는다.
    private static string ReadString(JsonObject obj, string property)
    {
        return obj[property]?.ToString() ?? "";
    }

    // sha256 표기를 소문자 순수 해시 값으로 정규화한다.
    private static string NormalizeHash(string value)
    {
        return value.Trim().ToLowerInvariant().Replace("sha256:", "", StringComparison.OrdinalIgnoreCase);
    }

    // 출력용 경로를 저장소 상대 경로 또는 원래 절대 경로로 만든다.
    private static string DisplayPath(string root, string path)
    {
        var full = Path.GetFullPath(path);
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return full.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            ? Path.GetRelativePath(root, full).Replace('\\', '/')
            : full.Replace('\\', '/');
    }
}

internal sealed record DirectiveCheckResult(
    JsonObject Report,
    int Missing,
    int Stale,
    int Ok,
    int Skipped,
    int Warnings);

internal sealed record RequiredInputCheckResult(JsonObject Report, int Missing, int Stale, int Ok);
