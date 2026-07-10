// Tier2Approver의 분기(적격성·캡·halt·이상감지·리뷰어 unavailable)를 실측하는 검증용 CLI.
// 임시 작업 공간 사본에서만 동작하며 실제 저장소·outbox는 건드리지 않는다.
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

public static class Tier2ApproverTestCli
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // 시나리오 이름을 받아 해당 분기를 실행하고 결과를 한 줄 JSON으로 출력한다.
    public static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: tier2test <scenario>");
            return 2;
        }

        var scenario = args[1];
        var scratchRoot = Path.Combine(Path.GetTempPath(), $"tier2test-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}");
        Directory.CreateDirectory(scratchRoot);

        try
        {
            var result = scenario switch
            {
                "eligible-approved" => RunEligibleApproved(scratchRoot),
                "core-file-touched" => RunCoreFileTouched(scratchRoot),
                "baseline-file-touched" => RunBaselineFileTouched(scratchRoot),
                "violations-increased" => RunViolationsIncreased(scratchRoot),
                "daily-cap" => RunDailyCap(scratchRoot),
                "anomaly-halt" => RunAnomalyHalt(scratchRoot),
                "reviewer-unavailable" => RunReviewerUnavailable(scratchRoot),
                "disabled" => RunDisabled(scratchRoot),
                _ => null,
            };

            if (result is null)
            {
                Console.Error.WriteLine(CliError($"unknown scenario: {scenario}").ToJsonString(JsonOptions));
                return 2;
            }

            Console.WriteLine(result.ToJsonString(JsonOptions));
            return 0;
        }
        finally
        {
            TryDeleteDirectory(scratchRoot);
        }
    }

    // 정상 게이트 클린 + AI 승인 시나리오 — imported가 돼야 한다.
    private static JsonObject RunEligibleApproved(string root)
    {
        var (outbox, approver, taskId, taskDirectory, meta) = CreateTask(root, ["README.md"], before: 3, after: 3, index: 0, enabled: true);
        var final = approver.MaybeAutoApprove(outbox, taskId, taskDirectory, meta, (_, _) => new ReviewOutcome(true, "no scope creep", "test-model"), () => 3);
        return Summarize(final);
    }

    // 기능 꺼짐 — 기존과 동일하게 import_pending으로 남아야 한다.
    private static JsonObject RunDisabled(string root)
    {
        var (outbox, approver, taskId, taskDirectory, meta) = CreateTask(root, ["README.md"], before: 3, after: 3, index: 0, enabled: false);
        var final = approver.MaybeAutoApprove(outbox, taskId, taskDirectory, meta, (_, _) => new ReviewOutcome(true, "should not be called", "test-model"), () => 3);
        return Summarize(final);
    }

    // 코어 파일(Engine.cs) 변경 — 자동 승인 대상에서 제외돼야 한다.
    private static JsonObject RunCoreFileTouched(string root)
    {
        var (outbox, approver, taskId, taskDirectory, meta) = CreateTask(root, ["server/Engine.cs"], before: 3, after: 3, index: 0, enabled: true);
        var final = approver.MaybeAutoApprove(outbox, taskId, taskDirectory, meta, (_, _) => new ReviewOutcome(true, "should not be called", "test-model"), () => 3);
        return Summarize(final);
    }

    // 기준 파일(workflow-definition.json) 변경 — 자동 승인 대상에서 제외돼야 한다.
    private static JsonObject RunBaselineFileTouched(string root)
    {
        var (outbox, approver, taskId, taskDirectory, meta) = CreateTask(root, ["dashboard/data/dev-pack/workflow-definition.json"], before: 3, after: 3, index: 0, enabled: true);
        var final = approver.MaybeAutoApprove(outbox, taskId, taskDirectory, meta, (_, _) => new ReviewOutcome(true, "should not be called", "test-model"), () => 3);
        return Summarize(final);
    }

    // 위반 수 증가 — 자동 승인 대상에서 제외돼야 한다.
    private static JsonObject RunViolationsIncreased(string root)
    {
        var (outbox, approver, taskId, taskDirectory, meta) = CreateTask(root, ["README.md"], before: 3, after: 5, index: 0, enabled: true);
        var final = approver.MaybeAutoApprove(outbox, taskId, taskDirectory, meta, (_, _) => new ReviewOutcome(true, "should not be called", "test-model"), () => 3);
        return Summarize(final);
    }

    // 리뷰어(Ollama) 연결 불가 — 사람 대기로 남아야 한다.
    private static JsonObject RunReviewerUnavailable(string root)
    {
        var (outbox, approver, taskId, taskDirectory, meta) = CreateTask(root, ["README.md"], before: 3, after: 3, index: 0, enabled: true);
        // Endpoint를 존재하지 않는 포트로 둬서 실제 연결 실패 경로를 그대로 타게 한다(override 없음).
        var final = approver.MaybeAutoApprove(outbox, taskId, taskDirectory, meta);
        return Summarize(final);
    }

    // 일일 캡 도달 — 적격이어도 6번째 건은 막혀야 한다.
    private static JsonObject RunDailyCap(string root)
    {
        var outbox = new OutboxManager(root);
        var approver = new Tier2Approver(Path.GetFullPath(root), new Tier2ApproverOptions(true, 5, "test", "test-model", null, "http://127.0.0.1:1", 5));
        JsonObject? last = null;

        for (var index = 0; index < 6; index += 1)
        {
            var (taskId, taskDirectory, meta) = WriteTask(root, ["README.md"], before: 3, after: 3, index);
            last = approver.MaybeAutoApprove(outbox, taskId, taskDirectory, meta, (_, _) => new ReviewOutcome(true, "ok", "test-model"), () => 3);
        }

        return Summarize(last!);
    }

    // 반입 후 재측정에서 위반이 늘어난 이상 상황 — halt로 전환되고 다음 건도 막혀야 한다.
    private static JsonObject RunAnomalyHalt(string root)
    {
        var outbox = new OutboxManager(root);
        var approver = new Tier2Approver(Path.GetFullPath(root), new Tier2ApproverOptions(true, 5, "test", "test-model", null, "http://127.0.0.1:1", 5));

        var (taskId1, taskDirectory1, meta1) = WriteTask(root, ["README.md"], before: 3, after: 3, 0);
        var first = approver.MaybeAutoApprove(outbox, taskId1, taskDirectory1, meta1, (_, _) => new ReviewOutcome(true, "ok", "test-model"), () => 7);

        var (taskId2, taskDirectory2, meta2) = WriteTask(root, ["README.md"], before: 3, after: 3, 1);
        var second = approver.MaybeAutoApprove(outbox, taskId2, taskDirectory2, meta2, (_, _) => new ReviewOutcome(true, "ok", "test-model"), () => 3);

        return new JsonObject
        {
            ["firstDecision"] = first["tier2"]?["decision"]?.DeepClone(),
            ["secondDecision"] = second["tier2"]?["decision"]?.DeepClone(),
        };
    }

    // outbox + 단일 task + Tier2Approver 인스턴스를 함께 준비한다.
    private static (OutboxManager Outbox, Tier2Approver Approver, string TaskId, string TaskDirectory, JsonObject Meta) CreateTask(
        string root, string[] changedFiles, int before, int after, int index, bool enabled)
    {
        var outbox = new OutboxManager(root);
        var approver = new Tier2Approver(Path.GetFullPath(root), new Tier2ApproverOptions(enabled, 5, "test", "test-model", null, "http://127.0.0.1:1", 5));
        var (taskId, taskDirectory, meta) = WriteTask(root, changedFiles, before, after, index);
        return (outbox, approver, taskId, taskDirectory, meta);
    }

    // outbox task 디렉터리와 meta.json/diff.patch/files를 실제로 만든다.
    private static (string TaskId, string TaskDirectory, JsonObject Meta) WriteTask(string root, string[] changedFiles, int before, int after, int index)
    {
        var taskId = $"task-{index:0000}";
        var taskDirectory = Path.Combine(root, "outbox", taskId);
        Directory.CreateDirectory(Path.Combine(taskDirectory, "files"));
        WriteText(Path.Combine(taskDirectory, "diff.patch"), "--- a/README.md\n+++ b/README.md\n@@\n+test line\n");

        var meta = new JsonObject
        {
            ["schemaVersion"] = 2,
            ["taskId"] = taskId,
            ["projectId"] = "dev-pack",
            ["instruction"] = "test instruction",
            ["status"] = "import_pending",
            ["changedFiles"] = new JsonArray(changedFiles.Select(file => (JsonNode)JsonValue.Create(file)).ToArray()),
            ["deletedFiles"] = new JsonArray(),
            ["gateViolationsBefore"] = before,
            ["gateViolationsAfter"] = after,
        };
        WriteJson(Path.Combine(taskDirectory, "meta.json"), meta);

        foreach (var file in changedFiles)
        {
            var stored = Path.Combine(taskDirectory, "files", file.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(stored)!);
            WriteText(stored, "test content" + Environment.NewLine);
        }

        return (taskId, taskDirectory, meta);
    }

    // 결과 요약(상태·결정·적격성·사유)만 뽑아 CLI 출력으로 만든다.
    private static JsonObject Summarize(JsonObject meta)
    {
        var tier2 = meta["tier2"]?.AsObject();
        return new JsonObject
        {
            ["status"] = meta["status"]?.DeepClone(),
            ["decision"] = tier2?["decision"]?.DeepClone(),
            ["eligible"] = tier2?["eligible"]?.DeepClone(),
            ["reason"] = tier2?["reason"]?.DeepClone() ?? tier2?["reviewer"]?["reason"]?.DeepClone(),
        };
    }

    // 텍스트 파일을 기록한다.
    private static void WriteText(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, Encoding.UTF8);
    }

    // JSON 파일을 기록한다.
    private static void WriteJson(string path, JsonNode node)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
    }

    // 임시 디렉터리를 정리한다.
    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // 임시 디렉터리 정리 실패는 결과에 영향 없다.
        }
    }

    // CLI 오류를 JSON으로 만든다.
    private static JsonObject CliError(string message) => new() { ["error"] = message };
}
