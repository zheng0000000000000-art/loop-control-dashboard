// 상위 AI 결재자 호출과 정책 게이트를 처리한다.
// 결재 결과 JSON을 검증하고 승인·거절·사람 이관 판정을 반환한다.
using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

public static class ApproverTier
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly HashSet<string> Decisions = new(StringComparer.Ordinal)
    {
        "approve",
        "reject",
        "defer_to_human",
    };

    // 정책과 현재 제안 상태를 검사해 결재자 호출 가능 여부를 반환한다.
    public static ApproverEligibility CheckEligibility(JsonObject definition, JsonObject state, JsonObject proposal, Tier1ReviewResult tier1, string riskAssessed)
    {
        var policy = definition["reviewPolicy"]?.AsObject()["approverTier"] as JsonObject;
        var proposalId = proposal["id"]?.GetValue<string>() ?? "";

        if (policy is null || policy["enabled"]?.GetValue<bool>() != true)
        {
            return ApproverEligibility.Blocked(proposalId, "approver.disabled", "상위 AI 결재 정책이 꺼져 있다.");
        }

        var provider = policy["provider"]?.GetValue<string>() ?? "";
        if (string.IsNullOrWhiteSpace(provider))
        {
            return ApproverEligibility.Blocked(proposalId, "approver.provider_missing", "상위 AI 결재 provider가 없다.");
        }

        if (state["mode"]?.GetValue<string>() == "degraded")
        {
            return ApproverEligibility.Blocked(proposalId, "approver.pacing_degraded", "보존 구간이라 상위 AI 결재를 사람 경로로 강등했다.");
        }

        if (!RiskAllowed(riskAssessed, policy["maxRisk"]?.GetValue<string>() ?? "low"))
        {
            return ApproverEligibility.Blocked(proposalId, "approver.risk_too_high", "산정 위험도가 상위 AI 결재 한도를 넘었다.");
        }

        if (CountAutoApprovalsInLoop(state, proposal) >= Math.Max(0, Number(policy["maxAutoApprovalsPerLoop"], 0)))
        {
            return ApproverEligibility.Blocked(proposalId, "approver.loop_limit", "이번 회차의 자동 결재 횟수 한도를 넘었다.");
        }

        var required = policy["requires"]?.AsArray().Select(node => node?.GetValue<string>() ?? "").ToHashSet(StringComparer.Ordinal) ?? [];
        if (required.Contains("tier1_passed") && tier1.Verdict != "approved")
        {
            return ApproverEligibility.Blocked(proposalId, "approver.tier1_not_passed", "1층 검토가 승인 판정이 아니다.");
        }

        if (required.Contains("no_suspended_tracks") && state["suspendedTracks"]?.AsArray().Count > 0)
        {
            return ApproverEligibility.Blocked(proposalId, "approver.suspended_tracks", "보류 트랙이 있어 사람 결재가 필요하다.");
        }

        if (required.Contains("not_meta_change") && IsMetaChange(proposal))
        {
            return ApproverEligibility.Blocked(proposalId, "approver.meta_change", "기준·레버·blueprint·스킬·반입 변경은 사람 전용이다.");
        }

        if (HasIdentityConflict(proposal["createdBy"] as JsonObject, provider, null))
        {
            return ApproverEligibility.Blocked(proposalId, "approver.identity_generator", "결재자와 제안 생성자가 같다.");
        }

        if (HasIdentityConflict(tier1.Report?["reviewer"] as JsonObject, provider, null))
        {
            return ApproverEligibility.Blocked(proposalId, "approver.identity_tier1", "결재자와 1층 확인자가 같다.");
        }

        return new ApproverEligibility(true, proposalId, provider, policy, null, null);
    }

    // 결재자를 호출하고 실패 시 사람 이관 판정을 반환한다.
    public static ApproverDecision Review(JsonObject definition, JsonObject proposal, JsonObject measurement, JsonObject? tier1Report, JsonObject blueprint, JsonArray recentHumanReviews)
    {
        var timer = Stopwatch.StartNew();
        var policy = definition["reviewPolicy"]?.AsObject()["approverTier"]?.AsObject() ?? new JsonObject();
        var provider = policy["provider"]?.GetValue<string>() ?? "claude-code";
        var proposalId = proposal["id"]?.GetValue<string>() ?? "";

        try
        {
            var raw = CallApprover(policy, BuildPrompt(proposal, measurement, tier1Report, blueprint, recentHumanReviews));
            var parsed = ParseDecision(raw);
            timer.Stop();

            if (parsed is null)
            {
                return Deferred(proposalId, provider, timer.ElapsedMilliseconds, "approver.invalid_json", "결재자 응답 JSON 스키마 검증에 실패했다.");
            }

            return new ApproverDecision(parsed.Decision, parsed.Reason, proposalId, provider, timer.ElapsedMilliseconds, null);
        }
        catch (Exception error) when (error is TimeoutException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            timer.Stop();
            return Deferred(proposalId, provider, timer.ElapsedMilliseconds, "approver.call_failed", error.Message);
        }
    }

    // 결재자 실행 비용을 만든다.
    public static JsonObject Cost()
    {
        return new JsonObject
        {
            ["inputTokens"] = 0,
            ["outputTokens"] = 0,
            ["estimatedUSD"] = 0,
            ["subscriptionCalls"] = 1,
            ["role"] = "runtime",
        };
    }

    // 정책 mock 또는 claude-code CLI를 통해 결재자 응답을 얻는다.
    private static string CallApprover(JsonObject policy, string prompt)
    {
        if (policy["mockTimeout"]?.GetValue<bool>() == true)
        {
            throw new TimeoutException("mock timeout");
        }

        if (policy["mockDecision"] is JsonObject mockDecision)
        {
            return mockDecision.ToJsonString(JsonOptions);
        }

        var timeoutSeconds = Math.Max(1, Number(policy["timeoutSeconds"], 60));
        using var process = new Process();
        process.StartInfo.FileName = policy["command"]?.GetValue<string>() ?? "claude-code";
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardInput = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.StandardOutputEncoding = Encoding.UTF8;
        process.StartInfo.StandardErrorEncoding = Encoding.UTF8;

        foreach (var argument in policy["args"]?.AsArray().Select(node => node?.GetValue<string>()).Where(value => !string.IsNullOrWhiteSpace(value)) ?? [])
        {
            process.StartInfo.ArgumentList.Add(argument!);
        }

        process.Start();
        process.StandardInput.Write(prompt);
        process.StandardInput.Close();

        if (!process.WaitForExit(timeoutSeconds * 1000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("상위 AI 결재 호출 시간이 초과됐다.");
        }

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"상위 AI 결재 호출 실패: {process.ExitCode} {error}");
        }

        return output;
    }

    // 결재자에게 보낼 구조화 프롬프트를 만든다.
    private static string BuildPrompt(JsonObject proposal, JsonObject measurement, JsonObject? tier1Report, JsonObject blueprint, JsonArray recentHumanReviews)
    {
        var input = new JsonObject
        {
            ["role"] = "tier-2 approver",
            ["allowedDecisions"] = new JsonArray("approve", "reject", "defer_to_human"),
            ["proposal"] = Clone(proposal),
            ["predictedMetrics"] = Clone(proposal["predictedMetrics"] ?? new JsonArray()),
            ["measurement"] = Clone(measurement),
            ["tier1"] = tier1Report is null ? null : Clone(tier1Report),
            ["blueprintExcerpt"] = Clone(BlueprintExcerpt(blueprint)),
            ["recentHumanReviews"] = Clone(recentHumanReviews),
            ["outputSchema"] = new JsonObject
            {
                ["decision"] = "approve|reject|defer_to_human",
                ["reason"] = "한 줄 근거",
            },
        };

        return "아래 입력을 검토하고 JSON 하나만 출력하라.\n" + input.ToJsonString(JsonOptions);
    }

    // 결재자 JSON 응답을 검증한다.
    private static ParsedDecision? ParseDecision(string raw)
    {
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');

        if (start < 0 || end <= start)
        {
            return null;
        }

        try
        {
            var json = JsonNode.Parse(raw[start..(end + 1)])?.AsObject();
            var decision = json?["decision"]?.GetValue<string>() ?? "";
            var reason = json?["reason"]?.GetValue<string>() ?? "";

            if (!Decisions.Contains(decision) || string.IsNullOrWhiteSpace(reason))
            {
                return null;
            }

            return new ParsedDecision(decision, reason);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // 사람 이관 결재 결과를 만든다.
    private static ApproverDecision Deferred(string proposalId, string provider, long durationMs, string reasonCode, string reason)
    {
        return new ApproverDecision("defer_to_human", reason, proposalId, provider, durationMs, reasonCode);
    }

    // 위험도가 정책 한도 안인지 확인한다.
    private static bool RiskAllowed(string risk, string maxRisk)
    {
        var order = new[] { "low", "medium", "high" };
        var riskIndex = Array.IndexOf(order, risk);
        var maxIndex = Array.IndexOf(order, maxRisk);
        return riskIndex >= 0 && maxIndex >= 0 && riskIndex <= maxIndex;
    }

    // 같은 루프에서 이미 기록된 자동 결재 횟수를 센다.
    private static int CountAutoApprovalsInLoop(JsonObject state, JsonObject proposal)
    {
        var currentLoop = Number(state["loopIteration"], 0);
        return proposal["autoApprovalLoop"]?.GetValue<int>() == currentLoop
            ? Number(proposal["autoApprovalCount"], 0)
            : 0;
    }

    // 사람 전용 메타 변경인지 확인한다.
    private static bool IsMetaChange(JsonObject proposal)
    {
        if (proposal["kind"]?.GetValue<string>() is "blueprint_suggestion" or "skill_draft" or "import")
        {
            return true;
        }

        foreach (var change in proposal["changes"]?.AsArray().OfType<JsonObject>() ?? [])
        {
            var path = change["path"]?.GetValue<string>() ?? "";
            if (path.Contains("blueprint", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("workflow-definition", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("reviewPolicy", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("tunableLevers", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("skills/", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("outbox/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // 두 행위자 신원이 같은지 확인한다.
    private static bool HasIdentityConflict(JsonObject? actor, string provider, string? model)
    {
        if (actor is null)
        {
            return false;
        }

        var actorProvider = actor["provider"]?.GetValue<string>() ?? "";
        var actorModel = actor["model"]?.GetValue<string>();
        return string.Equals(actorProvider, provider, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(model) || string.Equals(actorModel, model, StringComparison.OrdinalIgnoreCase));
    }

    // 결재 프롬프트에 넣을 blueprint 일부를 만든다.
    private static JsonArray BlueprintExcerpt(JsonObject blueprint)
    {
        return new JsonArray((blueprint["items"]?.AsArray() ?? new JsonArray())
            .Take(8)
            .Select(item => item is null ? null : Clone(item))
            .ToArray());
    }

    // JSON 노드를 깊은 복사한다.
    private static JsonNode Clone(JsonNode node)
    {
        return JsonNode.Parse(node.ToJsonString(JsonOptions))!;
    }

    // 노드에서 정수를 읽는다.
    private static int Number(JsonNode? node, int fallback)
    {
        return node is not null && int.TryParse(node.ToString(), out var value) ? value : fallback;
    }
}

public sealed record ApproverEligibility(bool Allowed, string ProposalId, string Provider, JsonObject? Policy, string? ReasonCode, string? Reason)
{
    // 차단된 결재 가능성 결과를 만든다.
    public static ApproverEligibility Blocked(string proposalId, string reasonCode, string reason)
    {
        return new ApproverEligibility(false, proposalId, "", null, reasonCode, reason);
    }
}

public sealed record ApproverDecision(string Decision, string Reason, string ProposalId, string Provider, long DurationMs, string? ReasonCode);

public sealed record ParsedDecision(string Decision, string Reason);
