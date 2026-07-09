// 가드레일과 체크포인트 조건을 판정한다.
// 상태 변경과 로그 추가만 처리한다.
using System.Globalization;
using System.Text.Json.Nodes;

public static class Guardrails
{
    // 가드레일과 체크포인트를 상태에 반영한다.
    public static GuardrailResult Enforce(JsonObject definition, JsonObject state, JsonObject runLog)
    {
        var guardrailEvaluation = EvaluateGuardrails(definition, state, runLog);

        if (guardrailEvaluation.Breaches.Count > 0)
        {
            return HaltForGuardrail(definition, state, runLog, guardrailEvaluation);
        }

        var checkpointEvaluation = EvaluateCheckpoints(definition, state);

        if (checkpointEvaluation.Triggered && checkpointEvaluation.Checkpoint is not null)
        {
            return PauseForCheckpoint(definition, state, runLog, checkpointEvaluation);
        }

        return new GuardrailResult(state, runLog, false);
    }

    // 반복 횟수와 비용 한도 초과 여부를 평가한다.
    public static GuardrailEvaluation EvaluateGuardrails(JsonObject definition, JsonObject state, JsonObject runLog)
    {
        var guardrails = definition["guardrails"]?.AsObject() ?? new JsonObject();
        var maxLoopIterations = Number(guardrails["maxLoopIterations"], int.MaxValue);
        var maxEstimatedCost = DecimalNumber(guardrails["maxEstimatedCost"], decimal.MaxValue);
        var maxSubscriptionCalls = Number(guardrails["maxSubscriptionCalls"], int.MaxValue);
        var loopIteration = Engine.GetLoopIteration(state);
        var totalCost = Engine.SumEstimatedCost(runLog);
        var subscriptionCalls = Engine.SumSubscriptionCalls(runLog);
        var breaches = new List<JsonObject>();

        if (loopIteration >= maxLoopIterations && !IsGuardrailAcknowledged(state, "loopIteration", loopIteration))
        {
            breaches.Add(new JsonObject { ["type"] = "loopIteration", ["actual"] = loopIteration, ["limit"] = maxLoopIterations });
        }

        if (totalCost >= maxEstimatedCost && !IsGuardrailAcknowledged(state, "estimatedCost", loopIteration))
        {
            breaches.Add(new JsonObject { ["type"] = "estimatedCost", ["actual"] = totalCost, ["limit"] = maxEstimatedCost });
        }

        if (subscriptionCalls >= maxSubscriptionCalls && !IsGuardrailAcknowledged(state, "subscriptionCalls", loopIteration))
        {
            breaches.Add(new JsonObject { ["type"] = "subscriptionCalls", ["actual"] = subscriptionCalls, ["limit"] = maxSubscriptionCalls });
        }

        return new GuardrailEvaluation(breaches, totalCost, subscriptionCalls);
    }

    // 체크포인트 발동 여부를 평가한다.
    public static CheckpointEvaluation EvaluateCheckpoints(JsonObject definition, JsonObject state)
    {
        var checkpoints = definition["checkpoints"]?.AsArray() ?? new JsonArray();
        var loopIteration = Engine.GetLoopIteration(state);

        foreach (var checkpointNode in checkpoints.OfType<JsonObject>())
        {
            if (checkpointNode["on"]?.GetValue<string>() == "loopIteration" && Number(checkpointNode["every"], 0) > 0)
            {
                var checkpointId = checkpointNode["id"]?.GetValue<string>() ?? "";
                var every = Number(checkpointNode["every"], 0);

                if (loopIteration > 0 && loopIteration % every == 0 && !IsCheckpointAcknowledged(state, checkpointId, loopIteration))
                {
                    return new CheckpointEvaluation(true, checkpointNode, loopIteration);
                }
            }
        }

        return new CheckpointEvaluation(false, null, loopIteration);
    }

    // 체크포인트 확인 기록을 추가한다.
    public static JsonObject AcknowledgeCheckpoint(JsonObject state, string checkpointId)
    {
        var nextState = Engine.CloneObject(state);
        var loopIteration = Engine.GetLoopIteration(nextState);
        var acknowledged = nextState["acknowledgedCheckpoints"]?.AsArray() ?? new JsonArray();
        nextState["acknowledgedCheckpoints"] = acknowledged;

        if (!IsCheckpointAcknowledged(nextState, checkpointId, loopIteration))
        {
            acknowledged.Add(new JsonObject
            {
                ["checkpointId"] = checkpointId,
                ["loopIteration"] = loopIteration,
                ["acknowledgedAt"] = DateTimeOffset.Now.ToString("O"),
            });
        }

        nextState["loopState"] = "running";
        nextState.Remove("pausedAt");
        nextState.Remove("pausedBy");
        nextState.Remove("checkpointId");
        nextState["lastUpdated"] = DateTimeOffset.Now.ToString("O");
        return nextState;
    }

    // 가드레일 확인 상태를 적용한다.
    public static JsonObject AcknowledgeGuardrail(JsonObject state, string id)
    {
        var nextState = Engine.CloneObject(state);
        var loopIteration = Engine.GetLoopIteration(nextState);
        var breachTypes = nextState["haltedBy"]?.AsObject()["breaches"]?.AsArray()
            .OfType<JsonObject>()
            .Select(breach => breach["type"]?.GetValue<string>())
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Distinct()
            .ToList() ?? [];

        if (breachTypes.Count == 0)
        {
            breachTypes.Add(id);
        }

        var acknowledged = nextState["acknowledgedGuardrails"]?.AsArray() ?? new JsonArray();
        nextState["acknowledgedGuardrails"] = acknowledged;

        foreach (var breachType in breachTypes)
        {
            if (!IsGuardrailAcknowledged(nextState, breachType!, loopIteration))
            {
                acknowledged.Add(new JsonObject
                {
                    ["id"] = id,
                    ["type"] = breachType,
                    ["loopIteration"] = loopIteration,
                    ["acknowledgedAt"] = DateTimeOffset.Now.ToString("O"),
                });
            }
        }

        nextState["loopState"] = "running";
        nextState.Remove("haltedAt");
        nextState.Remove("haltedBy");
        nextState["lastUpdated"] = DateTimeOffset.Now.ToString("O");
        return nextState;
    }

    // 가드레일 초과 상태를 만든다.
    private static GuardrailResult HaltForGuardrail(JsonObject definition, JsonObject state, JsonObject runLog, GuardrailEvaluation evaluation)
    {
        var nextState = Engine.CloneObject(state);
        var alreadyHalted = nextState["loopState"]?.GetValue<string>() == "halted" &&
            nextState["haltedBy"]?.AsObject()["type"]?.GetValue<string>() == "guardrail";

        nextState["loopState"] = "halted";
        nextState["haltedAt"] = DateTimeOffset.Now.ToString("O");
        nextState["haltedBy"] = new JsonObject
        {
            ["type"] = "guardrail",
            ["breaches"] = new JsonArray(evaluation.Breaches.Select(breach => Engine.CloneNode(breach)).ToArray()),
        };
        nextState.Remove("pausedAt");
        nextState.Remove("pausedBy");
        nextState["overallStatus"] = Engine.ComputeOverallStatus(definition, nextState);
        nextState["lastUpdated"] = DateTimeOffset.Now.ToString("O");

        if (alreadyHalted)
        {
            return new GuardrailResult(nextState, runLog, false);
        }

        var nextRunLog = Engine.AppendLog(runLog, new JsonObject
        {
            ["event"] = "guardrail.halted",
            ["params"] = new JsonObject
            {
                ["text"] = string.Join(", ", evaluation.Breaches.Select(breach => $"{breach["type"]} {breach["actual"]} >= {breach["limit"]}")),
            },
            ["level"] = "warning",
            ["producedBy"] = new JsonObject { ["provider"] = "guardrails", ["model"] = null },
            ["cost"] = RuntimeCost(),
        }, Engine.GetLoopIteration(nextState));
        return new GuardrailResult(nextState, nextRunLog, true);
    }

    // 체크포인트 일시정지 상태를 만든다.
    private static GuardrailResult PauseForCheckpoint(JsonObject definition, JsonObject state, JsonObject runLog, CheckpointEvaluation evaluation)
    {
        var nextState = Engine.CloneObject(state);
        var checkpointId = evaluation.Checkpoint!["id"]?.GetValue<string>() ?? "";
        var alreadyPaused = nextState["loopState"]?.GetValue<string>() == "paused" &&
            nextState["pausedBy"]?.AsObject()["type"]?.GetValue<string>() == "checkpoint" &&
            nextState["pausedBy"]?.AsObject()["checkpointId"]?.GetValue<string>() == checkpointId;

        nextState["loopState"] = "paused";
        nextState["pausedAt"] = DateTimeOffset.Now.ToString("O");
        nextState["pausedBy"] = new JsonObject { ["type"] = "checkpoint", ["checkpointId"] = checkpointId };
        nextState.Remove("haltedAt");
        nextState.Remove("haltedBy");
        nextState["checkpointId"] = checkpointId;
        nextState["overallStatus"] = Engine.ComputeOverallStatus(definition, nextState);
        nextState["lastUpdated"] = DateTimeOffset.Now.ToString("O");

        if (alreadyPaused)
        {
            return new GuardrailResult(nextState, runLog, false);
        }

        var nextRunLog = Engine.AppendLog(runLog, new JsonObject
        {
            ["event"] = "checkpoint.paused",
            ["params"] = new JsonObject { ["checkpointId"] = checkpointId, ["loopIteration"] = evaluation.LoopIteration },
            ["level"] = "warning",
            ["producedBy"] = new JsonObject { ["provider"] = "checkpoints", ["model"] = null },
            ["cost"] = RuntimeCost(),
        }, Engine.GetLoopIteration(nextState));
        return new GuardrailResult(nextState, nextRunLog, true);
    }

    // 체크포인트 확인 기록 존재 여부를 확인한다.
    private static bool IsCheckpointAcknowledged(JsonObject state, string checkpointId, int loopIteration)
    {
        return (state["acknowledgedCheckpoints"]?.AsArray() ?? new JsonArray())
            .OfType<JsonObject>()
            .Any(item =>
                item["checkpointId"]?.GetValue<string>() == checkpointId &&
                Number(item["loopIteration"], -1) == loopIteration);
    }

    // 가드레일 확인 기록 존재 여부를 확인한다.
    private static bool IsGuardrailAcknowledged(JsonObject state, string breachType, int loopIteration)
    {
        return (state["acknowledgedGuardrails"]?.AsArray() ?? new JsonArray())
            .OfType<JsonObject>()
            .Any(item =>
                item["type"]?.GetValue<string>() == breachType &&
                Number(item["loopIteration"], -1) == loopIteration);
    }

    // 런타임 비용 0 객체를 만든다.
    private static JsonObject RuntimeCost()
    {
        return new JsonObject
        {
            ["inputTokens"] = 0,
            ["outputTokens"] = 0,
            ["estimatedUSD"] = 0,
            ["subscriptionCalls"] = 0,
            ["role"] = "runtime",
        };
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

public sealed record GuardrailEvaluation(List<JsonObject> Breaches, decimal TotalCost, int SubscriptionCalls);

public sealed record CheckpointEvaluation(bool Triggered, JsonObject? Checkpoint, int LoopIteration);

public sealed record GuardrailResult(JsonObject State, JsonObject RunLog, bool Changed);
