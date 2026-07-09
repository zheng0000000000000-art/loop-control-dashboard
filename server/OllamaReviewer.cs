// 로컬 Ollama 모델로 1층 체크리스트 검토를 수행한다.
// 검토 결과 리포트와 실행 로그 항목을 생성한다.
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

public static class OllamaReviewer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // definition 정책에 따라 제안을 검토하고 결과를 반환한다.
    public static Tier1ReviewResult Review(JsonObject definition, JsonObject proposal, JsonObject measurement, string riskAssessed)
    {
        var timer = Stopwatch.StartNew();
        var policy = definition["reviewerPolicy"]?.AsObject()["tier1"] as JsonObject;
        var checklist = definition["reviewChecklist"]?.AsArray() ?? new JsonArray();
        var proposalId = proposal["id"]?.GetValue<string>() ?? "";

        if (policy is null || checklist.Count == 0)
        {
            return Skipped(proposalId, timer, "review.no_tier1_policy", "1층 검토 정책 또는 체크리스트가 없다.");
        }

        var provider = policy["provider"]?.GetValue<string>() ?? "";
        var model = policy["model"]?.GetValue<string>() ?? "";
        var fallbackModel = policy["fallbackModel"]?.GetValue<string>();

        if (HasIdentityConflict(proposal, provider, model))
        {
            return Skipped(proposalId, timer, "review.identity_conflict", "제안 생성자와 검토자 신원이 같아 1층 검토를 건너뛰었다.", provider, model);
        }

        var primary = TryReviewWithModel(policy, proposal, measurement, checklist, model);
        var attempt = primary;

        if (primary.Unavailable && !string.IsNullOrWhiteSpace(fallbackModel))
        {
            attempt = TryReviewWithModel(policy, proposal, measurement, checklist, fallbackModel!);
        }

        if (attempt.Unavailable)
        {
            return Skipped(proposalId, timer, "system.reviewer_unavailable", attempt.Error ?? "1층 검토자를 사용할 수 없다.", provider, attempt.Model);
        }

        var verdict = DecideVerdict(attempt.Findings);
        var reason = VerdictReason(verdict);
        var report = new JsonObject
        {
            ["id"] = $"review-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            ["proposalId"] = proposalId,
            ["verdict"] = verdict,
            ["reviewer"] = new JsonObject { ["type"] = "ai", ["provider"] = provider, ["model"] = attempt.Model },
            ["riskAssessed"] = riskAssessed,
            ["findings"] = CloneArray(attempt.Findings),
            ["reason"] = reason,
            ["createdAt"] = DateTimeOffset.Now.ToString("O"),
        };

        timer.Stop();
        return new Tier1ReviewResult(report, LogEntry(proposalId, verdict, provider, attempt.Model, timer.ElapsedMilliseconds, null, attempt.Findings.Count, CountUncertain(attempt.Findings), attempt.TotalAttempts), verdict, null);
    }

    // 단일 모델로 체크리스트 전체를 실행한다.
    private static ModelReviewAttempt TryReviewWithModel(JsonObject policy, JsonObject proposal, JsonObject measurement, JsonArray checklist, string model)
    {
        var findings = new JsonArray();
        var maxRetries = Math.Max(1, Number(policy["maxRetries"], 1));
        var changes = proposal["changes"]?.AsArray() ?? new JsonArray();
        var totalAttempts = 0;

        foreach (var check in checklist.OfType<JsonObject>())
        {
            foreach (var change in changes.OfType<JsonObject>())
            {
                var result = RunSingleCheck(policy, model, check, change, measurement, maxRetries);
                totalAttempts += result.Attempts;

                if (result.Unavailable)
                {
                    return new ModelReviewAttempt(model, true, findings, totalAttempts, result.Error);
                }

                findings.Add(result.Finding);
            }
        }

        return new ModelReviewAttempt(model, false, findings, totalAttempts, null);
    }

    // 하나의 체크리스트 항목과 변경 항목을 검토한다.
    private static SingleCheckResult RunSingleCheck(JsonObject policy, string model, JsonObject check, JsonObject change, JsonObject measurement, int maxRetries)
    {
        var checkId = check["id"]?.GetValue<string>() ?? "unknown-check";
        var expected = check["expect"]?.GetValue<bool>() ?? true;
        var onFail = check["onFail"]?.GetValue<string>() ?? "needs_changes";
        var target = change["path"]?.GetValue<string>() ?? "change";
        string? lastError = null;

        for (var attempt = 1; attempt <= maxRetries; attempt += 1)
        {
            try
            {
                var raw = CallModel(policy, model, BuildPrompt(check, change, measurement));
                var parsed = ParseChecklistResponse(raw, checkId);

                if (parsed is not null)
                {
                    var uncertain = parsed.Confidence != "high";
                    var passed = !uncertain && parsed.Answer == expected;
                    return new SingleCheckResult(false, null, attempt, Finding(target, checkId, parsed.Answer, expected, parsed.Confidence, parsed.Note, onFail, passed, uncertain, attempt, model));
                }

                lastError = "JSON 응답 파싱 실패";
            }
            catch (ReviewerUnavailableException error)
            {
                return new SingleCheckResult(true, error.Message, attempt, Finding(target, checkId, null, expected, "low", error.Message, onFail, false, true, attempt, model));
            }
            catch (Exception error)
            {
                lastError = error.Message;
            }
        }

        return new SingleCheckResult(false, null, maxRetries, Finding(target, checkId, null, expected, "low", lastError ?? "응답을 해석할 수 없다.", onFail, false, true, maxRetries, model));
    }

    // Ollama generate API를 호출한다.
    private static string CallModel(JsonObject policy, string model, string prompt)
    {
        var endpoint = (policy["endpoint"]?.GetValue<string>() ?? "http://localhost:11434").TrimEnd('/');
        var timeoutSeconds = Math.Max(1, Number(policy["timeoutSeconds"], 60));
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
        var payload = new JsonObject
        {
            ["model"] = model,
            ["prompt"] = prompt,
            ["stream"] = false,
            ["format"] = "json",
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}/api/generate")
        {
            Content = new StringContent(payload.ToJsonString(JsonOptions), Encoding.UTF8, "application/json"),
        };

        try
        {
            using var response = client.Send(request);
            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            if (response.StatusCode == HttpStatusCode.NotFound || !response.IsSuccessStatusCode)
            {
                throw new ReviewerUnavailableException($"Ollama 응답 실패: {(int)response.StatusCode} {body}");
            }

            var json = JsonNode.Parse(body)?.AsObject();
            return json?["response"]?.GetValue<string>() ?? body;
        }
        catch (TaskCanceledException error)
        {
            throw new ReviewerUnavailableException($"Ollama 호출 시간 초과: {error.Message}");
        }
        catch (HttpRequestException error)
        {
            throw new ReviewerUnavailableException($"Ollama 연결 실패: {error.Message}");
        }
    }

    // 모델에 전달할 검토 프롬프트를 만든다.
    private static string BuildPrompt(JsonObject check, JsonObject change, JsonObject measurement)
    {
        var checkId = check["id"]?.GetValue<string>() ?? "unknown-check";
        var ask = check["ask"]?.GetValue<string>() ?? "";
        var changeSummary = new JsonObject
        {
            ["path"] = change["path"]?.DeepClone(),
            ["before"] = change["before"]?.DeepClone(),
            ["after"] = change["after"]?.DeepClone(),
            ["note"] = change["note"]?.DeepClone(),
        };

        return "너는 변경 제안 검토자다. 아래 질문에 JSON으로만 답하라.\n" +
            $"질문: {ask}\n" +
            $"제안: {changeSummary.ToJsonString(JsonOptions)}\n" +
            $"측정 요약: {measurement.ToJsonString(JsonOptions)}\n" +
            $"답 형식: {{\"checkId\":\"{checkId}\",\"answer\":true|false,\"confidence\":\"high|low\",\"note\":\"한 줄 근거\"}}";
    }

    // 모델 응답에서 체크리스트 JSON을 추출한다.
    public static ChecklistAnswer? ParseChecklistResponse(string raw, string expectedCheckId)
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
            var checkId = json?["checkId"]?.GetValue<string>();
            var confidence = json?["confidence"]?.GetValue<string>()?.ToLowerInvariant();

            if (checkId != expectedCheckId || json?["answer"] is not JsonValue answerValue || confidence is not ("high" or "low"))
            {
                return null;
            }

            return answerValue.TryGetValue<bool>(out var answer)
                ? new ChecklistAnswer(checkId, answer, confidence, json?["note"]?.GetValue<string>() ?? "")
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // 제안 생성자와 검토자 신원이 같은지 확인한다.
    private static bool HasIdentityConflict(JsonObject proposal, string provider, string model)
    {
        var createdBy = proposal["createdBy"] as JsonObject;
        var sourceProvider = createdBy?["provider"]?.GetValue<string>() ?? "";
        var sourceModel = createdBy?["model"]?.GetValue<string>() ?? "";
        return sourceProvider == provider && sourceModel == model;
    }

    // 체크리스트 결과 목록으로 최종 판정을 계산한다.
    private static string DecideVerdict(JsonArray findings)
    {
        var hasUncertain = false;
        var hasNeedsChanges = false;

        foreach (var finding in findings.OfType<JsonObject>())
        {
            if (finding["uncertain"]?.GetValue<bool>() == true)
            {
                hasUncertain = true;
            }

            if (finding["passed"]?.GetValue<bool>() == false && finding["onFail"]?.GetValue<string>() == "escalate")
            {
                hasUncertain = true;
            }

            if (finding["passed"]?.GetValue<bool>() == false && finding["onFail"]?.GetValue<string>() == "needs_changes")
            {
                hasNeedsChanges = true;
            }
        }

        if (hasUncertain)
        {
            return "uncertain";
        }

        return hasNeedsChanges ? "needs_changes" : "approved";
    }

    // 최종 판정의 사유 문장을 만든다.
    private static string VerdictReason(string verdict)
    {
        return verdict switch
        {
            "approved" => "1층 체크리스트를 통과했다. 사람 최종 결재가 필요하다.",
            "needs_changes" => "1층 체크리스트에서 수정 필요 항목이 발견됐다.",
            _ => "1층 체크리스트에서 불확실하거나 상위 검토가 필요한 항목이 발견됐다.",
        };
    }

    // 검토를 건너뛴 결과를 만든다.
    private static Tier1ReviewResult Skipped(string proposalId, Stopwatch timer, string reasonCode, string reason, string provider = "local-server", string? model = null)
    {
        timer.Stop();
        return new Tier1ReviewResult(null, LogEntry(proposalId, "uncertain", provider, model, timer.ElapsedMilliseconds, reasonCode, 0, 1, 0, reason), "uncertain", reasonCode);
    }

    // 실행 로그 항목을 만든다.
    private static JsonObject LogEntry(string proposalId, string verdict, string provider, string? model, long durationMs, string? reasonCode, int checkCount, int uncertainCount, int attempts, string? text = null)
    {
        var parameters = new JsonObject
        {
            ["proposalId"] = proposalId,
            ["verdict"] = verdict,
            ["model"] = model,
            ["durationMs"] = durationMs,
            ["checkCount"] = checkCount,
            ["uncertainCount"] = uncertainCount,
            ["attempts"] = attempts,
            ["text"] = text ?? "",
        };

        if (!string.IsNullOrWhiteSpace(reasonCode))
        {
            parameters["reasonCode"] = reasonCode;
        }

        return new JsonObject
        {
            ["event"] = "review.tier1_completed",
            ["params"] = parameters,
            ["level"] = reasonCode is null && verdict == "approved" ? "info" : "warning",
            ["producedBy"] = new JsonObject { ["provider"] = provider, ["model"] = model },
            ["cost"] = RuntimeCost(),
        };
    }

    // findings 배열을 복사한다.
    private static JsonArray CloneArray(JsonArray findings)
    {
        return new JsonArray(findings.Where(item => item is not null).Select(item => JsonNode.Parse(item!.ToJsonString())!).ToArray());
    }

    // 불확실 finding 수를 센다.
    private static int CountUncertain(JsonArray findings)
    {
        return findings.OfType<JsonObject>().Count(finding => finding["uncertain"]?.GetValue<bool>() == true);
    }

    // finding 객체를 만든다.
    private static JsonObject Finding(string target, string checkId, bool? answer, bool expected, string confidence, string note, string onFail, bool passed, bool uncertain, int attempts, string model)
    {
        return new JsonObject
        {
            ["target"] = target,
            ["checkId"] = checkId,
            ["answer"] = answer,
            ["expected"] = expected,
            ["confidence"] = confidence,
            ["note"] = note,
            ["comment"] = note,
            ["onFail"] = onFail,
            ["passed"] = passed,
            ["uncertain"] = uncertain,
            ["attempts"] = attempts,
            ["model"] = model,
            ["severity"] = uncertain ? "concern" : passed ? "info" : onFail == "needs_changes" ? "concern" : "blocker",
        };
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
        return node is not null && int.TryParse(node.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    }
}

public sealed record Tier1ReviewResult(JsonObject? Report, JsonObject LogEntry, string Verdict, string? ReasonCode);

public sealed record ChecklistAnswer(string CheckId, bool Answer, string Confidence, string Note);

public sealed record ModelReviewAttempt(string Model, bool Unavailable, JsonArray Findings, int TotalAttempts, string? Error);

public sealed record SingleCheckResult(bool Unavailable, string? Error, int Attempts, JsonObject Finding);

public sealed class ReviewerUnavailableException(string message) : Exception(message);
