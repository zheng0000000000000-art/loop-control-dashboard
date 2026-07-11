// 로컬 Ollama 모델로 blueprint 괴리 해소 제안의 note·title·summary를 생성한다.
// 수치(before/after)는 서버가 채우며, 실패 시 호출자가 rule-engine으로 강등한다.
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

public static class OllamaExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
    private static readonly string[] ProhibitedNoteTerms =
    [
        "\uC218\uC815 \uD544\uC694",
        "\uC218\uC815\uC774 \uD544\uC694",
        "\uAC70\uC808",
        "\uC2B9\uC778",
        "needs_changes",
        "rejected",
        "approved",
        "\uCE21\uC815 \uACB0\uACFC",
        "\uCE21\uC815\uACB0\uACFC",
    ];
    private const string NoteHygieneInstruction =
        "Review findings are instructions to apply, not text to quote. Do not put verdict words such as needs_changes, rejected, approved, or their Korean equivalents in note. " +
        "Describe only the purpose and expected effect of the change. Refer to predicted values as predictions, never as measured results.\n";

    // definition 정책과 위반 목록으로 note·title·summary를 생성하고 토큰 합계를 반환한다.
    public static ExecutorGenerateResult Generate(JsonObject definition, List<MetricCheck> violations, JsonObject? previousReviewReport)
    {
        var timer = Stopwatch.StartNew();
        var policy = definition["executorPolicy"]?.AsObject()["tier1"] as JsonObject;

        if (policy is null || violations.Count == 0)
        {
            return Unavailable(timer, "1층 생성 정책이 없거나 위반 항목이 없다.", fallbackReason: "ollama-disabled");
        }

        var provider = policy["provider"]?.GetValue<string>() ?? "";
        var model = policy["model"]?.GetValue<string>() ?? "";
        var maxRetries = Math.Max(1, Number(policy["maxRetries"], 1));
        var feedback = BuildFeedbackByMetric(previousReviewReport);
        var notes = new Dictionary<string, string>();
        var normalizedMetricIds = new List<(string Expected, string Actual)>();
        var totalIn = 0;
        var totalOut = 0;

        foreach (var violation in violations)
        {
            var (note, noteError, rejectReason, actualMetricId, noteIn, noteOut, normalizedActual) = TryGenerateNote(policy, model, violation, feedback.GetValueOrDefault(violation.MetricId), maxRetries);
            totalIn += noteIn;
            totalOut += noteOut;

            if (note is null)
            {
                return Unavailable(timer, $"{violation.MetricId}: {noteError ?? "note 생성 실패"}", provider, model,
                    fallbackReason: rejectReason, expectedMetricId: violation.MetricId, actualMetricId: actualMetricId);
            }

            if (normalizedActual is not null)
                normalizedMetricIds.Add((violation.MetricId, normalizedActual));

            notes[violation.MetricId] = note;
        }

        var (summary, summaryError, sumIn, sumOut) = TryGenerateSummary(policy, model, violations, notes, maxRetries);
        totalIn += sumIn;
        totalOut += sumOut;

        if (summary is null)
        {
            return Unavailable(timer, summaryError ?? "제목/요약 생성 실패", provider, model);
        }

        var selfReview = RunSelfReviewIfEnabled(definition, policy, model, "blueprint", notes, summary.Value.Title, summary.Value.Summary);
        totalIn += selfReview.InputTokens;
        totalOut += selfReview.OutputTokens;

        if (!selfReview.Passed)
        {
            return Unavailable(timer, selfReview.Error ?? "자가 비평 실패", provider, model, selfReview.Enabled, false);
        }

        timer.Stop();
        return new ExecutorGenerateResult(false, provider, model, notes, summary.Value.Title, summary.Value.Summary, summary.Value.Assumptions, timer.ElapsedMilliseconds, null, selfReview.Enabled, selfReview.Enabled, totalIn, totalOut, NormalizedMetricIds: normalizedMetricIds.Count > 0 ? normalizedMetricIds : null);
    }

    // 이미 결정된 레버 변경들을 서술하는 note·title·summary를 생성하고 토큰 합계를 반환한다.
    public static ExecutorGenerateResult GenerateForTuning(JsonObject definition, TuningResult tuning, JsonObject? previousReviewReport)
    {
        var timer = Stopwatch.StartNew();
        var policy = definition["executorPolicy"]?.AsObject()["tier1"] as JsonObject;

        if (policy is null)
        {
            return Unavailable(timer, "1층 생성 정책이 없다.", fallbackReason: "ollama-disabled");
        }

        var provider = policy["provider"]?.GetValue<string>() ?? "";
        var model = policy["model"]?.GetValue<string>() ?? "";
        var maxRetries = Math.Max(1, Number(policy["maxRetries"], 1));
        var feedback = BuildFeedbackByMetric(previousReviewReport);
        var notes = new Dictionary<string, string>();
        var normalizedMetricIds = new List<(string Expected, string Actual)>();
        var totalIn = 0;
        var totalOut = 0;

        foreach (var change in tuning.ChangedLevers)
        {
            var (note, noteError, rejectReason, actualMetricId, noteIn, noteOut, normalizedActual) = TryGenerateTuningNote(policy, model, change, tuning.PredictedMetrics, feedback.GetValueOrDefault(change.Path), maxRetries);
            totalIn += noteIn;
            totalOut += noteOut;

            if (note is null)
            {
                return Unavailable(timer, $"{change.Path}: {noteError ?? "note 생성 실패"}", provider, model,
                    fallbackReason: rejectReason, expectedMetricId: change.Path, actualMetricId: actualMetricId);
            }

            if (normalizedActual is not null)
                normalizedMetricIds.Add((change.Path, normalizedActual));

            notes[change.Path] = note;
        }

        var (summary, summaryError, sumIn, sumOut) = TryGenerateTuningSummary(policy, model, tuning, maxRetries);
        totalIn += sumIn;
        totalOut += sumOut;

        if (summary is null)
        {
            return Unavailable(timer, summaryError ?? "제목/요약 생성 실패", provider, model);
        }

        var selfReview = RunSelfReviewIfEnabled(definition, policy, model, "tuning", notes, summary.Value.Title, summary.Value.Summary);
        totalIn += selfReview.InputTokens;
        totalOut += selfReview.OutputTokens;

        if (!selfReview.Passed)
        {
            return Unavailable(timer, selfReview.Error ?? "자가 비평 실패", provider, model, selfReview.Enabled, false);
        }

        timer.Stop();
        return new ExecutorGenerateResult(false, provider, model, notes, summary.Value.Title, summary.Value.Summary, summary.Value.Assumptions, timer.ElapsedMilliseconds, null, selfReview.Enabled, selfReview.Enabled, totalIn, totalOut, NormalizedMetricIds: normalizedMetricIds.Count > 0 ? normalizedMetricIds : null);
    }

    // definition의 selfReview가 켜져 있으면 생성 결과를 모델로 한 번 자체 점검하고 토큰 수를 반환한다.
    private static SelfReviewResult RunSelfReviewIfEnabled(JsonObject definition, JsonObject policy, string model, string kind, Dictionary<string, string> notes, string title, string summary)
    {
        if (definition["selfReview"]?.GetValue<bool>() != true)
        {
            return new SelfReviewResult(false, true, null);
        }

        try
        {
            var (raw, inTok, outTok) = CallModel(policy, model, BuildSelfReviewPrompt(kind, notes, title, summary));
            var parsed = ExtractJsonObject(StripThinkBlock(raw));
            var passed = parsed?["passed"]?.GetValue<bool>();
            var note = parsed?["note"]?.GetValue<string>() ?? "";

            if (passed is null)
            {
                return new SelfReviewResult(true, false, "자가 비평 응답 스키마 불일치", inTok, outTok);
            }

            return passed.Value
                ? new SelfReviewResult(true, true, null, inTok, outTok)
                : new SelfReviewResult(true, false, $"자가 비평 실패: {note}", inTok, outTok);
        }
        catch (ReviewerUnavailableException error)
        {
            return new SelfReviewResult(true, false, error.Message);
        }
        catch (Exception error)
        {
            return new SelfReviewResult(true, false, error.Message);
        }
    }

    // 제출 전 자가 비평 프롬프트를 만든다.
    private static string BuildSelfReviewPrompt(string kind, Dictionary<string, string> notes, string title, string summary)
    {
        var notesJson = new JsonObject();
        foreach (var item in notes)
        {
            notesJson[item.Key] = item.Value;
        }

        return "너는 제출 전 제안 문안을 자체 점검한다. JSON으로만 답하라. note에 판정어, 거절/승인 언어, 예측을 측정 결과라고 부르는 표현이 있으면 passed=false다.\n" +
            $"종류: {kind}\n" +
            $"제목: {title}\n" +
            $"요약: {summary}\n" +
            $"notes: {notesJson.ToJsonString(JsonOptions)}\n" +
            "답 형식: {\"passed\":true|false,\"note\":\"한 줄 근거\"}";
    }

    // 레버 변경 하나에 대한 note를 생성하고 누적 토큰 수와 거부 사유를 함께 반환한다.
    // NormalizedActualMetricId가 non-null이면 대소문자 정규화로 통과 — 호출자가 warn 이벤트를 남긴다.
    private static (string? Note, string? Error, string? RejectReason, string? ActualMetricId, int InputTokens, int OutputTokens, string? NormalizedActualMetricId) TryGenerateTuningNote(JsonObject policy, string model, LeverChange change, List<PredictedMetricChange> predicted, string? feedback, int maxRetries)
    {
        string? lastError = null;
        string? lastRejectReason = null;
        string? lastActualMetricId = null;
        var totalIn = 0;
        var totalOut = 0;

        for (var attempt = 1; attempt <= maxRetries; attempt += 1)
        {
            try
            {
                var (raw, inTok, outTok) = CallModel(policy, model, BuildTuningNotePrompt(change, predicted, feedback));
                totalIn += inTok;
                totalOut += outTok;
                var (parsed, reason, actualMetricId) = ParseNoteResponse(raw, change.Path);

                if (parsed is not null)
                {
                    var normalizedActual = reason == "metricid-normalized" ? actualMetricId : null;
                    return (parsed, null, null, null, totalIn, totalOut, normalizedActual);
                }

                lastError = "응답 JSON 스키마 불일치 또는 빈/손상된 note";
                lastRejectReason = reason;
                lastActualMetricId = actualMetricId;
            }
            catch (ReviewerUnavailableException error)
            {
                return (null, error.Message, "ollama-unreachable", null, totalIn, totalOut, null);
            }
            catch (Exception error)
            {
                lastError = error.Message;
                lastRejectReason = "unknown";
            }
        }

        return (null, lastError, lastRejectReason, lastActualMetricId, totalIn, totalOut, null);
    }

    // 레버 변경 note 생성 프롬프트를 만든다.
    private static string BuildTuningNotePrompt(LeverChange change, List<PredictedMetricChange> predicted, string? feedback)
    {
        var predictedText = predicted.Count == 0
            ? "없음"
            : string.Join("; ", predicted.Select(metric => $"{metric.MetricId}: {metric.Before.ToString("0.###", CultureInfo.InvariantCulture)}→{metric.After.ToString("0.###", CultureInfo.InvariantCulture)} (밴드 {metric.Band})"));
        var feedbackLine = string.IsNullOrWhiteSpace(feedback)
            ? ""
            : $"이전 시도가 거절된 이유: {feedback}. 이 지적을 해소하는 note를 작성하라.\n";

        return "너는 자동 밸런스 튜닝 결과를 서술하는 작성자다. 수치(before/after)는 이미 탐색기가 정했으니 절대 바꾸지 말고, " +
            "이 레버 변경이 예측 지표에 어떤 영향을 주는지 한국어 1~2문장 note만 작성하라.\n" +
            $"레버 경로: {change.Path}\n" +
            $"변경: {change.Before.ToString("0.###", CultureInfo.InvariantCulture)} → {change.After.ToString("0.###", CultureInfo.InvariantCulture)}\n" +
            $"예측 지표 변화: {predictedText}\n" +
            NoteHygieneInstruction +
            feedbackLine +
            $"답 형식: {{\"metricId\":\"{change.Path}\",\"note\":\"한두 문장 설명\"}}";
    }

    // 튜닝 제안의 제목과 요약을 생성하고 누적 토큰 수를 함께 반환한다.
    private static ((string Title, string Summary, List<string> Assumptions)? Result, string? Error, int InputTokens, int OutputTokens) TryGenerateTuningSummary(JsonObject policy, string model, TuningResult tuning, int maxRetries)
    {
        string? lastError = null;
        var totalIn = 0;
        var totalOut = 0;

        for (var attempt = 1; attempt <= maxRetries; attempt += 1)
        {
            try
            {
                var (raw, inTok, outTok) = CallModel(policy, model, BuildTuningSummaryPrompt(tuning));
                totalIn += inTok;
                totalOut += outTok;
                var parsed = ParseSummaryResponse(raw);

                if (parsed is not null)
                {
                    return (parsed, null, totalIn, totalOut);
                }

                lastError = "응답 JSON 스키마 불일치 또는 빈/손상된 title·summary";
            }
            catch (ReviewerUnavailableException error)
            {
                return (null, error.Message, totalIn, totalOut);
            }
            catch (Exception error)
            {
                lastError = error.Message;
            }
        }

        return (null, lastError, totalIn, totalOut);
    }

    // 튜닝 제안 제목/요약 생성 프롬프트를 만든다.
    private static string BuildTuningSummaryPrompt(TuningResult tuning)
    {
        var changesText = tuning.ChangedLevers.Count == 0
            ? "없음(레버 범위 안에서 개선되는 후보를 찾지 못함)"
            : string.Join("; ", tuning.ChangedLevers.Select(change => $"{change.Path}: {change.Before.ToString("0.###", CultureInfo.InvariantCulture)}→{change.After.ToString("0.###", CultureInfo.InvariantCulture)}"));
        var residualText = tuning.ResidualViolations.Count == 0 ? "없음" : string.Join("; ", tuning.ResidualViolations);

        return "너는 자동 밸런스 튜닝 결과를 서술하는 작성자다. 아래 탐색 결과를 요약하는 짧은 한국어 제목과 한 문장 요약을 작성하라. " +
            "밴드 도달에 실패했다면 그 사실과 잔여 위반을 요약에 반드시 포함하라.\n" +
            $"레버 변경: {changesText}\n" +
            $"밴드 도달: {(tuning.ReachedBand ? "성공" : "실패")}\n" +
            $"잔여 위반: {residualText}\n" +
            "가정이나 불확실한 지점이 있으면 assumptions 배열에 짧게 쓰고, 없으면 빈 배열로 두라.\n" +
            "답 형식: {\"title\":\"짧은 제목\",\"summary\":\"한 문장 요약\",\"assumptions\":[]}";
    }

    // 이전 검토에서 통과하지 못한 finding을 metricId 기준으로 모은다.
    private static Dictionary<string, string> BuildFeedbackByMetric(JsonObject? previousReviewReport)
    {
        var result = new Dictionary<string, string>();
        var findings = previousReviewReport?["findings"]?.AsArray();

        if (findings is null)
        {
            return result;
        }

        foreach (var finding in findings.OfType<JsonObject>().Where(f => f["passed"]?.GetValue<bool>() == false))
        {
            var target = finding["target"]?.GetValue<string>();
            var checkId = finding["checkId"]?.GetValue<string>() ?? "unknown";
            var note = finding["note"]?.GetValue<string>() ?? finding["comment"]?.GetValue<string>();

            if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(note))
            {
                continue;
            }

            var structuredNote = $"{checkId}: {note}";
            result[target] = result.TryGetValue(target, out var existing) ? $"{existing} / {structuredNote}" : structuredNote;
        }

        return result;
    }

    // 위반 항목 하나에 대한 note를 생성하고 누적 토큰 수와 거부 사유를 함께 반환한다.
    // NormalizedActualMetricId가 non-null이면 대소문자 정규화로 통과 — 호출자가 warn 이벤트를 남긴다.
    private static (string? Note, string? Error, string? RejectReason, string? ActualMetricId, int InputTokens, int OutputTokens, string? NormalizedActualMetricId) TryGenerateNote(JsonObject policy, string model, MetricCheck violation, string? feedback, int maxRetries)
    {
        string? lastError = null;
        string? lastRejectReason = null;
        string? lastActualMetricId = null;
        var totalIn = 0;
        var totalOut = 0;

        for (var attempt = 1; attempt <= maxRetries; attempt += 1)
        {
            try
            {
                var (raw, inTok, outTok) = CallModel(policy, model, BuildNotePrompt(violation, feedback));
                totalIn += inTok;
                totalOut += outTok;
                var (parsed, reason, actualMetricId) = ParseNoteResponse(raw, violation.MetricId);

                if (parsed is not null)
                {
                    var normalizedActual = reason == "metricid-normalized" ? actualMetricId : null;
                    return (parsed, null, null, null, totalIn, totalOut, normalizedActual);
                }

                lastError = "응답 JSON 스키마 불일치 또는 빈/손상된 note";
                lastRejectReason = reason;
                lastActualMetricId = actualMetricId;
            }
            catch (ReviewerUnavailableException error)
            {
                return (null, error.Message, "ollama-unreachable", null, totalIn, totalOut, null);
            }
            catch (Exception error)
            {
                lastError = error.Message;
                lastRejectReason = "unknown";
            }
        }

        return (null, lastError, lastRejectReason, lastActualMetricId, totalIn, totalOut, null);
    }

    // note 생성 프롬프트를 만든다.
    private static string BuildNotePrompt(MetricCheck violation, string? feedback)
    {
        var evidence = violation.Evidence.Count == 0 ? "근거 없음" : string.Join("; ", violation.Evidence.Take(3));
        var feedbackLine = string.IsNullOrWhiteSpace(feedback)
            ? ""
            : $"이전 시도가 거절된 이유: {feedback}. 이 지적을 해소하는 note를 작성하라.\n";

        return "너는 변경 제안 작성자다. 수치(before/after)는 이미 정해져 있으니 절대 바꾸지 말고, " +
            "이 변경이 왜·무엇을 고치는지 근거 위치를 인용해 한국어 1~2문장 note만 작성하라.\n" +
            $"metricId: {violation.MetricId}\n" +
            $"측정값: {ValueText(violation.Value)}\n" +
            $"목표: {violation.Expected}\n" +
            $"근거: {evidence}\n" +
            NoteHygieneInstruction +
            feedbackLine +
            $"답 형식: {{\"metricId\":\"{violation.MetricId}\",\"note\":\"한두 문장 설명\"}}";
    }

    // note 응답 JSON을 파싱한다. 거부 시 reason 코드와 실제 metricId를 함께 반환한다.
    // "metricid-normalized" reason은 대소문자 무시 대조로 통과한 경우 — 호출자가 warn 이벤트를 남긴다.
    private static (string? Note, string Reason, string? ActualMetricId) ParseNoteResponse(string raw, string expectedMetricId)
    {
        var json = ExtractJsonObject(StripThinkBlock(raw));
        if (json is null) return (null, "parse-rejected-json", null);
        var metricId = json["metricId"]?.GetValue<string>();
        var note = json["note"]?.GetValue<string>()?.Trim();

        // 1차: 엄격 대조 — 정상 경로, 이벤트 없음
        if (metricId == expectedMetricId)
        {
            if (string.IsNullOrWhiteSpace(note) || HasReplacementChar(note) || HasProhibitedNoteTerm(note))
                return (null, "parse-rejected-note", metricId);
            return (note, "ok", metricId);
        }

        // 2차: 대소문자 무시 대조만 — 통과 시 수용하되 호출자가 warn 이벤트를 남긴다
        if (string.Equals(metricId, expectedMetricId, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(note) || HasReplacementChar(note) || HasProhibitedNoteTerm(note))
                return (null, "parse-rejected-note", metricId);
            return (note, "metricid-normalized", metricId);
        }

        return (null, "parse-rejected-metricid", metricId);
    }

    // note에 판정어와 예측/사실 혼동어가 섞였는지 확인한다.
    public static bool HasProhibitedNoteTerm(string note)
    {
        return ProhibitedNoteTerms.Any(term => note.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    // 인코딩이 깨진 응답(유니코드 대체 문자 포함)인지 확인한다.
    private static bool HasReplacementChar(string text)
    {
        return text.Contains('�');
    }

    // 제안 제목과 요약을 생성하고 누적 토큰 수를 함께 반환한다.
    private static ((string Title, string Summary, List<string> Assumptions)? Result, string? Error, int InputTokens, int OutputTokens) TryGenerateSummary(JsonObject policy, string model, List<MetricCheck> violations, Dictionary<string, string> notes, int maxRetries)
    {
        string? lastError = null;
        var totalIn = 0;
        var totalOut = 0;

        for (var attempt = 1; attempt <= maxRetries; attempt += 1)
        {
            try
            {
                var (raw, inTok, outTok) = CallModel(policy, model, BuildSummaryPrompt(violations, notes));
                totalIn += inTok;
                totalOut += outTok;
                var parsed = ParseSummaryResponse(raw);

                if (parsed is not null)
                {
                    return (parsed, null, totalIn, totalOut);
                }

                lastError = "응답 JSON 스키마 불일치 또는 빈/손상된 title·summary";
            }
            catch (ReviewerUnavailableException error)
            {
                return (null, error.Message, totalIn, totalOut);
            }
            catch (Exception error)
            {
                lastError = error.Message;
            }
        }

        return (null, lastError, totalIn, totalOut);
    }

    // 제목/요약 생성 프롬프트를 만든다.
    private static string BuildSummaryPrompt(List<MetricCheck> violations, Dictionary<string, string> notes)
    {
        var items = new JsonArray(violations.Select(violation => (JsonNode)new JsonObject
        {
            ["metricId"] = violation.MetricId,
            ["note"] = notes.GetValueOrDefault(violation.MetricId, ""),
        }).ToArray());

        return "너는 변경 제안 작성자다. 아래 변경 항목들을 대표하는 짧은 한국어 제목과 한 문장 요약을 작성하라.\n" +
            $"변경 항목: {items.ToJsonString(JsonOptions)}\n" +
            "가정이나 불확실한 지점이 있으면 assumptions 배열에 짧게 쓰고, 없으면 빈 배열로 두라.\n" +
            "답 형식: {\"title\":\"짧은 제목\",\"summary\":\"한 문장 요약\",\"assumptions\":[]}";
    }

    // 제목/요약 응답 JSON을 파싱한다.
    private static (string Title, string Summary, List<string> Assumptions)? ParseSummaryResponse(string raw)
    {
        var json = ExtractJsonObject(StripThinkBlock(raw));
        var title = json?["title"]?.GetValue<string>()?.Trim();
        var summary = json?["summary"]?.GetValue<string>()?.Trim();
        var assumptions = json?["assumptions"]?.AsArray()
            .Select(item => item?.GetValue<string>()?.Trim() ?? "")
            .Where(item => !string.IsNullOrWhiteSpace(item) && !HasReplacementChar(item))
            .Take(5)
            .ToList() ?? [];
        var valid = !string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(summary) &&
            !HasReplacementChar(title) && !HasReplacementChar(summary);
        return valid ? (title!, summary!, assumptions) : null;
    }

    // 원시 응답 문자열에서 JSON 객체를 추출한다.
    private static JsonObject? ExtractJsonObject(string raw)
    {
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');

        if (start < 0 || end <= start)
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(raw[start..(end + 1)])?.AsObject();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // 모델 응답에서 think 블록을 제거한다(think:false가 적용되지 않는 버전 대비).
    private static string StripThinkBlock(string raw)
    {
        return Regex.Replace(raw, "<think>.*?</think>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
    }

    // Ollama generate API를 호출하고 응답 텍스트와 prompt_eval_count·eval_count를 반환한다.
    private static (string Response, int InputTokens, int OutputTokens) CallModel(JsonObject policy, string model, string prompt)
    {
        var endpoint = (policy["endpoint"]?.GetValue<string>() ?? "http://127.0.0.1:11434").TrimEnd('/');
        var timeoutSeconds = Math.Max(1, Number(policy["timeoutSeconds"], 90));
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
        var payload = new JsonObject
        {
            ["model"] = model,
            ["prompt"] = prompt,
            ["stream"] = false,
            ["format"] = "json",
            ["think"] = false,
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
            var inputTokens = json?["prompt_eval_count"]?.GetValue<int>() ?? 0;
            var outputTokens = json?["eval_count"]?.GetValue<int>() ?? 0;
            return (json?["response"]?.GetValue<string>() ?? body, inputTokens, outputTokens);
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

    // 표시용 값 문자열을 만든다.
    private static string ValueText(JsonNode? node)
    {
        return node is null ? "없음" : node is JsonValue ? node.ToString() : node.ToJsonString();
    }

    // 생성 불가 결과를 만든다. fallbackReason·expectedMetricId·actualMetricId는 폴백 이벤트 기록에 쓰인다.
    private static ExecutorGenerateResult Unavailable(Stopwatch timer, string error, string provider = "rule-engine", string? model = null, bool selfReviewed = false, bool selfReviewPassed = false, string? fallbackReason = null, string? expectedMetricId = null, string? actualMetricId = null)
    {
        timer.Stop();
        return new ExecutorGenerateResult(true, provider, model, new Dictionary<string, string>(), "", "", [], timer.ElapsedMilliseconds, error, selfReviewed, selfReviewPassed, FallbackReason: fallbackReason, ExpectedMetricId: expectedMetricId, ActualMetricId: actualMetricId);
    }

    // 노드에서 정수 값을 읽는다.
    private static int Number(JsonNode? node, int fallback)
    {
        return node is not null && int.TryParse(node.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    }
}

public sealed record SelfReviewResult(bool Enabled, bool Passed, string? Error, int InputTokens = 0, int OutputTokens = 0);

public sealed record ExecutorGenerateResult(bool Unavailable, string Provider, string? Model, Dictionary<string, string> Notes, string Title, string Summary, List<string> Assumptions, long DurationMs, string? Error, bool SelfReviewed, bool SelfReviewPassed, int InputTokens = 0, int OutputTokens = 0, string? FallbackReason = null, string? ExpectedMetricId = null, string? ActualMetricId = null, List<(string Expected, string Actual)>? NormalizedMetricIds = null);
