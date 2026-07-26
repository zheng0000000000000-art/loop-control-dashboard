// 지시서 allowlist와 파일 claim을 git 변경분에 대조하는 읽기 전용 하네스.
// 범위 밖 변경과 활성 claim 충돌을 보고만 하고 파일·프로세스는 수정하지 않는다.
using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

internal static class ScopeCheckCli
{
    private static readonly string[] ByproductPatterns =
    [
        "outputs/**",
        "dashboard/data/*/measurement.json",
        "dashboard/data/*/run-log.json",
        "dashboard/data/*/workflow-state.json",
        "dashboard/data/*/patch-proposal.json",
        "dashboard/data/*/review-report.json",
        "**/*.pid",
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // CLI 인자를 해석해 allowlist 대조와 claim 충돌 검사를 실행한다.
    internal static int Run(string[] args)
    {
        try
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("{\"error\":\"usage: scope-check <directivePath|diId> [--actor <actor>] [--claims <claimsPath>]\"}");
                return 2;
            }

            var root = GitTools.FindRepoRoot();
            var options = ParseOptions(args);
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

            var allChanged = GetChangedFiles(root);
            var ignored = allChanged.Where(IsByproduct).ToList();
            var changed = allChanged.Where(file => !IsByproduct(file)).ToList();
            var outOfScope = new JsonArray();
            foreach (var file in changed)
            {
                if (!allowlist.Any(pattern => GlobMatches(pattern, file)))
                    outOfScope.Add(file);
            }

            var claimReport = ReadClaims(root, options.ClaimsPath, options.Actor, changed);
            var conflictCount = claimReport.Conflicts.Count;
            var report = new JsonObject
            {
                ["harness"] = "scope-check",
                ["directive"] = Path.GetRelativePath(root, directive).Replace('\\', '/'),
                ["actor"] = options.Actor,
                ["claimsFile"] = claimReport.RelativePath,
                ["allowlistCount"] = allowlist.Count,
                ["changedFileCount"] = changed.Count,
                ["ignoredByproductCount"] = ignored.Count,
                ["outOfScopeCount"] = outOfScope.Count,
                ["claimConflictCount"] = conflictCount,
                ["staleClaimCount"] = claimReport.StaleClaims.Count,
                ["unknownAllowlistClaimCount"] = claimReport.UnknownAllowlistClaims.Count,
                ["verdict"] = outOfScope.Count == 0 && conflictCount == 0 ? "PASS" : "FAIL",
                ["allowlist"] = new JsonArray(allowlist.Select(p => (JsonNode)p).ToArray()),
                ["outOfScopeFiles"] = outOfScope,
                ["ignoredByproducts"] = new JsonArray(ignored.Select(file => (JsonNode)file).ToArray()),
                ["claimConflicts"] = claimReport.Conflicts,
                ["staleClaims"] = claimReport.StaleClaims,
                ["unknownAllowlistClaims"] = claimReport.UnknownAllowlistClaims,
                ["note"] = "Read-only check. Out-of-scope files and active claim conflicts must be handled by the orchestrator or implementer.",
            };

            Console.WriteLine(report.ToJsonString(JsonOptions));
            return outOfScope.Count == 0 && conflictCount == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{{\"error\":\"scope-check failed: {ex.Message}\"}}");
            return 2;
        }
    }

    // 선택 인자의 기본값과 파일 경로를 정한다.
    private static ScopeOptions ParseOptions(string[] args)
    {
        var actor = "codex";
        string? claimsPath = null;
        for (var i = 2; i < args.Length; i++)
        {
            if (args[i] == "--actor" && i + 1 < args.Length)
            {
                actor = args[++i];
                continue;
            }

            if (args[i] == "--claims" && i + 1 < args.Length)
            {
                claimsPath = args[++i];
                continue;
            }

            throw new ArgumentException($"unknown option: {args[i]}");
        }

        return new ScopeOptions(actor, claimsPath);
    }

    // 경로 또는 ORCH01/HOOK01 같은 지시서 ID를 실제 파일로 해석한다.
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

    // allowlist 제목 아래의 글머리표 glob 패턴을 추출한다.
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

    // git status에서 변경 파일을 읽고 rename은 목적지 경로로 판정한다.
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

    // 실행기·측정기가 만드는 런타임 부산물은 작업 범위 변경에서 제외한다.
    private static bool IsByproduct(string file)
        => ByproductPatterns.Any(pattern => GlobMatches(pattern, file));

    // claim 원장을 읽어 활성 충돌, 죽은 PID, allowlist 미상 claim을 분리한다.
    private static ClaimReport ReadClaims(string root, string? claimsPathOption, string selfActor, List<string> changedFiles)
    {
        var claimsPath = Path.GetFullPath(Path.IsPathRooted(claimsPathOption ?? "")
            ? claimsPathOption!
            : Path.Combine(root, claimsPathOption ?? Path.Combine("docs", "handoff", "FILE-CLAIMS.json")));
        var relativePath = Path.GetRelativePath(root, claimsPath).Replace('\\', '/');
        var conflicts = new JsonArray();
        var staleClaims = new JsonArray();
        var unknownAllowlistClaims = new JsonArray();

        if (!File.Exists(claimsPath))
            return new ClaimReport(relativePath, conflicts, staleClaims, unknownAllowlistClaims);

        var doc = JsonNode.Parse(File.ReadAllText(claimsPath))?.AsObject()
            ?? throw new InvalidOperationException($"claims file is not a JSON object: {relativePath}");
        var claims = doc["claims"]?.AsArray();
        if (claims is null) return new ClaimReport(relativePath, conflicts, staleClaims, unknownAllowlistClaims);

        foreach (var item in claims.OfType<JsonObject>())
        {
            var status = item["status"]?.GetValue<string>() ?? "";
            var actor = item["actor"]?.GetValue<string>() ?? "";
            var taskId = item["taskId"]?.GetValue<string>() ?? "";
            var claimId = item["claimId"]?.GetValue<string>() ?? "";
            var pid = ReadInt(item["pid"]);
            var allowlistSource = item["allowlistSource"]?.GetValue<string>();
            var paths = ReadPaths(item["paths"]);

            if (string.IsNullOrWhiteSpace(allowlistSource))
                unknownAllowlistClaims.Add(BuildClaimSummary(item, "allowlist-unknown"));

            if (!status.Equals("active", StringComparison.OrdinalIgnoreCase)) continue;

            var liveness = EvaluateLiveness(item, pid);
            if (!liveness.Alive)
                staleClaims.Add(BuildClaimSummary(item, liveness.Reason));

            if (actor.Equals(selfActor, StringComparison.OrdinalIgnoreCase)) continue;
            if (!liveness.Alive) continue;
            foreach (var file in changedFiles)
            {
                var matchedPath = paths.FirstOrDefault(path => GlobMatches(path, file));
                if (matchedPath is null) continue;
                conflicts.Add(new JsonObject
                {
                    ["file"] = file,
                    ["claimedPath"] = matchedPath,
                    ["actor"] = actor,
                    ["taskId"] = taskId,
                    ["pid"] = pid,
                    ["claimId"] = claimId,
                });
            }
        }

        return new ClaimReport(relativePath, conflicts, staleClaims, unknownAllowlistClaims);
    }

    // claim의 paths 배열을 문자열 목록으로 정규화한다.
    private static List<string> ReadPaths(JsonNode? node)
    {
        if (node is null) return [];
        if (node is JsonArray array)
            return array.OfType<JsonValue>()
                .Select(value => value.GetValue<string>().Replace('\\', '/'))
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        return [];
    }

    // JSON 숫자 또는 문자열 PID를 정수로 읽는다.
    private static int? ReadInt(JsonNode? node)
    {
        if (node is null) return null;
        if (node is JsonValue value && value.TryGetValue<int>(out var n)) return n;
        if (node is JsonValue text && text.TryGetValue<string>(out var s) && int.TryParse(s, out var parsed)) return parsed;
        return null;
    }

    // claim 시각·만료 시각과 프로세스 시작 시각을 함께 대조해 PID 재사용을 배제한다.
    private static ClaimLiveness EvaluateLiveness(JsonObject item, int? pid)
    {
        if (item["expiresAt"] is not null
            && DateTimeOffset.TryParse(item["expiresAt"]!.ToString(), out var expiresAt)
            && expiresAt <= DateTimeOffset.Now)
            return new ClaimLiveness(false, "expired");
        if (pid is null || pid <= 0) return new ClaimLiveness(false, "pid-dead");
        try
        {
            using var process = Process.GetProcessById(pid.Value);
            if (process.HasExited) return new ClaimLiveness(false, "pid-dead");
            if (!DateTimeOffset.TryParse(item["claimedAt"]?.ToString(), out var claimedAt))
                return new ClaimLiveness(false, "claimed-at-missing");
            var startedAt = new DateTimeOffset(process.StartTime);
            return startedAt <= claimedAt
                ? new ClaimLiveness(true, "alive")
                : new ClaimLiveness(false, "pid-reused");
        }
        catch
        {
            return new ClaimLiveness(false, "pid-dead");
        }
    }

    // 경고 출력용 claim 요약 객체를 만든다.
    private static JsonObject BuildClaimSummary(JsonObject item, string reason)
    {
        return new JsonObject
        {
            ["reason"] = reason,
            ["claimId"] = item["claimId"]?.DeepClone(),
            ["actor"] = item["actor"]?.DeepClone(),
            ["taskId"] = item["taskId"]?.DeepClone(),
            ["pid"] = item["pid"]?.DeepClone(),
            ["status"] = item["status"]?.DeepClone(),
            ["allowlistSource"] = item["allowlistSource"]?.DeepClone(),
            ["expiresAt"] = item["expiresAt"]?.DeepClone(),
        };
    }

    // 지시서와 claim allowlist에서 쓰는 최소 glob 규칙을 판정한다.
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

    // 지시서 ID와 파일명 비교를 위해 기호를 제거한다.
    private static string Normalize(string value)
        => Regex.Replace(value, "[^A-Za-z0-9]", "").ToLowerInvariant();

    private sealed record ScopeOptions(string Actor, string? ClaimsPath);

    private sealed record ClaimReport(
        string RelativePath,
        JsonArray Conflicts,
        JsonArray StaleClaims,
        JsonArray UnknownAllowlistClaims);

    private sealed record ClaimLiveness(bool Alive, string Reason);
}
