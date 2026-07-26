// 게이트 판정 보고서를 근거로 쓸 수 있는지 한 곳에서 판정한다.
// trust-origin과 program-verify가 같은 규칙을 두 벌 갖지 않게 하려는 것이다 —
// 갈리면 한쪽이 받는 보고를 다른 쪽이 거절하고, 그 어긋남이 ADR-016 §6의 사건이었다.
using System.Text;
using System.Text.Json.Nodes;

internal static class GateReportReader
{
    // 판정 산출자로 인정하는 이름이다. ADR-016 §15로 정본은 di-completion-check이며,
    // 기존 보고 21건이 program-verify 산이라 둘 다 받는다. 모르는 이름은 받지 않는다 —
    // 아무거나 받으면 판정하지 않은 산출을 근거로 적는 것이 된다.
    private static readonly string[] KnownProducers = ["di-completion-check", "program-verify"];

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    // 보고서를 읽어 근거로 쓸 수 있는지 본다. 쓸 수 있으면 null과 함께 report를 채운다.
    // 통과 조건을 모두 만족해야 null이다 — 모르는 것은 통과로 적지 않는다.
    internal static string? Reject(string root, string reportPath, string expectedGateId, out JsonObject? report)
    {
        report = null;
        var full = Path.GetFullPath(Path.IsPathRooted(reportPath) ? reportPath : Path.Combine(root, reportPath));
        if (!File.Exists(full)) return "gate-report-missing";
        try { report = JsonNode.Parse(File.ReadAllText(full, Utf8NoBom))?.AsObject(); }
        catch { return "gate-report-unparsable"; }
        if (report is null) return "gate-report-unparsable";

        // 두 러너는 필드 이름만 다르다 — verifier/harness, verdict/gateVerdict.
        var producer = Text(report, "verifier") is { Length: > 0 } v ? v : Text(report, "harness");
        if (!KnownProducers.Contains(producer, StringComparer.Ordinal)) return "gate-report-wrong-verifier";
        if (!string.Equals(Text(report, "gateId"), expectedGateId, StringComparison.OrdinalIgnoreCase))
            return "gate-report-wrong-gate";
        var verdict = Text(report, "gateVerdict") is { Length: > 0 } gv ? gv : Text(report, "verdict");
        if (verdict != "PASS") return "gate-report-not-passing";

        // 낡은 통과 보고서를 나중에 다시 들이미는 것을 막는다. 잰 커밋이 지금 HEAD여야 한다.
        if (Text(report, "baselineCommit") != GitTools.RunGitText(root, "rev-parse HEAD")?.Trim())
            return "gate-report-baseline-mismatch";
        // 더러운 트리에서 잰 판정에 깨끗한 커밋 해시만 붙이면, 커밋되지 않은 코드로 통과하고
        // 그 커밋을 쟀다고 말하는 것이 된다.
        if (report["worktreeCleanAtStart"]?.GetValue<bool>() != true) return "gate-report-dirty-worktree";

        var checks = report["checks"]?.AsArray();
        if (checks is null || checks.Count == 0) return "gate-report-no-checks";
        foreach (var check in checks)
        {
            var expected = check?["expectedExit"]?.GetValue<int>();
            var actual = check?["actualExit"]?.GetValue<int>();
            if (expected is null || actual is null || expected != actual) return "gate-report-check-mismatch";
        }

        return null;
    }

    // 보고서에 실제로 들어 있는 검사 명령 이름을 모은다.
    internal static HashSet<string> CommandsIn(JsonObject report)
        => (report["checks"]?.AsArray() ?? [])
            .Select(c => c?["command"]?.GetValue<string>() ?? "")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    // JSON 문자열 필드를 읽되 부재는 빈 문자열로 취급한다.
    private static string Text(JsonObject report, string name) => report[name]?.ToString() ?? "";
}
