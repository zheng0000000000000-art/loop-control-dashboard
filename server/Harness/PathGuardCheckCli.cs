// 경로 root 검사가 separator-bounded인지 확인하는 하네스 CLI.
// sibling-prefix escape 회귀(FAIL-2026-006/007)를 내장 케이스와 직접 입력으로 검증한다.
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class PathGuardCheckCli
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // path-guard-check 진입점. exit 0=모든 케이스 기대값 일치, 1=경계 실패, 2=사용법 오류.
    internal static int Run(string[] args)
    {
        try
        {
            var root = GitTools.FindRepoRoot();
            var cases = args.Length >= 3
                ? BuildInputCases(args[1], args[2..])
                : BuildRegressionCases();

            var results = new JsonArray();
            var failures = 0;
            foreach (var testCase in cases)
            {
                var actual = IsWithinRoot(testCase.Candidate, testCase.Root);
                var ok = actual == testCase.Expected;
                if (!ok) failures++;
                results.Add(new JsonObject
                {
                    ["name"] = testCase.Name,
                    ["root"] = testCase.Root,
                    ["candidate"] = testCase.Candidate,
                    ["expectedWithinRoot"] = testCase.Expected,
                    ["actualWithinRoot"] = actual,
                    ["verdict"] = ok ? "match" : "MISMATCH",
                });
            }

            var report = new JsonObject
            {
                ["harness"] = "path-guard-check",
                ["mode"] = args.Length >= 3 ? "input" : "regression",
                ["caseCount"] = cases.Count,
                ["failureCount"] = failures,
                ["verdict"] = failures == 0 ? "PASS" : "FAIL",
                ["cases"] = results,
                ["note"] = "Root containment must be full path equality or root plus directory separator, not raw prefix.",
            };
            Console.WriteLine(report.ToJsonString(JsonOptions));
            return failures == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{{\"error\":\"path-guard-check failed: {ex.Message}\"}}");
            return 2;
        }
    }

    // 직접 입력된 후보 경로들을 모두 root 내부여야 하는 케이스로 만든다.
    private static List<PathGuardCase> BuildInputCases(string root, string[] candidates)
    {
        if (string.IsNullOrWhiteSpace(root) || candidates.Length == 0)
            throw new ArgumentException("usage: path-guard-check [root candidatePath...]");

        var fullRoot = Path.GetFullPath(root);
        return candidates
            .Select((candidate, index) => new PathGuardCase(
                $"input-{index + 1}",
                fullRoot,
                Path.GetFullPath(candidate),
                true))
            .ToList();
    }

    // FAIL-2026-006/007 sibling-prefix 회귀 케이스를 만든다.
    private static List<PathGuardCase> BuildRegressionCases()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "lfwd-path-guard");
        var root = Path.Combine(baseDir, "data");
        var outboxRoot = Path.Combine(baseDir, "outbox");
        return new List<PathGuardCase>
        {
            new("root-equals-root", root, root, true),
            new("child-path", root, Path.Combine(root, "dev-pack", "workflow-state.json"), true),
            new("trailing-root-separator", root + Path.DirectorySeparatorChar, Path.Combine(root, "dev-pack"), true),
            new("storage-sibling-prefix", root, Path.Combine(baseDir, "data-escape", "projects.json"), false),
            new("outbox-sibling-prefix", outboxRoot, Path.Combine(baseDir, "outbox-escape", "poc", "meta.json"), false),
            new("parent-traversal-to-sibling", root, Path.Combine(root, "..", "data-escape", "projects.json"), false),
        };
    }

    // separator-bounded root 포함 여부를 판정한다.
    private static bool IsWithinRoot(string candidate, string root)
    {
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullCandidate = Path.GetFullPath(candidate)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return string.Equals(fullCandidate, fullRoot, StringComparison.OrdinalIgnoreCase)
            || fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || fullCandidate.StartsWith(fullRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record PathGuardCase(string Name, string Root, string Candidate, bool Expected);
}
