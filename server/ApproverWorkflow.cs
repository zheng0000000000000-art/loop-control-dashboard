// 1층 통과 제안을 상위 AI 결재자에게 연결한다.
// 결재 결과를 검토 이력과 실행 로그에 남기고 승인 공통 경로를 호출한다.
using System.Text.Json;
using System.Text.Json.Nodes;

public delegate IResult ApplyApprovalDecisionHandler(
    Storage storage,
    string projectId,
    ProjectBundle bundle,
    JsonObject reviewStage,
    JsonObject report,
    string provider,
    JsonObject cost,
    JsonSerializerOptions jsonOptions,
    NtfyOptions ntfy,
    out ProjectBundle committedBundle);

public static class ApproverWorkflow
{
    // 상위 AI 결재자에게 넘기고 자동 적용 여부를 반환한다.
    public static bool TryRun(ProjectBundle bundle, string projectId, NtfyOptions ntfy, Storage storage, JsonSerializerOptions jsonOptions, Tier1ReviewResult tier1, ApplyApprovalDecisionHandler applyApproval)
    {
        var risk = AssessRisk(bundle.Definition, bundle.Proposal);
        var eligibility = ApproverTier.CheckEligibility(bundle.Definition, bundle.State, bundle.Proposal, tier1, risk);

        if (!eligibility.Allowed)
        {
            bundle.RunLog = Engine.AppendLog(bundle.RunLog, RoutedLog(eligibility.ProposalId, eligibility.ReasonCode ?? "approver.blocked", eligibility.Reason ?? ""), Engine.GetLoopIteration(bundle.State));
            return false;
        }

        var decision = ApproverTier.Review(bundle.Definition, bundle.Proposal, bundle.Measurement, tier1.Report, bundle.Blueprint, RecentHumanReviews(bundle.Reviews));
        bundle.RunLog = Engine.AppendLog(bundle.RunLog, CompletedLog(decision), Engine.GetLoopIteration(bundle.State));

        if (decision.Decision == "approve")
        {
            return ApplyApprovedDecision(bundle, projectId, ntfy, storage, jsonOptions, applyApproval, risk, decision);
        }

        if (decision.Decision == "reject")
        {
            AppendReport(bundle.Reviews, ReviewReport(bundle.Proposal, "rejected", decision.Reason, risk, decision.Provider));
            bundle.RunLog = Engine.AppendLog(bundle.RunLog, RejectedLog(decision), Engine.GetLoopIteration(bundle.State));
        }

        return false;
    }

    // 승인 판정이면 공통 승인 경로를 호출한다.
    private static bool ApplyApprovedDecision(ProjectBundle bundle, string projectId, NtfyOptions ntfy, Storage storage, JsonSerializerOptions jsonOptions, ApplyApprovalDecisionHandler applyApproval, string risk, ApproverDecision decision)
    {
        var reviewStage = Engine.GetHumanReviewStage(bundle.Definition, bundle.State);
        if (reviewStage is null)
        {
            bundle.RunLog = Engine.AppendLog(bundle.RunLog, RoutedLog(decision.ProposalId, "approver.no_review_stage", "검토 단계가 없어 사람 경로로 남겼다."), Engine.GetLoopIteration(bundle.State));
            return false;
        }

        MarkAutoApprovalCount(bundle.State, bundle.Proposal);
        var report = ReviewReport(bundle.Proposal, "approved", decision.Reason, risk, decision.Provider);
        applyApproval(storage, projectId, bundle, reviewStage, report, decision.Provider, ApproverTier.Cost(), jsonOptions, ntfy, out var committedBundle);
        bundle.State = committedBundle.State;
        bundle.RunLog = committedBundle.RunLog;
        bundle.Proposal = committedBundle.Proposal;
        bundle.Reviews = committedBundle.Reviews;
        bundle.Measurement = committedBundle.Measurement;
        return true;
    }

    // 검토 리포트를 목록에 추가한다.
    private static void AppendReport(JsonObject reviews, JsonObject report)
    {
        var reports = reviews["reports"]?.AsArray() ?? new JsonArray();
        reviews["schemaVersion"] = 2;
        reviews["reports"] = reports;
        reports.Add(report);
    }

    // AI 결재자 검토 리포트를 만든다.
    private static JsonObject ReviewReport(JsonObject proposal, string verdict, string reason, string risk, string provider)
    {
        return new JsonObject
        {
            ["id"] = $"review-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            ["proposalId"] = proposal["id"]?.GetValue<string>() ?? "",
            ["verdict"] = verdict,
            ["reviewer"] = new JsonObject { ["type"] = "ai", ["provider"] = provider, ["role"] = "approver", ["model"] = null },
            ["riskAssessed"] = risk,
            ["findings"] = new JsonArray(),
            ["reason"] = reason,
            ["createdAt"] = DateTimeOffset.Now.ToString("O"),
        };
    }

    // 상위 AI 결재 라우팅 로그를 만든다.
    private static JsonObject RoutedLog(string proposalId, string reasonCode, string text)
    {
        return new JsonObject
        {
            ["event"] = "review.routed",
            ["params"] = new JsonObject { ["proposalId"] = proposalId, ["reasonCode"] = reasonCode, ["text"] = text },
            ["level"] = "warning",
            ["producedBy"] = new JsonObject { ["provider"] = "rule-engine", ["model"] = null },
            ["cost"] = RuntimeCost(),
        };
    }

    // 상위 AI 결재 완료 로그를 만든다.
    private static JsonObject CompletedLog(ApproverDecision decision)
    {
        var parameters = new JsonObject
        {
            ["proposalId"] = decision.ProposalId,
            ["decision"] = decision.Decision,
            ["durationMs"] = decision.DurationMs,
            ["text"] = decision.Reason,
        };

        if (!string.IsNullOrWhiteSpace(decision.ReasonCode))
        {
            parameters["reasonCode"] = decision.ReasonCode;
        }

        return new JsonObject
        {
            ["event"] = "review.approver_completed",
            ["params"] = parameters,
            ["level"] = decision.Decision == "approve" ? "info" : "warning",
            ["producedBy"] = new JsonObject { ["provider"] = decision.Provider, ["model"] = null },
            ["cost"] = ApproverTier.Cost(),
        };
    }

    // AI 거절 로그를 만든다.
    private static JsonObject RejectedLog(ApproverDecision decision)
    {
        return new JsonObject
        {
            ["event"] = "review.rejected",
            ["params"] = new JsonObject { ["proposalId"] = decision.ProposalId, ["text"] = decision.Reason },
            ["level"] = "warning",
            ["producedBy"] = new JsonObject { ["provider"] = decision.Provider, ["model"] = null },
            ["cost"] = ApproverTier.Cost(),
        };
    }

    // 현재 루프의 자동 결재 횟수 메타를 제안에 남긴다.
    private static void MarkAutoApprovalCount(JsonObject state, JsonObject proposal)
    {
        var loop = Engine.GetLoopIteration(state);
        var currentLoop = Number(proposal["autoApprovalLoop"], -1);
        var count = currentLoop == loop ? Number(proposal["autoApprovalCount"], 0) : 0;
        proposal["autoApprovalLoop"] = loop;
        proposal["autoApprovalCount"] = count + 1;
    }

    // 최근 사람 결재 이력 일부를 반환한다.
    private static JsonArray RecentHumanReviews(JsonObject reviews)
    {
        return new JsonArray((reviews["reports"]?.AsArray() ?? new JsonArray())
            .OfType<JsonObject>()
            .Where(report => report["reviewer"]?.AsObject()["type"]?.GetValue<string>() == "human")
            .OrderByDescending(report => report["createdAt"]?.GetValue<string>(), StringComparer.Ordinal)
            .Take(3)
            .Select(report => Engine.CloneNode(report))
            .ToArray());
    }

    // 구조화된 변경 항목으로 위험도를 산정한다.
    private static string AssessRisk(JsonObject definition, JsonObject proposal)
    {
        var metrics = ProposalMetrics(proposal);
        var rules = definition["reviewPolicy"]?.AsObject()["riskRules"]?.AsArray() ?? new JsonArray();
        var assessed = "high";

        foreach (var rule in rules.OfType<JsonObject>())
        {
            if (rule["if"] is not null && EvaluateRiskExpression(rule["if"]!.GetValue<string>(), metrics))
            {
                return NormalizeRisk(rule["then"]?.GetValue<string>()) ?? "high";
            }

            if (rule["default"] is not null)
            {
                assessed = NormalizeRisk(rule["default"]?.GetValue<string>()) ?? "high";
            }
        }

        return assessed;
    }

    // 제안 변경 수와 최대 증감률을 계산한다.
    private static Dictionary<string, decimal> ProposalMetrics(JsonObject proposal)
    {
        var changes = proposal["changes"]?.AsArray() ?? new JsonArray();
        var maxDelta = 0m;

        foreach (var change in changes.OfType<JsonObject>())
        {
            if (decimal.TryParse(change["before"]?.ToString(), out var before) &&
                decimal.TryParse(change["after"]?.ToString(), out var after))
            {
                var delta = before == 0 ? (after == 0 ? 0 : 100) : Math.Abs((after - before) / before) * 100;
                maxDelta = Math.Max(maxDelta, delta);
            }
        }

        return new Dictionary<string, decimal> { ["changeCount"] = changes.Count, ["maxValueDeltaPercent"] = maxDelta };
    }

    // 위험도 규칙 표현식을 평가한다.
    private static bool EvaluateRiskExpression(string expression, Dictionary<string, decimal> metrics)
    {
        return expression.Split("&&", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).All(part =>
        {
            var match = System.Text.RegularExpressions.Regex.Match(part, @"^(changeCount|maxValueDeltaPercent)\s*(<=|>=|<|>|===|==)\s*(-?\d+(?:\.\d+)?)$");
            if (!match.Success)
            {
                return false;
            }

            var left = metrics[match.Groups[1].Value];
            var right = decimal.Parse(match.Groups[3].Value);
            return match.Groups[2].Value switch
            {
                "<=" => left <= right,
                ">=" => left >= right,
                "<" => left < right,
                ">" => left > right,
                _ => left == right,
            };
        });
    }

    // 위험도 값을 허용 enum으로 정규화한다.
    private static string? NormalizeRisk(string? value)
    {
        var normalized = value?.ToLowerInvariant();
        return normalized is "low" or "medium" or "high" ? normalized : null;
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

    // 노드에서 정수를 읽는다.
    private static int Number(JsonNode? node, int fallback)
    {
        return node is not null && int.TryParse(node.ToString(), out var value) ? value : fallback;
    }
}
