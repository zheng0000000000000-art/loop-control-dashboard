// 현재 회차의 측정·생성·검토·사람 대기 시간을 집계한다.
// run-log와 proposal만 읽어 대시보드 요약 JSON을 만든다.
using System.Globalization;
using System.Text.Json.Nodes;

public static class CycleSummaryBuilder
{
    // run-log와 현재 proposal로 회차 시간 분해 JSON을 만든다.
    public static JsonObject Build(JsonObject state, JsonObject runLog, JsonObject proposal)
    {
        var loopIteration = Engine.GetLoopIteration(state);
        var entries = EntriesForLoop(runLog, loopIteration);
        var measurementMs = SumEventDuration(entries, "measure.completed");
        var generationMs = SumEventDuration(entries, "proposal.generated");
        var reviewMs = SumEventDuration(entries, "review.tier1_completed");
        var humanWaitingMs = CalculateHumanWaitingMs(entries, proposal);

        return new JsonObject
        {
            ["schemaVersion"] = 2,
            ["loopIteration"] = loopIteration,
            ["segments"] = new JsonObject
            {
                ["measurementMs"] = measurementMs,
                ["generationMs"] = generationMs,
                ["reviewMs"] = reviewMs,
                ["humanWaitingMs"] = humanWaitingMs,
                ["totalMs"] = measurementMs + generationMs + reviewMs + humanWaitingMs,
            },
        };
    }

    // 현재 루프의 실행 로그 항목을 시간순으로 고른다.
    private static List<JsonObject> EntriesForLoop(JsonObject runLog, int loopIteration)
    {
        return (runLog["entries"]?.AsArray() ?? new JsonArray())
            .OfType<JsonObject>()
            .Where(entry => Number(entry["loopIteration"], loopIteration) == loopIteration)
            .OrderBy(entry => entry["createdAt"]?.GetValue<string>() ?? "", StringComparer.Ordinal)
            .ToList();
    }

    // 특정 이벤트의 durationMs 값을 합산한다.
    private static long SumEventDuration(List<JsonObject> entries, string eventName)
    {
        return entries
            .Where(entry => entry["event"]?.GetValue<string>() == eventName)
            .Select(entry => Number(entry["params"]?.AsObject()["durationMs"], 0))
            .Sum(value => (long)Math.Max(0, value));
    }

    // 제출된 proposal이 사람 결재를 기다린 시간을 계산한다.
    private static long CalculateHumanWaitingMs(List<JsonObject> entries, JsonObject proposal)
    {
        var proposalId = proposal["id"]?.GetValue<string>() ?? "";
        if (string.IsNullOrWhiteSpace(proposalId))
        {
            return 0;
        }

        var start = ProposalCreatedAt(entries, proposalId);
        if (start is null)
        {
            return 0;
        }

        var decidedAt = ProposalDecidedAt(entries, proposalId);
        if (decidedAt is not null)
        {
            return Math.Max(0, (long)(decidedAt.Value - start.Value).TotalMilliseconds);
        }

        return proposal["lifecycle"]?.GetValue<string>() == "submitted"
            ? Math.Max(0, (long)(DateTimeOffset.Now - start.Value).TotalMilliseconds)
            : 0;
    }

    // proposal.created 로그 시각을 찾는다.
    private static DateTimeOffset? ProposalCreatedAt(List<JsonObject> entries, string proposalId)
    {
        var createdAt = entries
            .Where(entry => entry["event"]?.GetValue<string>() == "proposal.created" &&
                entry["params"]?.AsObject()["proposalId"]?.GetValue<string>() == proposalId)
            .Select(entry => entry["createdAt"]?.GetValue<string>())
            .FirstOrDefault();

        return DateTimeOffset.TryParse(createdAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
            ? start
            : null;
    }

    // 승인 또는 거절 로그 시각을 찾는다.
    private static DateTimeOffset? ProposalDecidedAt(List<JsonObject> entries, string proposalId)
    {
        var decidedAt = entries
            .Where(entry => (entry["event"]?.GetValue<string>() == "review.approved" ||
                    entry["event"]?.GetValue<string>() == "review.rejected") &&
                entry["params"]?.AsObject()["proposalId"]?.GetValue<string>() == proposalId)
            .Select(entry => entry["createdAt"]?.GetValue<string>())
            .FirstOrDefault();

        return DateTimeOffset.TryParse(decidedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var end)
            ? end
            : null;
    }

    // 노드에서 정수 값을 읽는다.
    private static int Number(JsonNode? node, int fallback)
    {
        return node is not null && int.TryParse(node.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    }
}
