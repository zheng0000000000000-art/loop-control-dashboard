// 사람 전용 게이트(결재·반입·기준변경)가 비사람 주체에 의해 수행된 흔적을 감사하는 하네스 CLI.
// 북극성 고정점("결재·반입·기준변경은 항상 사람")의 회귀 테스트. 검출만 하고 되돌리지 않는다.
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

internal static class GateAuditCli
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // 자동 프로세스가 남기는 커밋 서명. 사람이 아닌 주체의 표식이다.
    private static readonly string[] AutomationSignatures = { "[loop]", "[auto]", "[bot]" };

    // 사람 전용 게이트에 해당하는 액션 이름.
    private static readonly string[] GateActions =
    {
        "approve-import", "reject-import", "approve", "reject", "acknowledge-guardrail", "acknowledge",
    };

    // 기준 파일 — 변경은 사람 결재 사항이다.
    private static readonly string[] BaselineFiles = { "blueprint.json", "workflow-definition.json" };

    // gate-audit 진입점. exit 0=위반 없음, 1=위반 검출, 2=오류.
    internal static int Run(string[] args)
    {
        try
        {
            var repoRoot = GitTools.FindRepoRoot();
            var since = ReadSince(args);
            var range = string.IsNullOrEmpty(since) ? "" : $"{since}..HEAD";

            var violations = new JsonArray();
            var log = GitTools.RunGitText(repoRoot, $"log --pretty=format:%h|~|%an|~|%aI|~|%s {range}") ?? "";

            foreach (var line in log.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var f = line.Split("|~|", StringSplitOptions.None);
                if (f.Length < 4) continue;
                var (hash, author, when, subject) = (f[0], f[1], f[2], f[3]);

                // A. 자동 서명 + 결재 액션 = 무인 결재 커밋.
                if (HasAutomationSignature(subject) && FindGateAction(subject) is { } action)
                {
                    violations.Add(new JsonObject
                    {
                        ["kind"] = "A",
                        ["commit"] = hash,
                        ["author"] = author,
                        ["when"] = when,
                        ["action"] = action,
                        ["target"] = ExtractProposalId(subject),
                        ["evidence"] = subject,
                        ["why"] = "자동 서명 커밋이 사람 전용 결재 액션을 수행함 — 고정점 위반",
                    });
                }

                // C. 자동 서명 + 기준 파일 변경 = 무단 기준 변경.
                if (HasAutomationSignature(subject) && TouchesBaseline(repoRoot, hash) is { } touched)
                {
                    violations.Add(new JsonObject
                    {
                        ["kind"] = "C",
                        ["commit"] = hash,
                        ["author"] = author,
                        ["when"] = when,
                        ["action"] = "baseline-change",
                        ["target"] = touched,
                        ["evidence"] = subject,
                        ["why"] = "자동 서명 커밋이 기준 파일을 변경함 — 기준 변경은 사람 결재",
                    });
                }
            }

            var report = new JsonObject
            {
                ["harness"] = "gate-audit",
                ["since"] = string.IsNullOrEmpty(since) ? "(전체 이력)" : since,
                ["violationCount"] = violations.Count,
                ["verdict"] = violations.Count == 0 ? "CLEAN" : "VIOLATION",
                ["violations"] = violations,
                ["note"] = "검출만 한다. 되돌리기·결재·정책 변경은 사람의 몫이다.",
            };
            Console.WriteLine(report.ToJsonString(JsonOptions));
            return violations.Count == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{{\"error\":\"gate-audit 실패: {ex.Message}\"}}");
            return 2;
        }
    }

    // --since <commit> 인자를 읽는다.
    private static string ReadSince(string[] args)
    {
        for (var i = 1; i < args.Length - 1; i++)
            if (string.Equals(args[i], "--since", StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return "";
    }

    // 커밋 제목에 자동 프로세스 서명이 있는지 확인한다.
    private static bool HasAutomationSignature(string subject)
        => AutomationSignatures.Any(s => subject.Contains(s, StringComparison.OrdinalIgnoreCase));

    // 커밋 제목에서 사람 전용 게이트 액션을 찾는다. 없으면 null.
    private static string? FindGateAction(string subject)
        => GateActions.FirstOrDefault(a => subject.Contains(a, StringComparison.OrdinalIgnoreCase));

    // 커밋 제목에서 대상 proposal id를 뽑는다.
    private static string ExtractProposalId(string subject)
    {
        var m = Regex.Match(subject, @"proposal-\d+");
        return m.Success ? m.Value : "(불명)";
    }

    // 해당 커밋이 기준 파일을 건드렸는지 확인한다. 건드렸으면 파일명, 아니면 null.
    private static string? TouchesBaseline(string repoRoot, string hash)
    {
        var files = GitTools.RunGitText(repoRoot, $"show --pretty=format: --name-only {hash}") ?? "";
        foreach (var b in BaselineFiles)
            if (files.Contains(b, StringComparison.OrdinalIgnoreCase))
                return b;
        return null;
    }
}
