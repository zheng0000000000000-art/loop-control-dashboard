// 단계 상태와 게이트 판정을 계산한다.
// 파일 저장과 표시 문구는 이 파일에서 다루지 않는다.
using System.Globalization;
using System.Text.Json.Nodes;

public static class Engine
{
    private static readonly HashSet<string> StatusValues =
    [
        "not_started",
        "in_progress",
        "completed",
        "passed",
        "warning",
        "blocked",
        "pending_review",
        "approved",
        "failed",
    ];

    private static readonly HashSet<string> BlockKindValues = ["waiting", "gate_blocked", "failed_upstream"];
    private static readonly HashSet<string> LoopStateValues = ["running", "paused", "halted", "aligned"];
    private static readonly HashSet<string> ModeValues = ["normal", "degraded"];
    private static readonly string[] PriorityStatus = ["failed", "failed_upstream", "gate_blocked", "warning", "pending_review", "in_progress"];
    private static readonly HashSet<string> DoneStatuses = ["completed", "passed", "approved"];

    // JSON 객체 복사본을 만든다.
    public static JsonObject CloneObject(JsonObject value)
    {
        return JsonNode.Parse(value.ToJsonString())!.AsObject();
    }

    // 현재 반복 횟수를 반환한다.
    public static int GetLoopIteration(JsonObject state)
    {
        return Number(state["loopIteration"], 0);
    }

    // 단계의 현재 상태값을 반환한다.
    public static string GetStageStatus(JsonObject state, string stageId)
    {
        return state["stages"]?.AsObject()[stageId]?.GetValue<string>() ?? "not_started";
    }

    // 단계 정의를 ID로 찾는다.
    public static JsonObject? GetStage(JsonObject definition, string stageId)
    {
        return Stages(definition)
            .OfType<JsonObject>()
            .FirstOrDefault(stage => stage["id"]?.GetValue<string>() == stageId);
    }

    // 차단 단계의 종류를 반환한다.
    public static string? GetBlockKind(JsonObject state, string stageId)
    {
        if (GetStageStatus(state, stageId) != "blocked")
        {
            return null;
        }

        var kind = state["blockInfo"]?.AsObject()[stageId]?.AsObject()["kind"]?.GetValue<string>();
        return kind is not null && BlockKindValues.Contains(kind) ? kind : "waiting";
    }

    // 사람 검토가 필요한 단계를 찾는다.
    public static JsonObject? GetHumanReviewStage(JsonObject definition, JsonObject? state = null)
    {
        var reviewStages = Stages(definition)
            .OfType<JsonObject>()
            .Where(stage => Bool(stage["requiresHuman"]))
            .ToList();

        if (state is null)
        {
            return reviewStages.FirstOrDefault();
        }

        return reviewStages.FirstOrDefault(stage => GetStageStatus(state, stage["id"]!.GetValue<string>()) == "pending_review")
            ?? reviewStages.FirstOrDefault();
    }

    // 현재 단계 다음 순서의 단계를 반환한다.
    public static JsonObject? GetNextStage(JsonObject definition, string stageId)
    {
        var stages = Stages(definition).OfType<JsonObject>().ToList();
        var index = stages.FindIndex(stage => stage["id"]?.GetValue<string>() == stageId);
        return index >= 0 && index + 1 < stages.Count ? stages[index + 1] : null;
    }

    // 단계의 게이트 조건 충족 여부를 계산한다.
    public static GateEvaluation EvaluateGate(JsonObject definition, JsonObject state, string stageId)
    {
        var stage = GetStage(definition, stageId);
        var conditions = stage?["gate"]?.AsArray() ?? new JsonArray();
        var checks = conditions
            .OfType<JsonObject>()
            .Select(condition => EvaluateGateCondition(state, condition))
            .ToList();

        return new GateEvaluation(stageId, checks.Count > 0, checks.All(check => check.Passed), checks);
    }

    // 전체 상태 표시값을 계산한다.
    public static string ComputeOverallStatus(JsonObject definition, JsonObject state)
    {
        var statuses = Stages(definition)
            .OfType<JsonObject>()
            .Select(stage =>
            {
                var stageId = stage["id"]!.GetValue<string>();
                var status = GetStageStatus(state, stageId);
                var blockKind = GetBlockKind(state, stageId);
                return blockKind == "waiting" ? "not_started" : blockKind ?? status;
            })
            .ToList();
        var priority = PriorityStatus.FirstOrDefault(statuses.Contains);

        if (priority is not null)
        {
            return priority;
        }

        var effective = statuses.Where(status => status != "not_started").ToList();
        return effective.Count > 0 && effective.All(DoneStatuses.Contains) ? "completed" : "not_started";
    }

    // 단일 단계 상태를 갱신한다.
    public static JsonObject ApplyStageStatus(JsonObject definition, JsonObject state, string stageId, string status)
    {
        if (!StatusValues.Contains(status))
        {
            throw new InvalidOperationException($"Unknown status: {status}");
        }

        var nextState = CloneObject(state);
        nextState["stages"] ??= new JsonObject();
        nextState["stages"]!.AsObject()[stageId] = status;
        nextState["blockInfo"] ??= new JsonObject();

        if (status != "blocked")
        {
            nextState["blockInfo"]!.AsObject().Remove(stageId);
        }

        nextState["currentStage"] = stageId;
        nextState["overallStatus"] = ComputeOverallStatus(definition, nextState);
        nextState["lastUpdated"] = DateTimeOffset.Now.ToString("O");
        return nextState;
    }

    // 상태 변경 묶음을 적용하고 전체 상태를 갱신한다.
    public static JsonObject ApplyStatePatch(JsonObject definition, JsonObject state, JsonObject patch)
    {
        var nextState = CloneObject(state);

        if (patch["currentStage"] is not null)
        {
            nextState["currentStage"] = patch["currentStage"]!.GetValue<string>();
        }

        if (patch["loopIteration"] is not null)
        {
            nextState["loopIteration"] = Number(patch["loopIteration"], 0);
        }

        if (patch["loopState"] is not null)
        {
            var loopState = patch["loopState"]!.GetValue<string>();
            if (LoopStateValues.Contains(loopState))
            {
                nextState["loopState"] = loopState;

                if (loopState is "running" or "aligned")
                {
                    nextState.Remove("haltedAt");
                    nextState.Remove("pausedAt");
                    nextState.Remove("pausedBy");
                    nextState.Remove("haltedBy");
                    nextState.Remove("checkpointId");
                }
            }
        }

        if (patch["mode"] is not null)
        {
            var mode = patch["mode"]!.GetValue<string>();
            if (ModeValues.Contains(mode))
            {
                nextState["mode"] = mode;
            }
        }

        if (patch["suspendedTracks"] is not null)
        {
            nextState["suspendedTracks"] = CloneNode(patch["suspendedTracks"]!);
        }

        if (patch["applyBaselineViolations"] is not null)
        {
            nextState["applyBaselineViolations"] = CloneNode(patch["applyBaselineViolations"]!);
        }

        if (patch["stageStatuses"] is JsonObject stageStatuses)
        {
            ApplyStageStatuses(nextState, stageStatuses);
        }

        if (patch["blockInfo"] is JsonObject blockInfo)
        {
            ApplyBlockInfo(nextState, blockInfo);
        }

        var requestedOverall = patch["overallStatus"]?.GetValue<string>();
        nextState["overallStatus"] = IsKnownOverallStatus(requestedOverall) ? requestedOverall : ComputeOverallStatus(definition, nextState);
        nextState["lastUpdated"] = DateTimeOffset.Now.ToString("O");
        return nextState;
    }

    // 단계 상태 묶음을 nextState에 적용한다.
    private static void ApplyStageStatuses(JsonObject nextState, JsonObject stageStatuses)
    {
        nextState["stages"] ??= new JsonObject();
        nextState["blockInfo"] ??= new JsonObject();

        foreach (var (stageId, statusNode) in stageStatuses)
        {
            var status = statusNode?.GetValue<string>() ?? "not_started";
            nextState["stages"]!.AsObject()[stageId] = status;

            if (status != "blocked")
            {
                nextState["blockInfo"]!.AsObject().Remove(stageId);
            }
        }
    }

    // 차단 정보 묶음을 nextState에 적용한다.
    private static void ApplyBlockInfo(JsonObject nextState, JsonObject blockInfo)
    {
        nextState["blockInfo"] ??= new JsonObject();

        foreach (var (stageId, infoNode) in blockInfo)
        {
            if (infoNode is null)
            {
                nextState["blockInfo"]!.AsObject().Remove(stageId);
                continue;
            }

            var info = CloneNode(infoNode).AsObject();
            var kind = info["kind"]?.GetValue<string>();
            info["kind"] = kind is not null && BlockKindValues.Contains(kind) ? kind : "waiting";
            nextState["blockInfo"]!.AsObject()[stageId] = info;
        }
    }

    // 게이트 차단 상태와 차단 종류를 함께 적용한다.
    public static JsonObject ApplyGateBlockedPatch(JsonObject definition, JsonObject state, JsonObject patch, string stageId)
    {
        var blockedPatch = CloneObject(patch);
        blockedPatch["stageStatuses"] ??= new JsonObject();
        blockedPatch["stageStatuses"]!.AsObject()[stageId] = "blocked";
        blockedPatch["blockInfo"] ??= new JsonObject();
        blockedPatch["blockInfo"]!.AsObject()[stageId] = new JsonObject { ["kind"] = "gate_blocked" };
        return ApplyStatePatch(definition, state, blockedPatch);
    }

    // 실행 로그 항목을 표준 형태로 추가한다.
    public static JsonObject AppendLog(JsonObject runLog, JsonObject entry, int loopIteration)
    {
        var nextRunLog = CloneObject(runLog);
        nextRunLog["schemaVersion"] = 3;
        var entries = nextRunLog["entries"]?.AsArray() ?? new JsonArray();
        nextRunLog["entries"] = entries;
        entries.Add(new JsonObject
        {
            ["createdAt"] = entry["createdAt"]?.GetValue<string>() ?? DateTimeOffset.Now.ToString("O"),
            ["event"] = entry["event"]?.GetValue<string>() ?? "unknown.event",
            ["params"] = CloneNode(entry["params"] ?? new JsonObject()),
            ["level"] = entry["level"]?.GetValue<string>() ?? "info",
            ["producedBy"] = NormalizeProducedBy(entry["producedBy"] as JsonObject),
            ["attempt"] = Number(entry["attempt"], 1),
            ["loopIteration"] = loopIteration,
            ["cost"] = NormalizeCost(entry["cost"] as JsonObject),
        });
        return nextRunLog;
    }

    // 예상 비용 합계를 계산한다.
    public static decimal SumEstimatedCost(JsonObject runLog)
    {
        return Entries(runLog).Sum(entry =>
        {
            var cost = entry?.AsObject()["cost"] as JsonObject;
            return DecimalNumber(cost?["estimatedUSD"], 0);
        });
    }

    // 구독 호출 합계를 계산한다.
    public static int SumSubscriptionCalls(JsonObject runLog)
    {
        return Entries(runLog).Sum(entry =>
        {
            var cost = entry?.AsObject()["cost"] as JsonObject;
            return Number(cost?["subscriptionCalls"], 0);
        });
    }

    // JsonNode 복사본을 만든다.
    public static JsonNode CloneNode(JsonNode value)
    {
        return JsonNode.Parse(value.ToJsonString())!;
    }

    // 단계 배열을 반환한다.
    private static JsonArray Stages(JsonObject definition)
    {
        return definition["stages"]?.AsArray() ?? new JsonArray();
    }

    // 실행 로그 배열을 반환한다.
    private static JsonArray Entries(JsonObject runLog)
    {
        return runLog["entries"]?.AsArray() ?? new JsonArray();
    }

    // 단일 게이트 조건을 평가한다.
    private static GateCheck EvaluateGateCondition(JsonObject state, JsonObject condition)
    {
        if (condition["check"]?.GetValue<string>() == "stageStatus")
        {
            var stageId = condition["stage"]?.GetValue<string>() ?? "";
            var actual = GetStageStatus(state, stageId);
            var allowed = condition["mustBe"]?.AsArray().Select(node => node?.GetValue<string>()).Where(value => value is not null).ToHashSet();
            return new GateCheck(condition, actual, allowed?.Contains(actual) ?? false);
        }

        return new GateCheck(condition, null, false);
    }

    // 전체 상태 표시값 포함 여부를 확인한다.
    private static bool IsKnownOverallStatus(string? status)
    {
        return status is not null && (StatusValues.Contains(status) || BlockKindValues.Contains(status));
    }

    // 실행자 정보를 표준 형태로 만든다.
    private static JsonObject NormalizeProducedBy(JsonObject? producedBy)
    {
        return new JsonObject
        {
            ["provider"] = producedBy?["provider"]?.GetValue<string>() ?? "local-server",
            ["model"] = producedBy?["model"]?.DeepClone(),
        };
    }

    // 비용 정보를 표준 형태로 만든다.
    private static JsonObject NormalizeCost(JsonObject? cost)
    {
        return new JsonObject
        {
            ["inputTokens"] = Number(cost?["inputTokens"], 0),
            ["outputTokens"] = Number(cost?["outputTokens"], 0),
            ["estimatedUSD"] = DecimalNumber(cost?["estimatedUSD"], 0),
            ["subscriptionCalls"] = Number(cost?["subscriptionCalls"], 0),
            ["role"] = cost?["role"]?.GetValue<string>() == "dev" ? "dev" : "runtime",
        };
    }

    // 노드에서 bool 값을 읽는다.
    private static bool Bool(JsonNode? node)
    {
        return node is not null && node.GetValue<bool>();
    }

    // 노드에서 정수 값을 읽는다.
    private static int Number(JsonNode? node, int fallback)
    {
        if (node is null)
        {
            return fallback;
        }

        return int.TryParse(node.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    }

    // 노드에서 decimal 값을 읽는다.
    private static decimal DecimalNumber(JsonNode? node, decimal fallback)
    {
        if (node is null)
        {
            return fallback;
        }

        return decimal.TryParse(node.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    }
}

public sealed record GateEvaluation(string StageId, bool HasGate, bool Passed, List<GateCheck> Checks);

public sealed record GateCheck(JsonObject Condition, string? Actual, bool Passed);
