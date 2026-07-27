// 실행자 산출물 디렉터리의 처분 기록과 반입 게이트 연결을 검증한다.
// 기록 부재와 기록·실체 불일치를 디렉터리별 위반으로 집계한다.
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class LaunchDispositionCli
{

    // launch-disposition 진입점. exit 0=위반 없음, 1=위반 있음, 2=입력 또는 실행 오류.
    internal static int Run(string[] args)
    {
        try
        {
            if (args.Length > 2)
                throw new ArgumentException("usage: launch-disposition [fixture-root]");

            var repoRoot = GitTools.FindRepoRoot();
            var input = args.Length == 2 ? args[1] : "outbox";
            var scanRoot = Path.GetFullPath(Path.IsPathRooted(input) ? input : Path.Combine(repoRoot, input));
            if (!Directory.Exists(scanRoot))
                throw new DirectoryNotFoundException($"launch root not found: {input}");

            var reports = new JsonArray();
            foreach (var directory in Directory.GetDirectories(scanRoot, "codex-launch-*")
                .OrderBy(path => path, StringComparer.Ordinal))
                reports.Add(InspectDirectory(repoRoot, directory));

            var violations = reports.OfType<JsonObject>()
                .Count(report => report["violation"]?.GetValue<bool>() == true);
            Console.WriteLine(new JsonObject
            {
                ["harness"] = "launch-disposition",
                ["root"] = Path.GetRelativePath(repoRoot, scanRoot).Replace('\\', '/'),
                ["launchCount"] = reports.Count,
                ["violations"] = violations,
                ["launches"] = reports,
            }.ToJsonString(HarnessJson.Options));
            return violations == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new
            {
                error = $"launch-disposition failed: {ex.Message}",
            }));
            return 2;
        }
    }

    // 한 실행 디렉터리의 처분 기록을 검사하고 위반 사유 하나를 반환한다.
    private static JsonObject InspectDirectory(string repoRoot, string directory)
    {
        var directoryName = Path.GetFileName(directory);
        var expectedLaunchId = directoryName["codex-launch-".Length..];
        var dispositionPath = Path.Combine(directory, "disposition.json");
        string? reason = null;

        if (!Path.Exists(dispositionPath))
            reason = "disposition-missing";
        else
        {
            try
            {
                var disposition = JsonNode.Parse(File.ReadAllText(dispositionPath))?.AsObject()
                    ?? throw new JsonException("root must be an object");
                reason = ValidateDisposition(repoRoot, directory, expectedLaunchId, disposition);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                reason = $"disposition-unparsable: {ex.Message}";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                reason = $"disposition-unreadable: {ex.Message}";
            }
        }

        return new JsonObject
        {
            ["launchId"] = expectedLaunchId,
            ["directory"] = directoryName,
            ["violation"] = reason is not null,
            ["reason"] = reason,
        };
    }

    // 공통 필드와 상태별 필수 증거를 검사한다.
    private static string? ValidateDisposition(
        string repoRoot,
        string directory,
        string expectedLaunchId,
        JsonObject disposition)
    {
        if (!string.Equals(ReadRequired(disposition, "launchId"), expectedLaunchId, StringComparison.Ordinal))
            return "launch-id-mismatch";
        if (string.IsNullOrWhiteSpace(ReadRequired(disposition, "decidedAt")))
            return "decided-at-missing";
        if (string.IsNullOrWhiteSpace(ReadRequired(disposition, "actor")))
            return "actor-missing";

        var state = ReadRequired(disposition, "state");
        return state switch
        {
            "pending" => "disposition-pending",
            "rejected" => string.IsNullOrWhiteSpace(ReadRequired(disposition, "reason"))
                ? "rejection-reason-missing"
                : null,
            "no-output" => HasNonEmptyPatch(directory)
                ? "no-output-has-patch"
                : null,
            "imported" => ValidateImported(repoRoot, expectedLaunchId, disposition),
            _ => "state-invalid",
        };
    }

    // 반입 기록의 보고서 존재, 실행 식별자, 커밋 선후 관계를 검사한다.
    private static string? ValidateImported(
        string repoRoot,
        string expectedLaunchId,
        JsonObject disposition)
    {
        var importCommit = ReadRequired(disposition, "importCommit");
        var gateReport = ReadRequired(disposition, "gateReport");
        if (!IsCommitId(importCommit))
            return "import-commit-invalid";
        if (string.IsNullOrWhiteSpace(gateReport))
            return "gate-report-missing";

        var reportPath = Path.GetFullPath(Path.IsPathRooted(gateReport)
            ? gateReport
            : Path.Combine(repoRoot, gateReport));
        if (!Path.Exists(reportPath))
            return "gate-report-not-found";

        JsonObject report;
        try
        {
            report = JsonNode.Parse(File.ReadAllText(reportPath))?.AsObject()
                ?? throw new JsonException("root must be an object");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return $"gate-report-unparsable: {ex.Message}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"gate-report-unreadable: {ex.Message}";
        }

        var reportLaunchId = ReadEquivalentLaunchId(report);
        if (!string.Equals(reportLaunchId, expectedLaunchId, StringComparison.Ordinal))
            return "gate-report-launch-id-mismatch";

        var baselineCommit = ReadRequired(report, "baselineCommit");
        if (!IsCommitId(baselineCommit))
            return "gate-report-baseline-invalid";
        var ancestry = GitTools.RunGitBytes(
            repoRoot,
            $"merge-base --is-ancestor {importCommit} {baselineCommit}");
        return ancestry is null ? "gate-report-predates-import" : null;
    }

    // 보고서가 쓰는 허용 식별자 이름 중 첫 값을 읽는다.
    private static string ReadEquivalentLaunchId(JsonObject report)
    {
        foreach (var name in new[] { "launchId", "executorLaunchId", "sourceLaunchId" })
        {
            var value = report[name]?.ToString();
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }
        return "";
    }

    // candidate.patch가 실제 바이트를 포함하는지 확인한다.
    private static bool HasNonEmptyPatch(string directory)
    {
        var patchPath = Path.Combine(directory, "candidate.patch");
        return File.Exists(patchPath) && new FileInfo(patchPath).Length > 0;
    }

    // JSON 문자열 필드를 읽되 부재는 빈 문자열로 취급한다.
    private static string ReadRequired(JsonObject value, string name)
        => value[name]?.ToString() ?? "";

    // 셸 인자에 안전한 전체 길이 Git 커밋 식별자인지 확인한다.
    private static bool IsCommitId(string value)
        => value.Length == 40 && value.All(Uri.IsHexDigit);
}
