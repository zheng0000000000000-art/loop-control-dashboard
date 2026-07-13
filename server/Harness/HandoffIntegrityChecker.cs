// WORKSTATE ↔ applier-log 불일치를 탐지하는 내부 reconciliation 검사기.
// CLI 표면 미노출 — HarnessRegistry 미등록. StateApplierCli in-process 호출 전용.
using System.Text.Json;
using System.Text.Json.Nodes;

// in-process 호출 옵션. PendingTransitionId가 있으면 state-only 1회 면제를 적용한다.
internal record ReconciliationOptions(
    string WorkstatePath,
    string ApplierLogPath,
    string? PendingTransitionId = null
);

// 개별 log 항목 파싱 결과.
internal record LogEntry(
    string TransitionId,
    string Result,
    int ExitCode,
    string At,
    int SchemaVersion,
    string? TransitionContractSha256
);

// lookupSuccess 반환값 — 06C-1 idempotency 판정용.
internal record LogLookupResult(bool Exists, int SchemaVersion, string? TransitionContractSha256);

// reconciliation 검사 단일 항목.
internal record ReconciliationEntry(string Subject, string Code, string Message);

// reconciliation 지표 — JSON 직렬화용.
internal record ReconciliationMetrics(
    int AppliedTransitionCount,
    int SuccessfulLogEntryCount,
    int SuccessfulLogIdCount,
    int DuplicateSuccessLogCount,
    Dictionary<string, int> LogSchemaVersions,
    bool PendingExemptionApplied,
    string Reconciliation
);

internal class ReconciliationResult
{
    public List<ReconciliationEntry> Failures { get; } = [];
    public List<ReconciliationEntry> Warnings { get; } = [];
    public List<ReconciliationEntry> HarnessErrors { get; } = [];
    public ReconciliationMetrics? Metrics { get; set; }
    // 06C-1 idempotency 판정용 lookup. 성공 log에서만 반환.
    public Dictionary<string, LogLookupResult> SuccessLookup { get; } = [];
}

internal static class HandoffIntegrityChecker
{
    private static readonly HashSet<string> AllowedTransitionKinds =
        new(StringComparer.OrdinalIgnoreCase) { "NORMAL", "RECOVERY", "PHASE_CHANGE", "REPLAY" };

    // WORKSTATE appliedTransitions ↔ applier-log.jsonl 불일치를 검사한다.
    internal static ReconciliationResult Run(ReconciliationOptions opts)
    {
        var result = new ReconciliationResult();

        var (workstate, wsErr) = LoadWorkstate(opts.WorkstatePath);
        if (wsErr != null) { result.HarnessErrors.Add(wsErr); return result; }

        var (stateIds, transErr) = ParseAppliedTransitions(workstate!);
        if (transErr != null) { result.HarnessErrors.Add(transErr); return result; }

        var (logEntries, logErr) = ParseApplierLog(opts.ApplierLogPath);
        if (logErr != null) { result.HarnessErrors.Add(logErr); return result; }

        var sets = BuildSets(stateIds!, logEntries!);
        CheckDuplicatesInState(stateIds!, result);
        CheckDuplicateSuccessLog(sets.successCounts, result);
        CheckLogToState(sets.successfulLogIdSet, sets.stateIdSet, result);
        var pendingApplied = CheckStateToLog(sets.stateIdSet, sets.successfulLogIdSet, stateIds!, opts.PendingTransitionId, result);
        BuildLookup(sets.successfulLogIdSet, sets.successCounts, result);
        result.Metrics = BuildMetrics(stateIds!, logEntries!, sets, pendingApplied, result);
        return result;
    }

    // WORKSTATE.json을 파싱한다. 실패 시 HarnessError 반환.
    private static (JsonObject? ws, ReconciliationEntry? err) LoadWorkstate(string path)
    {
        try
        {
            var ws = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            return ws is null
                ? (null, H("workstate", "workstate-malformed", "WORKSTATE is not a JSON object"))
                : (ws, null);
        }
        catch (Exception ex)
        {
            return (null, H("workstate", "workstate-malformed", ex.Message));
        }
    }

    // appliedTransitions 배열을 파싱해 id 목록을 반환한다.
    private static (List<string>? ids, ReconciliationEntry? err) ParseAppliedTransitions(JsonObject ws)
    {
        if (ws["appliedTransitions"] is not JsonArray arr)
            return (null, H("workstate.appliedTransitions", "workstate-malformed",
                "appliedTransitions is missing or not an array"));

        var ids = new List<string>();
        foreach (var node in arr)
        {
            if (node is not JsonObject elem)
                return (null, H("workstate.appliedTransitions", "workstate-malformed",
                    "appliedTransitions element is not an object"));

            var id = elem["id"]?.GetValue<string>() ?? "";
            if (string.IsNullOrWhiteSpace(id))
                return (null, H("workstate.appliedTransitions", "workstate-malformed",
                    "appliedTransitions element has missing or empty id"));

            var appliedAt = elem["appliedAt"]?.GetValue<string>() ?? "";
            if (!IsValidUtcRfc3339(appliedAt))
                return (null, H($"workstate.appliedTransitions[{id}].appliedAt",
                    "workstate-malformed",
                    $"appliedAt is missing or not a valid UTC RFC3339 timestamp: '{appliedAt}'"));

            ids.Add(id);
        }
        return (ids, null);
    }

    // applier-log.jsonl을 파싱해 LogEntry 목록을 반환한다.
    private static (List<LogEntry>? entries, ReconciliationEntry? err) ParseApplierLog(string path)
    {
        if (!File.Exists(path))
            return (null, H("applier-log", "applier-log-malformed", $"applier-log not found: {path}"));

        var entries = new List<LogEntry>();
        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            var (entry, err) = ParseLogLine(trimmed);
            if (err != null) return (null, err);
            entries.Add(entry!);
        }
        return (entries, null);
    }

    // log 1줄을 파싱한다. 오류 시 null entry + ReconciliationEntry error 반환.
    private static (LogEntry? entry, ReconciliationEntry? err) ParseLogLine(string trimmed)
    {
        JsonObject row;
        try { row = JsonNode.Parse(trimmed) as JsonObject ?? throw new InvalidOperationException("not object"); }
        catch (Exception ex)
            { return (null, H("applier-log", "applier-log-malformed", $"log line not valid JSON: {ex.Message}")); }

        var tid = row["transitionId"]?.GetValue<string>() ?? "";
        if (string.IsNullOrWhiteSpace(tid))
            return (null, H("applier-log.transitionId", "applier-log-malformed", "transitionId missing or empty"));

        var res = row["result"];
        if (res is null || res.GetValueKind() != JsonValueKind.String)
            return (null, H($"applier-log[{tid}].result", "applier-log-malformed", "result missing or not string"));

        int exitCode;
        try { exitCode = row["exitCode"]?.GetValue<int>() ?? throw new InvalidOperationException("missing"); }
        catch { return (null, H($"applier-log[{tid}].exitCode", "applier-log-malformed", "exitCode missing or not integer")); }

        var at = row["at"]?.GetValue<string>() ?? "";
        if (!IsValidUtcRfc3339(at))
            return (null, H($"applier-log[{tid}].at", "applier-log-malformed", $"at invalid: '{at}'"));

        int sv = 1;
        if (row["schemaVersion"] is { } svNode && int.TryParse(svNode.ToString(), out var p)) sv = p;

        string? contractHash = null;
        if (sv >= 2)
        {
            var err = ValidateV2Fields(row, tid);
            if (err != null) return (null, H($"applier-log[{tid}]", "applier-log-malformed", err));
            contractHash = row["transitionContractSha256"]?.GetValue<string>();
        }

        return (new LogEntry(tid, res.GetValue<string>(), exitCode, at, sv, contractHash), null);
    }

    // 집합 구조체를 조립한다.
    private static (HashSet<string> stateIdSet, HashSet<string> successfulLogIdSet,
        HashSet<string> allLogIdSet, Dictionary<string, List<LogEntry>> successCounts)
        BuildSets(List<string> stateIds, List<LogEntry> logEntries)
    {
        var stateIdSet = new HashSet<string>(stateIds, StringComparer.Ordinal);
        var allLogIdSet = new HashSet<string>(logEntries.Select(e => e.TransitionId), StringComparer.Ordinal);
        var successfulEntries = logEntries
            .Where(e => string.Equals(e.Result, "ok", StringComparison.OrdinalIgnoreCase) && e.ExitCode == 0);
        var successfulLogIdSet = new HashSet<string>(
            successfulEntries.Select(e => e.TransitionId), StringComparer.Ordinal);
        var successCounts = successfulEntries
            .GroupBy(e => e.TransitionId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        return (stateIdSet, successfulLogIdSet, allLogIdSet, successCounts);
    }

    // state 내 중복 id를 탐지한다.
    private static void CheckDuplicatesInState(List<string> stateIds, ReconciliationResult result)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in stateIds)
            if (!seen.Add(id))
                result.Failures.Add(F(id, "duplicate-in-state",
                    $"transition id '{id}' appears more than once in appliedTransitions"));
    }

    // log 내 중복 성공 항목을 탐지한다. contract hash 충돌은 Failure, 같은 hash는 Warning.
    private static void CheckDuplicateSuccessLog(
        Dictionary<string, List<LogEntry>> successCounts, ReconciliationResult result)
    {
        foreach (var (id, entries) in successCounts.Where(kv => kv.Value.Count > 1))
        {
            var hasContract = entries.All(e => !string.IsNullOrEmpty(e.TransitionContractSha256));
            if (hasContract)
            {
                var distinct = entries.Select(e => e.TransitionContractSha256!)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (distinct.Count > 1)
                    result.Failures.Add(F(id, "duplicate-success-log-conflict",
                        $"id '{id}' has {entries.Count} success entries with differing transitionContractSha256"));
                else
                    result.Warnings.Add(W(id, "duplicate-success-in-log",
                        $"id '{id}' appears {entries.Count} times (same contract — append-only replay)"));
            }
            else
            {
                result.Warnings.Add(W(id, "duplicate-success-in-log",
                    $"id '{id}' appears {entries.Count} times (v1 entries, contract binding unverifiable)"));
            }
        }
    }

    // 규칙 1: successfulLogIdSet ⊆ stateIdSet. log 성공 항목이 state에 없으면 Failure.
    private static void CheckLogToState(
        HashSet<string> successfulLogIdSet, HashSet<string> stateIdSet, ReconciliationResult result)
    {
        foreach (var id in successfulLogIdSet)
            if (!stateIdSet.Contains(id))
                result.Failures.Add(F(id, "log-transition-missing-from-state",
                    $"id '{id}' is in successful log but missing from appliedTransitions (mid-incident state)"));
    }

    // 규칙 2: stateIdSet ⊆ successfulLogIdSet. state 항목이 성공 log에 없으면 Failure. PendingTransitionId 1회 면제.
    private static bool CheckStateToLog(
        HashSet<string> stateIdSet, HashSet<string> successfulLogIdSet,
        List<string> stateIds, string? pendingId, ReconciliationResult result)
    {
        var pendingApplied = false;
        foreach (var id in stateIdSet)
        {
            if (successfulLogIdSet.Contains(id)) continue;
            if (!string.IsNullOrEmpty(pendingId)
                && string.Equals(id, pendingId, StringComparison.Ordinal)
                && stateIds.Count(s => string.Equals(s, id, StringComparison.Ordinal)) == 1)
            {
                pendingApplied = true;
                continue;
            }
            result.Failures.Add(F(id, "state-transition-not-logged",
                $"id '{id}' is in appliedTransitions but has no log entry"));
        }
        return pendingApplied;
    }

    // 06C-1 idempotency 판정을 위한 lookupSuccess 사전을 채운다.
    private static void BuildLookup(
        HashSet<string> successfulLogIdSet,
        Dictionary<string, List<LogEntry>> successCounts,
        ReconciliationResult result)
    {
        foreach (var id in successfulLogIdSet)
        {
            var best = successCounts.GetValueOrDefault(id)?.FirstOrDefault();
            if (best is not null)
                result.SuccessLookup[id] = new LogLookupResult(true, best.SchemaVersion, best.TransitionContractSha256);
        }
    }

    // 지표 객체를 조립한다.
    private static ReconciliationMetrics BuildMetrics(
        List<string> stateIds, List<LogEntry> logEntries,
        (HashSet<string> stateIdSet, HashSet<string> successfulLogIdSet,
         HashSet<string> allLogIdSet, Dictionary<string, List<LogEntry>> successCounts) sets,
        bool pendingApplied, ReconciliationResult result)
    {
        var successfulEntries = logEntries
            .Where(e => string.Equals(e.Result, "ok", StringComparison.OrdinalIgnoreCase) && e.ExitCode == 0)
            .ToList();
        var schemaVersions = logEntries
            .GroupBy(e => $"v{e.SchemaVersion}")
            .ToDictionary(g => g.Key, g => g.Count());
        var dupCount = sets.successCounts.Values.Count(v => v.Count > 1);
        var allClear = result.Failures.Count == 0 && result.HarnessErrors.Count == 0;
        return new ReconciliationMetrics(
            AppliedTransitionCount: stateIds.Count,
            SuccessfulLogEntryCount: successfulEntries.Count,
            SuccessfulLogIdCount: sets.successfulLogIdSet.Count,
            DuplicateSuccessLogCount: dupCount,
            LogSchemaVersions: schemaVersions,
            PendingExemptionApplied: pendingApplied,
            Reconciliation: allClear ? "PASS" : "FAIL"
        );
    }

    // v2 log 항목의 필수 필드를 검증한다. 오류 메시지를 반환하고 정상이면 null.
    private static string? ValidateV2Fields(JsonObject row, string tid)
    {
        var kind = row["transitionKind"]?.GetValue<string>() ?? "";
        if (!AllowedTransitionKinds.Contains(kind))
            return $"transitionKind missing or invalid (got '{kind}')";

        foreach (var field in new[] { "requestSha256", "preStateSha256", "postStateSha256", "transitionContractSha256" })
        {
            var val = row[field]?.GetValue<string>() ?? "";
            if (!Is64Hex(val)) return $"{field} missing or not 64-char hex";
        }

        var effectiveAt = row["effectiveAt"]?.GetValue<string>() ?? "";
        if (!IsValidUtcRfc3339(effectiveAt)) return $"effectiveAt invalid: '{effectiveAt}'";
        return null;
    }

    // 64자 16진수 문자열인지 확인한다.
    private static bool Is64Hex(string? s)
        => s is { Length: 64 } && s.All(c =>
            (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));

    // UTC RFC3339 타임스탬프 형식인지 확인한다.
    private static bool IsValidUtcRfc3339(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        return DateTimeOffset.TryParse(s,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out _);
    }

    // 실패 항목을 만든다.
    private static ReconciliationEntry F(string s, string c, string m) => new(s, c, m);
    // 경고 항목을 만든다.
    private static ReconciliationEntry W(string s, string c, string m) => new(s, c, m);
    // 하네스 오류 항목을 만든다.
    private static ReconciliationEntry H(string s, string c, string m) => new(s, c, m);
}
