// 회차(cycle) 시간 분해 요약을 구성하는 순수 정적 빌더.
using System.Globalization;
using System.Text.Json.Nodes;

internal static class CycleSummaryBuilder
{
    // run-log와 현재 proposal로 회차 시간 분해 JSON을 만든다.
    internal static JsonObject BuildCycleSummary(JsonObject state, JsonObject runLog, JsonObject proposal)
    {
        var loopIteration = Engine.GetLoopIteration(state);
        var entries = (runLog["entries"]?.AsArray() ?? new JsonArray())
            .OfType<JsonObject>()
            .Where(entry => Number(entry["loopIteration"], loopIteration) == loopIteration)
            .OrderBy(entry => entry["createdAt"]?.GetValue<string>() ?? "", StringComparer.Ordinal)
            .ToList();
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
        var lifecycle = proposal["lifecycle"]?.GetValue<string>() ?? "";

        if (string.IsNullOrWhiteSpace(proposalId))
        {
            return 0;
        }

        var createdAt = entries
            .Where(entry => entry["event"]?.GetValue<string>() == "proposal.created" &&
                entry["params"]?.AsObject()["proposalId"]?.GetValue<string>() == proposalId)
            .Select(entry => entry["createdAt"]?.GetValue<string>())
            .FirstOrDefault();

        if (!DateTimeOffset.TryParse(createdAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
        {
            return 0;
        }

        var decidedAt = entries
            .Where(entry => (entry["event"]?.GetValue<string>() == "review.approved" ||
                    entry["event"]?.GetValue<string>() == "review.rejected") &&
                entry["params"]?.AsObject()["proposalId"]?.GetValue<string>() == proposalId)
            .Select(entry => entry["createdAt"]?.GetValue<string>())
            .FirstOrDefault();

        if (DateTimeOffset.TryParse(decidedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
        {
            return Math.Max(0, (long)(end - start).TotalMilliseconds);
        }

        return lifecycle == "submitted"
            ? Math.Max(0, (long)(DateTimeOffset.Now - start).TotalMilliseconds)
            : 0;
    }

    // 노드에서 정수 값을 읽는다(Program.cs 로컬 함수 접근 불가로 복사).
    private static int Number(JsonNode? node, int fallback)
    {
        return node is not null && int.TryParse(node.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    }
}
