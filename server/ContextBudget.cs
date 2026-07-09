// dispatch 격리 사본에 포함된 파일 전체의 컨텍스트 예산(바이트·추정 토큰·파일 수)을 실측한다.
// 측정값을 outbox task 메타에 붙이고 프로젝트 run-log에 context.budget 이벤트로 남긴다.
using System.Text.Json.Nodes;

public static class ContextBudget
{
    // 사본 파일 전체를 합산해 컨텍스트 예산 측정값을 만든다.
    public static JsonObject Measure(string copyRoot)
    {
        // run-log는 본 계측 자신이 이벤트를 추가하는 파일이라 합산에서 제외한다(재dispatch 결정론 보장).
        var files = Directory.EnumerateFiles(copyRoot, "*", SearchOption.AllDirectories)
            .Where(file => !IsSelfAppendedEventLog(copyRoot, file))
            .ToList();
        var contextBytes = files.Sum(file => new FileInfo(file).Length);

        return new JsonObject
        {
            ["contextBytes"] = contextBytes,
            // estimatedContextTokens 산정 방식: contextBytes / 4 (바이트당 1/4토큰 근사, 추정값)
            ["estimatedContextTokens"] = contextBytes / 4,
            ["contextFileCount"] = files.Count,
            ["contextTokensEstimation"] = "contextBytes/4",
        };
    }

    // 측정값을 task 메타에 붙이고 run-log 이벤트 기록까지 수행한다.
    public static void Attach(JsonObject meta, string workspaceRoot, string projectId, string taskId, JsonObject budget)
    {
        foreach (var pair in budget.ToList())
        {
            meta[pair.Key] = pair.Value?.DeepClone();
        }

        AppendRunLogEvent(workspaceRoot, projectId, taskId, budget);
    }

    // 프로젝트 run-log에 context.budget 이벤트를 기존 로그 규약대로 추가한다.
    private static void AppendRunLogEvent(string workspaceRoot, string projectId, string taskId, JsonObject budget)
    {
        var storage = new Storage(Path.Combine(workspaceRoot, "dashboard", "data"));
        var state = storage.ReadProjectFile(projectId, Storage.StateFile).AsObject();
        var runLog = storage.ReadProjectFile(projectId, Storage.RunLogFile).AsObject();
        runLog = Engine.AppendLog(runLog, new JsonObject
        {
            ["event"] = "context.budget",
            ["params"] = new JsonObject
            {
                ["taskId"] = taskId,
                ["contextBytes"] = budget["contextBytes"]?.DeepClone(),
                ["estimatedContextTokens"] = budget["estimatedContextTokens"]?.DeepClone(),
                ["contextFileCount"] = budget["contextFileCount"]?.DeepClone(),
                ["estimation"] = budget["contextTokensEstimation"]?.DeepClone(),
            },
            ["level"] = "info",
            ["producedBy"] = new JsonObject { ["provider"] = "rule-engine", ["model"] = null },
            ["cost"] = new JsonObject
            {
                ["inputTokens"] = 0,
                ["outputTokens"] = 0,
                ["estimatedUSD"] = 0,
                ["subscriptionCalls"] = 0,
                ["role"] = "runtime",
            },
        }, Engine.GetLoopIteration(state));
        storage.WriteProjectFile(projectId, Storage.RunLogFile, runLog);
    }

    // 프로젝트 데이터의 run-log 파일 경로인지 확인한다.
    private static bool IsSelfAppendedEventLog(string copyRoot, string file)
    {
        var relative = Path.GetRelativePath(copyRoot, file).Replace('\\', '/');
        return relative.StartsWith("dashboard/data/", StringComparison.OrdinalIgnoreCase) &&
            relative.EndsWith("/" + Storage.RunLogFile, StringComparison.OrdinalIgnoreCase);
    }
}
