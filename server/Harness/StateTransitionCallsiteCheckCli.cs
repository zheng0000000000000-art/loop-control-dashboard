// state-transition 활성 경로에서 prepare/apply로 정렬되지 않은 옛 단일-샷 호출을 탐지한다.
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

internal static class StateTransitionCallsiteCheckCli
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // 활성 경로로 간주하는 파일 확장자 — 역사적 증거 파일은 allowlist로 면제.
    private static readonly string[] ActiveExtensions =
    [
        ".cs", ".ps1", ".sh", ".cmd", ".bat",
        ".json", ".yaml", ".yml",
        ".md",
    ];

    // 역사적 증거 경로 allowlist — 이 경로 아래 파일은 legacyCallsiteCount에 포함하지 않는다.
    // 경로 접두사가 아니라 명시 경로 목록 — 활성 경로(outputs/launch/ 등)는 포함하지 않는다.
    private static readonly string[] HistoricalPrefixes =
    [
        "docs/handoff/sessions/",
        "docs/handoff/WORKSTATE.applier-log.jsonl",
        "docs/handoff/RECOVERY.md",
        "docs/handoff/queue/",
        "docs/verification/",
        "docs/wiki/",
        "outputs/review/",
        "outputs/review-log.md",
        "outputs/reviewer-log.md",
        "HUMAN-INBOX.md",
    ];

    // 옛 단일-샷 호출 패턴 — state-transition 뒤에 --transition-id 또는 --expected-workstate-sha256이 오는 경우.
    private static readonly Regex LegacyCallPattern = new(
        @"state-transition\s+--(?:transition-id|expected-workstate-sha256)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // 활성 경로에서 옛 단일-샷 state-transition 호출을 탐지한다.
    internal static int Run(string[] args)
    {
        try
        {
            var root = GitTools.FindRepoRoot();
            var result = ScanCallsites(root);

            Console.WriteLine(result.ToJsonString(WriteOptions));
            return result["legacyCallsiteCount"]?.GetValue<int>() == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{{\"error\":\"state-transition-callsite-check 실패: {ex.Message}\"}}");
            return 2;
        }
    }

    // 저장소 전체를 스캔해 legacy callsite 수를 반환한다.
    private static JsonObject ScanCallsites(string root)
    {
        var legacyPaths = new List<string>();
        var historicalPaths = new List<string>();
        var scannedCount = 0;

        foreach (var filePath in EnumerateActiveFiles(root))
        {
            var relPath = ToRelative(filePath, root);
            if (IsHistorical(relPath))
            {
                if (ContainsLegacyCall(filePath))
                    historicalPaths.Add(relPath);
                continue;
            }

            scannedCount++;
            if (ContainsLegacyCall(filePath))
                legacyPaths.Add(relPath);
        }

        var classified = legacyPaths.Concat(historicalPaths.Select(p => "[historical] " + p)).ToArray();

        return new JsonObject
        {
            ["legacyCallsiteCount"] = legacyPaths.Count,
            ["historicalReferenceCount"] = historicalPaths.Count,
            ["scannedActiveFiles"] = scannedCount,
            ["classifiedPaths"] = new JsonArray(classified.Select(p => (JsonNode?)p).ToArray()),
        };
    }

    // 검사 대상 활성 파일을 열거한다.
    private static IEnumerable<string> EnumerateActiveFiles(string root)
    {
        var scanDirs = new[]
        {
            Path.Combine(root, "server"),
            Path.Combine(root, "scripts"),
            Path.Combine(root, "outputs"),
            Path.Combine(root, "docs"),
            Path.Combine(root, ".claude"),
            Path.Combine(root, ".github"),
        };

        foreach (var dir in scanDirs.Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ActiveExtensions.Contains(ext))
                    yield return file;
            }
        }

        // 루트의 스크립트·manifest 파일도 포함.
        foreach (var ext in new[] { "*.ps1", "*.sh", "*.cmd", "*.bat", "*.json", "*.yaml", "*.yml" })
        {
            foreach (var file in Directory.GetFiles(root, ext))
                yield return file;
        }
    }

    // 경로가 역사적 증거 allowlist에 속하는지 확인한다.
    private static bool IsHistorical(string relPath)
    {
        var normalized = relPath.Replace('\\', '/');
        return HistoricalPrefixes.Any(prefix =>
            normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, prefix.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
    }

    // 파일 내용에 legacy single-shot 패턴이 있는지 확인한다.
    private static bool ContainsLegacyCall(string filePath)
    {
        try
        {
            var content = File.ReadAllText(filePath);
            return LegacyCallPattern.IsMatch(content);
        }
        catch { return false; }
    }

    // 절대 경로를 저장소 루트 기준 상대 경로로 변환한다.
    private static string ToRelative(string absPath, string root)
    {
        if (absPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return absPath[(root.Length + 1)..].Replace('\\', '/');
        return absPath.Replace('\\', '/');
    }
}
