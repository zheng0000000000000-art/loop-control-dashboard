// 게이트 클린 outbox 반입 건만 상위 티어 AI가 diff를 검토해 자동 승인하는 좁은 이양 경로.
// 코어/기준 파일 변경, 위반 증가, 일일 캡 초과, halted 상태는 모두 사람 결재 대기로 되돌린다. proposal 자체의 승인/거절은 다루지 않는다.
using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

public sealed class Tier2Approver
{
    private static readonly string[] CoreFiles = ["server/Engine.cs", "server/Storage.cs", "server/Guardrails.cs"];
    private static readonly string[] BaselineFileNames = ["workflow-definition.json", "blueprint.json"];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string workspaceRoot;
    private readonly string auditLogPath;
    private readonly string stateFilePath;

    public Tier2ApproverOptions Options { get; }

    // 감사 로그·상태 파일 경로를 준비한다.
    public Tier2Approver(string workspaceRoot, Tier2ApproverOptions options)
    {
        this.workspaceRoot = workspaceRoot;
        Options = options;
        var auditDirectory = Path.Combine(workspaceRoot, "docs", "audit");
        Directory.CreateDirectory(auditDirectory);
        auditLogPath = Path.Combine(auditDirectory, "tier2-import-approvals.md");
        stateFilePath = Path.Combine(auditDirectory, "tier2-import-approvals-state.json");
    }

    // outbox 항목이 자동 반입 대상인지 평가하고, 대상이면 검토·반입·감사기록까지 수행한다.
    public JsonObject MaybeAutoApprove(OutboxManager outbox, string taskId, string taskDirectory, JsonObject meta)
        => MaybeAutoApprove(outbox, taskId, taskDirectory, meta, null, null);

    // 테스트에서 실제 Ollama 호출 대신 결정을 주입할 수 있게 하는 오버로드.
    public JsonObject MaybeAutoApprove(OutboxManager outbox, string taskId, string taskDirectory, JsonObject meta, Func<JsonObject, string, ReviewOutcome>? reviewOverride)
        => MaybeAutoApprove(outbox, taskId, taskDirectory, meta, reviewOverride, null);

    // 테스트에서 반입 후 재측정(dotnet 서브프로세스)까지 주입할 수 있게 하는 전체 오버로드.
    public JsonObject MaybeAutoApprove(OutboxManager outbox, string taskId, string taskDirectory, JsonObject meta, Func<JsonObject, string, ReviewOutcome>? reviewOverride, Func<int>? postImportViolationCountOverride)
    {
        var tier2 = new JsonObject { ["attempted"] = Options.Enabled };
        meta["tier2"] = tier2;

        if (!Options.Enabled)
        {
            tier2["decision"] = "skipped";
            tier2["reason"] = "tier2_disabled";
            return meta;
        }

        var state = LoadState();

        if (state.Halted)
        {
            tier2["decision"] = "blocked_halted";
            tier2["reason"] = state.HaltReason ?? "halted";
            AppendAudit(taskId, meta, "blocked_halted", state.HaltReason ?? "halted");
            return meta;
        }

        var eligibility = CheckEligibility(meta);
        tier2["eligible"] = eligibility.Eligible;
        tier2["eligibilityReasons"] = new JsonArray(eligibility.Reasons.Select(reason => (JsonNode)JsonValue.Create(reason)).ToArray());

        if (!eligibility.Eligible)
        {
            tier2["decision"] = "blocked_ineligible";
            AppendAudit(taskId, meta, "blocked_ineligible", string.Join("; ", eligibility.Reasons));
            return meta;
        }

        var today = DateTimeOffset.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var todayCount = state.DailyCounts.GetValueOrDefault(today, 0);

        if (todayCount >= Options.DailyCap)
        {
            tier2["decision"] = "blocked_daily_cap";
            AppendAudit(taskId, meta, "blocked_daily_cap", $"{todayCount}/{Options.DailyCap}");
            return meta;
        }

        var diffPath = Path.Combine(taskDirectory, "diff.patch");
        var diffText = File.Exists(diffPath) ? File.ReadAllText(diffPath, Encoding.UTF8) : "";
        var review = (reviewOverride ?? RequestReview)(meta, diffText);
        tier2["reviewer"] = new JsonObject
        {
            ["provider"] = Options.Provider,
            ["model"] = review.Model,
            ["verdict"] = review.Approved ? "approve" : "reject",
            ["reason"] = review.Reason,
        };

        if (!review.Approved)
        {
            tier2["decision"] = "reviewed_not_approved";
            AppendAudit(taskId, meta, "reviewed_not_approved", review.Reason);
            return meta;
        }

        try
        {
            var imported = outbox.ApplyAutoImport(taskId, taskDirectory, meta);
            imported["tier2"] = tier2;
            var afterImportViolations = postImportViolationCountOverride is not null
                ? postImportViolationCountOverride()
                : OutboxManager.ParseViolationCount(OutboxManager.RunMeasureAsync(workspaceRoot, noBuild: true).GetAwaiter().GetResult().Stdout);

            if (afterImportViolations > eligibility.ViolationsBefore)
            {
                Halt(state, $"post-import violations {eligibility.ViolationsBefore} -> {afterImportViolations} (task {taskId})");
                tier2["decision"] = "anomaly_halted";
                tier2["postImportViolations"] = afterImportViolations;
                AppendAudit(taskId, imported, "anomaly_halted", $"violations {eligibility.ViolationsBefore} -> {afterImportViolations}");
                return imported;
            }

            state.DailyCounts[today] = todayCount + 1;
            SaveState(state);
            tier2["decision"] = "approved";
            AppendAudit(taskId, imported, "approved", review.Reason);
            return imported;
        }
        catch (Exception error)
        {
            Halt(state, $"apply exception for task {taskId}: {error.Message}");
            tier2["decision"] = "anomaly_halted";
            tier2["reason"] = error.Message;
            AppendAudit(taskId, meta, "anomaly_halted", error.Message);
            return meta;
        }
    }

    // 코어/기준 파일 무수정과 위반 비증가를 확인한다.
    public static EligibilityResult CheckEligibility(JsonObject meta)
    {
        var reasons = new List<string>();
        var changed = (meta["changedFiles"]?.AsArray() ?? new JsonArray()).Select(node => node?.GetValue<string>() ?? "").ToList();
        var deleted = (meta["deletedFiles"]?.AsArray() ?? new JsonArray()).Select(node => node?.GetValue<string>() ?? "").ToList();
        var touched = changed.Concat(deleted).ToList();

        var coreHit = touched.Where(path => CoreFiles.Contains(path, StringComparer.OrdinalIgnoreCase)).ToList();
        if (coreHit.Count > 0)
        {
            reasons.Add($"core files touched: {string.Join(", ", coreHit)}");
        }

        var baselineHit = touched.Where(path => BaselineFileNames.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)).ToList();
        if (baselineHit.Count > 0)
        {
            reasons.Add($"baseline files touched: {string.Join(", ", baselineHit)}");
        }

        var before = meta["gateViolationsBefore"]?.GetValue<int>() ?? int.MaxValue;
        var after = meta["gateViolationsAfter"]?.GetValue<int>() ?? int.MaxValue;

        if (before == int.MaxValue || after == int.MaxValue)
        {
            reasons.Add($"violation count unavailable (measurement failed or unparseable): before={before}, after={after}");
        }
        else if (after > before)
        {
            reasons.Add($"violations increased: {before} -> {after}");
        }

        return new EligibilityResult(reasons.Count == 0, reasons, before, after);
    }

    // 이 시스템은 시뮬레이션 기반 선택(simtune/BalanceTuner류) 기능을 갖고 있다. 해당 도메인 팩에
    // 이 기능이 구현돼 있으면, 반입 전 이 프로그램으로 예상 결과를 판단한다.
    // 로컬 Ollama 모델에게 diff 검토를 요청한다. 실패하면 폴백 모델을 한 번 더 시도한다.
    private ReviewOutcome RequestReview(JsonObject meta, string diffText)
    {
        try
        {
            return RequestReviewWithModel(Options.Model, meta, diffText);
        }
        catch
        {
            // 기본 모델 실패는 폴백으로 넘어간다 — 아래에서 처리.
        }

        if (!string.IsNullOrWhiteSpace(Options.FallbackModel))
        {
            try
            {
                return RequestReviewWithModel(Options.FallbackModel!, meta, diffText);
            }
            catch
            {
                // 폴백도 실패하면 아래 unavailable로 떨어진다.
            }
        }

        return new ReviewOutcome(false, "reviewer_unavailable", null);
    }

    // 지정한 모델로 실제 검토 요청을 보낸다.
    private ReviewOutcome RequestReviewWithModel(string model, JsonObject meta, string diffText)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(Options.TimeoutSeconds) };
        var truncatedDiff = diffText.Length > 6000 ? diffText[..6000] + "\n...(truncated)" : diffText;
        var request = new JsonObject
        {
            ["model"] = model,
            ["prompt"] = BuildReviewPrompt(meta, truncatedDiff),
            ["stream"] = false,
            ["format"] = "json",
        };

        var response = client.PostAsJsonAsync($"{Options.Endpoint.TrimEnd('/')}/api/generate", request).GetAwaiter().GetResult();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"ollama http {(int)response.StatusCode}");
        }

        var body = JsonNode.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult())!.AsObject();
        var responseText = body["response"]?.GetValue<string>() ?? "";
        var verdict = JsonNode.Parse(ExtractJson(responseText))!.AsObject();
        var approved = string.Equals(verdict["verdict"]?.GetValue<string>(), "approve", StringComparison.OrdinalIgnoreCase);
        var reason = verdict["reason"]?.GetValue<string>() ?? "";
        return new ReviewOutcome(approved, reason, model);
    }

    // 모델 응답 텍스트에서 첫 JSON 객체만 추출한다.
    private static string ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');

        if (start < 0 || end < start)
        {
            throw new InvalidOperationException("no JSON object in reviewer response");
        }

        return text[start..(end + 1)];
    }

    // 검토 프롬프트를 만든다.
    private static string BuildReviewPrompt(JsonObject meta, string diffText)
    {
        var changedFiles = string.Join(", ", (meta["changedFiles"]?.AsArray() ?? new JsonArray()).Select(node => node?.GetValue<string>()));
        return $$"""
        You are a strict code-import reviewer for a local automation system.
        This diff already passed mechanical checks: quality-gate violations did not increase ({{meta["gateViolationsBefore"]}} -> {{meta["gateViolationsAfter"]}}), and it does not touch core engine files or baseline configuration files.
        Your job is to catch anything the mechanical checks cannot: secrets, unrelated scope creep, obviously broken logic, or anything that looks unsafe to import unattended.
        Respond with ONLY a JSON object: {"verdict": "approve" or "reject", "reason": "one sentence"}.

        Task instruction: {{meta["instruction"]}}
        Changed files: {{changedFiles}}

        Diff:
        {{diffText}}
        """;
    }

    // 상태 파일(halted, 일일 카운트)을 읽는다.
    private Tier2State LoadState()
    {
        if (!File.Exists(stateFilePath))
        {
            return new Tier2State(false, null, new Dictionary<string, int>());
        }

        var node = JsonNode.Parse(File.ReadAllText(stateFilePath, Encoding.UTF8))!.AsObject();
        var halted = node["halted"]?.GetValue<bool>() ?? false;
        var reason = node["haltReason"]?.GetValue<string>();
        var counts = new Dictionary<string, int>();

        if (node["dailyCounts"] is JsonObject countsObject)
        {
            foreach (var pair in countsObject)
            {
                counts[pair.Key] = pair.Value?.GetValue<int>() ?? 0;
            }
        }

        return new Tier2State(halted, reason, counts);
    }

    // 상태 파일을 기록한다.
    private void SaveState(Tier2State state)
    {
        var node = new JsonObject
        {
            ["halted"] = state.Halted,
            ["haltReason"] = state.HaltReason,
            ["dailyCounts"] = new JsonObject(state.DailyCounts.Select(pair => new KeyValuePair<string, JsonNode?>(pair.Key, JsonValue.Create(pair.Value)))),
        };
        File.WriteAllText(stateFilePath, node.ToJsonString(JsonOptions), Encoding.UTF8);
    }

    // 이상 감지 시 자동 정지 상태로 전환한다 — 사람이 상태 파일을 고쳐야 재개된다.
    private void Halt(Tier2State state, string reason)
    {
        state.Halted = true;
        state.HaltReason = reason;
        SaveState(state);
    }

    // 사람이 읽는 감사 로그(docs/audit/tier2-import-approvals.md)에 한 줄을 남긴다.
    private void AppendAudit(string taskId, JsonObject meta, string decision, string reason)
    {
        var changed = (meta["changedFiles"]?.AsArray() ?? new JsonArray()).Count;
        var deleted = (meta["deletedFiles"]?.AsArray() ?? new JsonArray()).Count;
        var line = $"| {DateTimeOffset.Now:O} | {taskId} | {meta["projectId"]} | {decision} | {meta["gateViolationsBefore"]}->{meta["gateViolationsAfter"]} | {changed}+{deleted} | {EscapeMarkdown(reason)} |{Environment.NewLine}";

        if (!File.Exists(auditLogPath))
        {
            File.WriteAllText(
                auditLogPath,
                "# Tier-2 AI 반입 승인 감사 로그" + Environment.NewLine + Environment.NewLine +
                "게이트 클린(위반 비증가) + 코어/기준 파일 무수정 반입만 상위 티어 AI가 검토·승인한다. 그 외 반입은 사람 결재로 남는다." + Environment.NewLine + Environment.NewLine +
                "| 시각 | taskId | project | 결정 | 위반(전->후) | 변경+삭제 | 사유 |" + Environment.NewLine +
                "| --- | --- | --- | --- | --- | --- | --- |" + Environment.NewLine,
                Encoding.UTF8);
        }

        File.AppendAllText(auditLogPath, line, Encoding.UTF8);
    }

    // 마크다운 테이블 셀에 안전하게 넣을 수 있도록 이스케이프한다.
    private static string EscapeMarkdown(string text)
    {
        return (text ?? "").Replace("|", "\\|").Replace("\n", " ").Replace("\r", "");
    }
}

public sealed record Tier2ApproverOptions(bool Enabled, int DailyCap, string Provider, string Model, string? FallbackModel, string Endpoint, int TimeoutSeconds)
{
    // server/appsettings.json의 Tier2Approver 섹션을 직접 읽는다(Program.cs 설정 배선을 늘리지 않기 위함).
    public static Tier2ApproverOptions Load(string workspaceRoot)
    {
        try
        {
            var path = Path.Combine(workspaceRoot, "server", "appsettings.json");
            var root = JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8))!.AsObject();
            var section = root["Tier2Approver"]?.AsObject();

            if (section is null)
            {
                return Default();
            }

            return new Tier2ApproverOptions(
                section["Enabled"]?.GetValue<bool>() ?? false,
                section["DailyCap"]?.GetValue<int>() ?? 5,
                section["Provider"]?.GetValue<string>() ?? "ollama",
                section["Model"]?.GetValue<string>() ?? "qwen3:14b",
                section["FallbackModel"]?.GetValue<string>(),
                section["Endpoint"]?.GetValue<string>() ?? "http://127.0.0.1:11434",
                section["TimeoutSeconds"]?.GetValue<int>() ?? 60);
        }
        catch
        {
            return Default();
        }
    }

    // 설정 파일이 없거나 읽기에 실패했을 때 쓰는 기본값(꺼짐).
    private static Tier2ApproverOptions Default() => new(false, 5, "ollama", "qwen3:14b", "llama3.1:8b", "http://127.0.0.1:11434", 60);
}

public sealed record EligibilityResult(bool Eligible, List<string> Reasons, int ViolationsBefore, int ViolationsAfter);

public sealed record ReviewOutcome(bool Approved, string Reason, string? Model);

public sealed class Tier2State
{
    public bool Halted { get; set; }
    public string? HaltReason { get; set; }
    public Dictionary<string, int> DailyCounts { get; }

    // 상태 값을 초기화한다.
    public Tier2State(bool halted, string? haltReason, Dictionary<string, int> dailyCounts)
    {
        Halted = halted;
        HaltReason = haltReason;
        DailyCounts = dailyCounts;
    }
}
