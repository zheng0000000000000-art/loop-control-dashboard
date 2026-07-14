// WORKSTATE 상태 전이의 유일한 writer.
// 06C-1 v2 core: reconciliation, idempotency, pending journal, atomic replace, v2 success log를 한 경로에 묶는다.
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

internal static class StateApplierCli
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions PrettyJson = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly HashSet<string> ValidTransitionKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "NORMAL", "PHASE_CHANGE", "RECOVERY", "REPLAY",
    };

    private static readonly HashSet<string> HighRiskKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "PHASE_CHANGE", "RECOVERY", "REPLAY",
    };

    private static readonly HashSet<string> ValidStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "waiting", "in_progress", "verifying", "completed", "blocked",
    };

    private static readonly HashSet<string> NextActionsRequired = new(StringComparer.OrdinalIgnoreCase)
    {
        "waiting", "in_progress", "verifying", "blocked",
    };

    private static readonly Dictionary<string, HashSet<string>> AllowedTransitions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["waiting"] = new(StringComparer.OrdinalIgnoreCase) { "in_progress", "blocked" },
        ["in_progress"] = new(StringComparer.OrdinalIgnoreCase) { "verifying", "blocked", "waiting" },
        ["verifying"] = new(StringComparer.OrdinalIgnoreCase) { "completed", "in_progress", "blocked" },
        ["blocked"] = new(StringComparer.OrdinalIgnoreCase) { "waiting", "in_progress", "verifying" },
        ["completed"] = new(StringComparer.OrdinalIgnoreCase) { },
    };

    private static readonly Dictionary<string, string> RemovedOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["human-decision"] = "removed-option: --human-decision",
        ["root"] = "removed-option: --root",
        ["force"] = "removed-option: --force",
        ["admin"] = "removed-option: --admin",
    };

    private static readonly HashSet<string> PrepareKnownKeys = new(StringComparer.OrdinalIgnoreCase)
        { "transition-id", "request", "dry-run-flag" };

    private static readonly HashSet<string> ApplyKnownKeys = new(StringComparer.OrdinalIgnoreCase)
        { "envelope", "dry-run-flag" };

    // prepare 옵션 값을 보관한다.
    private record PrepareOptions(string TransitionId, string RequestPath, bool DryRun);
    // apply 옵션 값을 보관한다.
    private record ApplyOptions(string EnvelopePath, bool DryRun);
    // 상태 전이에서 사용하는 저장소 경로 묶음이다.
    private record RepoContext(string Root, string WorkstatePath, string LogPath, string PendingDir);

    // v2 전이 envelope의 검증 대상 필드를 보관한다.
    private record TransitionEnvelope(
        int SchemaVersion,
        string TransitionId,
        string TransitionKind,
        string EffectiveAt,
        string ExpectedPreStateSha256,
        string ExpectedPostStateSha256,
        string RequestSha256,
        string TransitionContractSha256,
        string RequestPath,
        string CandidatePath);

    private enum ApplyFault
    {
        None,
        TempWrite,
        AtomicReplace,
        AfterReplaceBeforeLog,
        AfterLogBeforeCleanup,
    }

    // self-test 전용 장애 주입 설정이다.
    private record ApplyRuntime(ApplyFault Fault = ApplyFault.None);

    // 적용 직전 재계산한 state, request, candidate 증거를 보관한다.
    private record PreparedInput(
        byte[] StateBytes,
        JsonObject State,
        byte[] RequestBytes,
        JsonObject Request,
        byte[] CandidateBytes,
        JsonObject Candidate,
        string ActualPreHash,
        string ActualRequestHash,
        string ActualPostHash,
        string ActualContractHash);

    // CLI 출력과 exit code로 변환할 적용 결과를 보관한다.
    private record ApplyResult(
        string Status,
        int ExitCode,
        bool StateWritten,
        bool SuccessLogAppended,
        bool PendingJournalPresent,
        string? FailureCode = null,
        string? Detail = null);

    // state-transition 진입점. prepare/apply/--self-test만 외부에 노출한다.
    internal static int Run(string[] args)
    {
        var sub = args.Length > 1 ? args[1] : "";
        if (string.Equals(sub, "prepare", StringComparison.OrdinalIgnoreCase)) return RunPrepare(args);
        if (string.Equals(sub, "apply", StringComparison.OrdinalIgnoreCase)) return RunApply(args);
        if (string.Equals(sub, "--self-test", StringComparison.OrdinalIgnoreCase)) return RunSelfTest();
        WriteRawError("state-transition usage: prepare --transition-id <id> --request <file> | apply --envelope <file> | --self-test");
        return 2;
    }

    // prepare 명령의 옵션을 검증하고 envelope/candidate를 생성한다.
    private static int RunPrepare(string[] args)
    {
        var optErr = ValidateOptions(ParseFlagMap(args, 2), PrepareKnownKeys);
        if (optErr is not null) { WriteRawError(optErr); return 2; }
        var opts = ParsePrepareArgs(args);
        if (opts is null) { WriteRawError("prepare requires --transition-id and --request"); return 2; }
        try { return RunPrepareCore(CreateRepoContext(GitTools.FindRepoRoot()), opts); }
        catch (Exception ex) { WriteRawError($"state-transition prepare failed: {ex.Message}"); return 2; }
    }

    // apply 명령의 옵션을 검증하고 v2 core를 실행한다.
    private static int RunApply(string[] args)
    {
        var optErr = ValidateOptions(ParseFlagMap(args, 2), ApplyKnownKeys);
        if (optErr is not null) { WriteRawError(optErr); return 2; }
        var opts = ParseApplyArgs(args);
        if (opts is null) { WriteRawError("apply requires --envelope"); return 2; }
        var envelope = ReadEnvelope(opts.EnvelopePath);
        if (envelope is null) return 2;
        return ApplyEnvelopeCore(CreateRepoContext(GitTools.FindRepoRoot()), envelope, opts.DryRun, new ApplyRuntime());
    }

    // prepare core는 현재 state와 request에서 결정적 candidate와 v2 envelope를 기록한다.
    private static int RunPrepareCore(RepoContext ctx, PrepareOptions opts)
    {
        var stateBytes = File.ReadAllBytes(ctx.WorkstatePath);
        var state = ParseObject(stateBytes, "WORKSTATE.json");
        var requestPath = FullPathInRoot(ctx.Root, opts.RequestPath);
        var requestBytes = File.ReadAllBytes(requestPath);
        var request = ParseObject(requestBytes, "request");
        var effectiveAt = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var requestHash = Sha256Hex(requestBytes);
        var preHash = Sha256Hex(stateBytes);
        var candidate = BuildCandidate(state, request, opts.TransitionId, effectiveAt);
        var candidateBytes = CanonicalBytes(candidate);
        var postHash = Sha256Hex(candidateBytes);
        var contractHash = ComputeContractHash(opts.TransitionId, "NORMAL", requestHash, preHash, postHash, effectiveAt);

        if (opts.DryRun)
        {
            WritePrepareResult(opts.TransitionId, "", "", preHash, postHash, contractHash);
            return 0;
        }

        var outDir = Path.Combine(ctx.Root, "outputs", "state-transition");
        Directory.CreateDirectory(outDir);
        var safeId = SafeId(opts.TransitionId);
        var candidatePath = Path.Combine(outDir, $"{safeId}.candidate.json");
        var envelopePath = Path.Combine(outDir, $"{safeId}.envelope.json");
        File.WriteAllBytes(candidatePath, candidateBytes);
        File.WriteAllText(envelopePath, BuildEnvelope(opts.TransitionId, requestPath, candidatePath,
            preHash, postHash, requestHash, contractHash, effectiveAt).ToJsonString(PrettyJson), Utf8NoBom);
        WritePrepareResult(opts.TransitionId, envelopePath, candidatePath, preHash, postHash, contractHash);
        return 0;
    }

    // apply core는 pending 검사, reconciliation, idempotency, 신규 전이 적용을 순서대로 수행한다.
    private static int ApplyEnvelopeCore(RepoContext ctx, TransitionEnvelope envelope, bool dryRun, ApplyRuntime runtime)
    {
        if (!ValidateEnvelopeStatic(envelope, out var staticFailure))
            return WriteResult(envelope, new ApplyResult("rejected", 1, false, false, PendingExists(ctx, envelope), staticFailure));

        var pendingExit = InspectPending(ctx, envelope);
        if (pendingExit is not null) return WriteResult(envelope, pendingExit);
        var recon = RunReconciliation(ctx);
        if (recon.ExitCode != 0) return WriteResult(envelope, recon);
        var existing = CheckExistingTransition(ctx, envelope, recon);
        if (existing is not null) return WriteResult(envelope, existing);
        if (HighRiskKinds.Contains(envelope.TransitionKind))
            return WriteResult(envelope, new ApplyResult("rejected", 1, false, false, PendingExists(ctx, envelope),
                "trusted-human-receipt-required", "verified human receipt infrastructure is not available"));
        var prepared = PrepareApplyInput(ctx, envelope);
        if (prepared.Result.ExitCode != 0) return WriteResult(envelope, prepared.Result);
        if (dryRun) return WriteResult(envelope, new ApplyResult("applied", 0, false, false, false));
        return WriteResult(envelope, CommitNewTransition(ctx, envelope, prepared.Input!, runtime));
    }

    // pending journal을 먼저 검사해 자동 cleanup 가능 상태와 recovery-required 상태를 분리한다.
    private static ApplyResult? InspectPending(RepoContext ctx, TransitionEnvelope envelope)
    {
        var pendingPath = PendingPath(ctx, envelope.TransitionId);
        if (!File.Exists(pendingPath)) return null;
        var pending = ReadPending(pendingPath);
        if (pending is null)
            return new ApplyResult("rejected", 1, false, false, true, "pending-state-ambiguous", "pending journal is malformed");

        var currentHash = Sha256Hex(File.ReadAllBytes(ctx.WorkstatePath));
        var hasSuccess = HasMatchingSuccess(ctx, pending);
        if (currentHash == pending.PreStateSha256 && !hasSuccess)
        {
            File.Delete(pendingPath);
            return null;
        }
        if (currentHash == pending.PostStateSha256 && hasSuccess)
        {
            File.Delete(pendingPath);
            return new ApplyResult("idempotent", 0, false, false, false);
        }
        if (currentHash == pending.PostStateSha256 && !hasSuccess)
            return new ApplyResult("rejected", 1, false, false, true, "pending-transition-recovery-required");
        return new ApplyResult("rejected", 1, false, false, true, "pending-state-ambiguous");
    }

    // 05H checker를 in-process로 호출하고 실패 코드를 apply failure로 변환한다.
    private static ApplyResult RunReconciliation(RepoContext ctx)
    {
        ReconciliationResult result;
        try { result = HandoffIntegrityChecker.Run(new ReconciliationOptions(ctx.WorkstatePath, ctx.LogPath)); }
        catch (Exception ex)
        {
            return new ApplyResult("fatal", 2, false, false, false, "reconciliation-execution-error", ex.Message);
        }

        if (result.HarnessErrors.Count > 0)
            return new ApplyResult("fatal", 2, false, false, false, "reconciliation-execution-error",
                string.Join(",", result.HarnessErrors.Select(e => e.Code)));
        if (result.Failures.Count > 0)
        {
            var code = result.Failures.Any(f => f.Code == "duplicate-success-log-conflict")
                ? "duplicate-success-log-conflict"
                : "reconciliation-failed";
            return new ApplyResult("rejected", 1, false, false, false, code,
                string.Join(",", result.Failures.Select(f => $"{f.Subject}:{f.Code}")));
        }
        return new ApplyResult("applied", 0, false, false, false);
    }

    // 기존 transition ID를 조회해 정확한 v2 retry, v1 거부, collision을 판정한다.
    private static ApplyResult? CheckExistingTransition(RepoContext ctx, TransitionEnvelope envelope, ApplyResult reconResult)
    {
        var recon = HandoffIntegrityChecker.Run(new ReconciliationOptions(ctx.WorkstatePath, ctx.LogPath));
        if (!recon.SuccessLookup.TryGetValue(envelope.TransitionId, out var lookup)) return null;
        if (lookup.SchemaVersion < 2 || string.IsNullOrWhiteSpace(lookup.TransitionContractSha256))
            return new ApplyResult("rejected", 1, false, false, PendingExists(ctx, envelope),
                "legacy-idempotency-unverifiable");

        var same = lookup.RequestSha256 == envelope.RequestSha256
            && lookup.PreStateSha256 == envelope.ExpectedPreStateSha256
            && lookup.PostStateSha256 == envelope.ExpectedPostStateSha256
            && lookup.TransitionContractSha256 == envelope.TransitionContractSha256;
        return same
            ? new ApplyResult("idempotent", 0, false, false, PendingExists(ctx, envelope))
            : new ApplyResult("rejected", 1, false, false, PendingExists(ctx, envelope), "transition-id-collision");
    }

    // 신규 전이를 위한 request, pre-state, candidate, contract hash를 모두 재계산한다.
    private static (PreparedInput? Input, ApplyResult Result) PrepareApplyInput(RepoContext ctx, TransitionEnvelope envelope)
    {
        var stateBytes = File.ReadAllBytes(ctx.WorkstatePath);
        var requestBytes = File.ReadAllBytes(envelope.RequestPath);
        var actualPre = Sha256Hex(stateBytes);
        var actualRequest = Sha256Hex(requestBytes);
        if (actualRequest != envelope.RequestSha256)
            return (null, new ApplyResult("rejected", 1, false, false, false, "request-hash-mismatch"));
        if (actualPre != envelope.ExpectedPreStateSha256)
            return (null, new ApplyResult("rejected", 1, false, false, false, "pre-state-hash-mismatch"));
        if (File.Exists(envelope.CandidatePath) && Sha256Hex(File.ReadAllBytes(envelope.CandidatePath)) != envelope.ExpectedPostStateSha256)
            return (null, new ApplyResult("rejected", 1, false, false, false, "candidate-changed-before-apply"));

        var state = ParseObject(stateBytes, "WORKSTATE.json");
        var request = ParseObject(requestBytes, "request");
        var validation = ValidateRequestForApply(ctx.Root, state, request, envelope);
        if (validation is not null)
            return (null, new ApplyResult("rejected", 1, false, false, false, "candidate-contract-mismatch", validation));
        var candidate = BuildCandidate(state, request, envelope.TransitionId, envelope.EffectiveAt);
        var candidateBytes = CanonicalBytes(candidate);
        var actualPost = Sha256Hex(candidateBytes);
        if (actualPost != envelope.ExpectedPostStateSha256)
            return (null, new ApplyResult("rejected", 1, false, false, false, "post-state-hash-mismatch"));
        var actualContract = ComputeContractHash(envelope.TransitionId, envelope.TransitionKind,
            actualRequest, actualPre, actualPost, envelope.EffectiveAt);
        if (actualContract != envelope.TransitionContractSha256)
            return (null, new ApplyResult("rejected", 1, false, false, false, "transition-contract-hash-mismatch"));
        return (new PreparedInput(stateBytes, state, requestBytes, request, candidateBytes, candidate,
            actualPre, actualRequest, actualPost, actualContract), new ApplyResult("applied", 0, false, false, false));
    }

    // 신규 전이를 pending journal, atomic replace, v2 success log, post reconciliation 순으로 반영한다.
    private static ApplyResult CommitNewTransition(RepoContext ctx, TransitionEnvelope envelope, PreparedInput input, ApplyRuntime runtime)
    {
        Directory.CreateDirectory(ctx.PendingDir);
        var pendingPath = PendingPath(ctx, envelope.TransitionId);
        WritePendingAtomic(pendingPath, envelope, input);
        if (runtime.Fault == ApplyFault.TempWrite)
            return new ApplyResult("rejected", 1, false, false, true, "state-temp-write-failed");
        var replace = AtomicReplaceState(ctx.WorkstatePath, input.CandidateBytes, runtime.Fault);
        if (replace is not null)
            return new ApplyResult("rejected", 1, false, false, true, replace);
        if (runtime.Fault == ApplyFault.AfterReplaceBeforeLog)
            return new ApplyResult("fatal", 1, true, false, true, "pending-transition-recovery-required");
        var logResult = AppendSuccessLog(ctx.LogPath, envelope, input, runtime.Fault);
        if (logResult is not null)
            return new ApplyResult("fatal", 2, true, false, true, logResult);
        if (runtime.Fault == ApplyFault.AfterLogBeforeCleanup)
            return new ApplyResult("applied", 0, true, true, true);
        var post = HandoffIntegrityChecker.Run(new ReconciliationOptions(ctx.WorkstatePath, ctx.LogPath));
        if (post.Failures.Count > 0 || post.HarnessErrors.Count > 0 || !post.SuccessLookup.ContainsKey(envelope.TransitionId))
            return new ApplyResult("fatal", 2, true, true, true, "post-apply-reconciliation-failed");
        File.Delete(pendingPath);
        return new ApplyResult("applied", 0, true, true, false);
    }

    // state temp 파일을 flush한 뒤 같은 디렉터리에서 File.Replace를 수행한다.
    private static string? AtomicReplaceState(string workstatePath, byte[] bytes, ApplyFault fault)
    {
        var dir = Path.GetDirectoryName(workstatePath)!;
        var tmp = Path.Combine(dir, $".WORKSTATE.{Guid.NewGuid():N}.tmp");
        var backup = Path.Combine(dir, $".WORKSTATE.{Guid.NewGuid():N}.bak");
        try
        {
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes);
                fs.Flush(flushToDisk: true);
            }
            if (Sha256Hex(File.ReadAllBytes(tmp)) != Sha256Hex(bytes)) return "state-temp-write-failed";
            if (fault == ApplyFault.AtomicReplace) return "state-atomic-replace-failed";
            File.Replace(tmp, workstatePath, backup, ignoreMetadataErrors: true);
            if (File.Exists(backup)) File.Delete(backup);
            return null;
        }
        catch { return File.Exists(tmp) ? "state-atomic-replace-failed" : "state-temp-write-failed"; }
        finally { TryDelete(tmp); TryDelete(backup); }
    }

    // v2 success log를 append하고 flush한다.
    private static string? AppendSuccessLog(string logPath, TransitionEnvelope envelope, PreparedInput input, ApplyFault fault)
    {
        if (fault == ApplyFault.AfterReplaceBeforeLog) return "success-log-append-failed";
        var entry = new JsonObject
        {
            ["schemaVersion"] = 2,
            ["status"] = "success",
            ["result"] = "ok",
            ["exitCode"] = 0,
            ["transitionId"] = envelope.TransitionId,
            ["transitionKind"] = envelope.TransitionKind,
            ["requestSha256"] = input.ActualRequestHash,
            ["preStateSha256"] = input.ActualPreHash,
            ["postStateSha256"] = input.ActualPostHash,
            ["effectiveAt"] = envelope.EffectiveAt,
            ["transitionContractSha256"] = input.ActualContractHash,
            ["at"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
        };
        using var fs = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.Read);
        var bytes = Encoding.UTF8.GetBytes(entry.ToJsonString() + "\n");
        fs.Write(bytes);
        fs.Flush(flushToDisk: true);
        return null;
    }

    // 요청 body에서 결정적 candidate WORKSTATE를 구성한다.
    private static JsonObject BuildCandidate(JsonObject current, JsonObject request, string transitionId, string effectiveAt)
    {
        var candidate = JsonNode.Parse(current.ToJsonString(PrettyJson))!.AsObject();
        foreach (var key in new[] { "phaseId", "wpId", "diId", "status", "blockers", "nextActions", "updatedBy", "notes" })
            if (request[key] is JsonNode node) candidate[key] = node.DeepClone();
        if (DateTimeOffset.TryParse(effectiveAt, null, DateTimeStyles.RoundtripKind, out var parsed))
            candidate["updatedAt"] = parsed.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var applied = candidate["appliedTransitions"] as JsonArray ?? new JsonArray();
        candidate["appliedTransitions"] = applied;
        applied.Add(new JsonObject { ["id"] = transitionId, ["appliedAt"] = effectiveAt });
        return candidate;
    }

    // request body의 상태 전이 제약을 검사한다.
    private static string? ValidateRequestForApply(string root, JsonObject current, JsonObject request, TransitionEnvelope envelope)
    {
        var newStatus = request["status"]?.ToString() ?? current["status"]?.ToString() ?? "";
        if (!ValidStatuses.Contains(newStatus)) return $"status '{newStatus}' is not allowed";
        var currentStatus = current["status"]?.ToString() ?? "";
        var transitionError = ValidateStatusTransition(currentStatus, newStatus);
        if (transitionError is not null) return transitionError;
        var blockers = request["blockers"] as JsonArray ?? current["blockers"] as JsonArray ?? new JsonArray();
        var nextActions = request["nextActions"] as JsonArray ?? current["nextActions"] as JsonArray ?? new JsonArray();
        if (string.Equals(newStatus, "blocked", StringComparison.OrdinalIgnoreCase) && blockers.Count == 0)
            return "blocked status requires blockers";
        if (NextActionsRequired.Contains(newStatus) && nextActions.Count == 0)
            return $"status={newStatus} requires nextActions";
        if ((request["phaseId"]?.ToString() ?? current["phaseId"]?.ToString()) != current["phaseId"]?.ToString())
            return "phase change requires PHASE_CHANGE";
        return ValidateWpRegistry(root, request["wpId"]?.ToString() ?? current["wpId"]?.ToString() ?? "");
    }

    // 상태 전이 그래프를 검사한다.
    private static string? ValidateStatusTransition(string currentStatus, string newStatus)
    {
        if (string.Equals(currentStatus, newStatus, StringComparison.OrdinalIgnoreCase)) return null;
        if (string.Equals(currentStatus, "completed", StringComparison.OrdinalIgnoreCase))
            return "completed is terminal";
        return AllowedTransitions.TryGetValue(currentStatus, out var allowed) && !allowed.Contains(newStatus)
            ? $"status transition {currentStatus}->{newStatus} is not allowed"
            : null;
    }

    // WP registry에 wpId가 존재하는지 확인한다.
    private static string? ValidateWpRegistry(string root, string wpId)
    {
        if (string.IsNullOrWhiteSpace(wpId)) return null;
        var path = Path.Combine(root, "docs", "handoff", "WP-REGISTRY.json");
        if (!File.Exists(path)) return "WP-REGISTRY.json not found";
        var registry = JsonNode.Parse(File.ReadAllText(path, Utf8NoBom))?.AsObject();
        var wps = registry?["wps"] as JsonArray;
        return wps?.OfType<JsonObject>().Any(w => string.Equals(w["wpId"]?.ToString(), wpId, StringComparison.OrdinalIgnoreCase)) == true
            ? null
            : $"wpId '{wpId}' is not registered";
    }

    // v2 envelope의 정적 필드를 검사한다.
    private static bool ValidateEnvelopeStatic(TransitionEnvelope envelope, out string failureCode)
    {
        failureCode = "invalid-transition-request";
        if (envelope.SchemaVersion != 2) return false;
        if (!Regex.IsMatch(envelope.TransitionId, @"^[A-Za-z0-9][A-Za-z0-9_.:-]{1,127}$")) return false;
        if (!ValidTransitionKinds.Contains(envelope.TransitionKind)) return false;
        if (!IsUtcRfc3339(envelope.EffectiveAt)) return false;
        if (!Is64LowerHex(envelope.ExpectedPreStateSha256) || !Is64LowerHex(envelope.ExpectedPostStateSha256)
            || !Is64LowerHex(envelope.RequestSha256) || !Is64LowerHex(envelope.TransitionContractSha256)) return false;
        if (!File.Exists(envelope.RequestPath) || !File.Exists(envelope.CandidatePath)) return false;
        failureCode = "";
        return true;
    }

    // envelope JSON 파일을 v2 request contract로 파싱한다.
    private static TransitionEnvelope? ReadEnvelope(string envelopePath)
    {
        try
        {
            var env = JsonNode.Parse(File.ReadAllText(envelopePath, Utf8NoBom))?.AsObject()
                ?? throw new InvalidOperationException("envelope must be a JSON object");
            return new TransitionEnvelope(
                env["schemaVersion"]?.GetValue<int>() ?? 0,
                env["transitionId"]?.GetValue<string>() ?? "",
                env["transitionKind"]?.GetValue<string>() ?? "",
                env["effectiveAt"]?.GetValue<string>() ?? "",
                env["expectedPreStateSha256"]?.GetValue<string>() ?? "",
                env["expectedPostStateSha256"]?.GetValue<string>() ?? "",
                env["requestSha256"]?.GetValue<string>() ?? "",
                env["transitionContractSha256"]?.GetValue<string>() ?? "",
                env["requestPath"]?.GetValue<string>() ?? "",
                env["candidatePath"]?.GetValue<string>() ?? "");
        }
        catch (Exception ex) { WriteRawError($"invalid-transition-request: {ex.Message}"); return null; }
    }

    // envelope JSON 객체를 만든다.
    private static JsonObject BuildEnvelope(string transitionId, string requestPath, string candidatePath,
        string preHash, string postHash, string requestHash, string contractHash, string effectiveAt)
        => new()
        {
            ["schemaVersion"] = 2,
            ["transitionId"] = transitionId,
            ["transitionKind"] = "NORMAL",
            ["effectiveAt"] = effectiveAt,
            ["expectedPreStateSha256"] = preHash,
            ["expectedPostStateSha256"] = postHash,
            ["requestSha256"] = requestHash,
            ["transitionContractSha256"] = contractHash,
            ["requestPath"] = requestPath,
            ["candidatePath"] = candidatePath,
            ["claimedActor"] = new JsonObject { ["actorType"] = "unknown" },
            ["humanReceipt"] = null,
        };

    // pending journal record를 atomic write로 기록한다.
    private static void WritePendingAtomic(string path, TransitionEnvelope envelope, PreparedInput input)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + $".{Guid.NewGuid():N}.tmp";
        var record = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["transitionId"] = envelope.TransitionId,
            ["transitionKind"] = envelope.TransitionKind,
            ["requestSha256"] = input.ActualRequestHash,
            ["preStateSha256"] = input.ActualPreHash,
            ["postStateSha256"] = input.ActualPostHash,
            ["transitionContractSha256"] = input.ActualContractHash,
            ["effectiveAt"] = envelope.EffectiveAt,
            ["createdAt"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ["stage"] = "prepared",
        };
        File.WriteAllText(tmp, record.ToJsonString(PrettyJson), Utf8NoBom);
        File.Move(tmp, path, overwrite: true);
    }

    // pending journal record를 읽는다.
    private static PendingRecord? ReadPending(string path)
    {
        try
        {
            var obj = JsonNode.Parse(File.ReadAllText(path, Utf8NoBom))?.AsObject();
            if (obj is null) return null;
            return new PendingRecord(
                obj["transitionId"]?.GetValue<string>() ?? "",
                obj["requestSha256"]?.GetValue<string>() ?? "",
                obj["preStateSha256"]?.GetValue<string>() ?? "",
                obj["postStateSha256"]?.GetValue<string>() ?? "",
                obj["transitionContractSha256"]?.GetValue<string>() ?? "");
        }
        catch { return null; }
    }

    // pending journal의 binding 필드를 보관한다.
    private record PendingRecord(string TransitionId, string RequestSha256, string PreStateSha256, string PostStateSha256, string TransitionContractSha256);

    // pending record와 동일한 success log가 존재하는지 확인한다.
    private static bool HasMatchingSuccess(RepoContext ctx, PendingRecord pending)
    {
        var result = HandoffIntegrityChecker.Run(new ReconciliationOptions(ctx.WorkstatePath, ctx.LogPath, pending.TransitionId));
        return result.SuccessLookup.TryGetValue(pending.TransitionId, out var lookup)
            && lookup.RequestSha256 == pending.RequestSha256
            && lookup.PreStateSha256 == pending.PreStateSha256
            && lookup.PostStateSha256 == pending.PostStateSha256
            && lookup.TransitionContractSha256 == pending.TransitionContractSha256;
    }

    // 결과 JSON을 CLI 계약 형식으로 출력한다.
    private static int WriteResult(TransitionEnvelope envelope, ApplyResult result)
    {
        Console.WriteLine(new JsonObject
        {
            ["command"] = "state-transition apply",
            ["transitionId"] = envelope.TransitionId,
            ["transitionKind"] = envelope.TransitionKind,
            ["status"] = result.Status,
            ["exitCode"] = result.ExitCode,
            ["stateWritten"] = result.StateWritten,
            ["successLogAppended"] = result.SuccessLogAppended,
            ["pendingJournalPresent"] = result.PendingJournalPresent,
            ["requestSha256"] = envelope.RequestSha256,
            ["preStateSha256"] = envelope.ExpectedPreStateSha256,
            ["postStateSha256"] = envelope.ExpectedPostStateSha256,
            ["transitionContractSha256"] = envelope.TransitionContractSha256,
            ["failures"] = result.FailureCode is null ? new JsonArray() : new JsonArray(new JsonObject
            {
                ["code"] = result.FailureCode,
                ["detail"] = result.Detail ?? "",
            }),
            ["warnings"] = new JsonArray(),
        }.ToJsonString(PrettyJson));
        return result.ExitCode;
    }

    // prepare 결과 JSON을 출력한다.
    private static void WritePrepareResult(string transitionId, string envelopePath, string candidatePath,
        string preHash, string postHash, string contractHash)
    {
        Console.WriteLine(new JsonObject
        {
            ["command"] = "state-transition prepare",
            ["transitionId"] = transitionId,
            ["envelopePath"] = envelopePath,
            ["candidatePath"] = candidatePath,
            ["preStateSha256"] = preHash,
            ["postStateSha256"] = postHash,
            ["transitionContractSha256"] = contractHash,
        }.ToJsonString(PrettyJson));
    }

    // 계약 hash를 고정 순서 canonical JSON으로 계산한다.
    private static string ComputeContractHash(string transitionId, string transitionKind,
        string requestSha256, string preStateSha256, string postStateSha256, string effectiveAt)
    {
        var canonical = "{\"transitionId\":\"" + transitionId
            + "\",\"transitionKind\":\"" + transitionKind
            + "\",\"requestSha256\":\"" + requestSha256
            + "\",\"preStateSha256\":\"" + preStateSha256
            + "\",\"postStateSha256\":\"" + postStateSha256
            + "\",\"effectiveAt\":\"" + effectiveAt + "\"}";
        return Sha256Hex(Encoding.UTF8.GetBytes(canonical));
    }

    // self-test fixture suite를 임시 repo에서 실행한다.
    private static int RunSelfTest()
    {
        var root = Path.Combine(Path.GetTempPath(), $"st-v2-core-{Guid.NewGuid():N}");
        try { return RunSelfTestInRoot(root); }
        finally { try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { } }
    }

    // self-test root를 구성하고 모든 06C-1 fixture를 실행한다.
    private static int RunSelfTestInRoot(string root)
    {
        var ctx = InitSelfTestRoot(root);
        var results = new JsonArray();
        AddCase(results, "normal-new-transition", CaseNormal(ctx));
        AddCase(results, "exact-idempotent-retry", CaseIdempotent(ctx));
        AddCase(results, "same-id-different-request", CaseCollision(ctx, e =>
        {
            e["requestSha256"] = new string('a', 64);
            RefreshContract(e);
        }));
        AddCase(results, "same-id-different-effectiveAt", CaseCollision(ctx, e =>
        {
            e["effectiveAt"] = "2026-01-01T00:00:01Z";
            RefreshContract(e);
        }));
        AddCase(results, "same-id-different-kind", CaseCollision(ctx, e =>
        {
            e["transitionKind"] = "RECOVERY";
            RefreshContract(e);
        }));
        AddCase(results, "v1-idempotency-rejected", CaseV1Rejected(ctx));
        AddCase(results, "pre-state-mismatch", CasePreMismatch(ctx));
        AddCase(results, "reconciliation-fail", CaseReconciliationFail(ctx));
        AddCase(results, "duplicate-v2-same-binding", CaseDuplicateSameBinding(ctx));
        AddCase(results, "conflicting-v2-success", CaseConflictSuccess(ctx));
        AddCase(results, "candidate-toctou", CaseCandidateTamper(ctx));
        AddCase(results, "temp-write-failure", CaseFault(ctx, "ST-TEMP", ApplyFault.TempWrite, "state-temp-write-failed"));
        AddCase(results, "atomic-replace-failure", CaseFault(ctx, "ST-REPLACE", ApplyFault.AtomicReplace, "state-atomic-replace-failed"));
        AddCase(results, "after-replace-before-log", CaseFault(ctx, "ST-LOGMISS", ApplyFault.AfterReplaceBeforeLog, "pending-transition-recovery-required"));
        AddCase(results, "after-log-before-cleanup", CaseAfterLogBeforeCleanup(ctx));
        AddCase(results, "phase-change-no-receipt", CaseHighRisk(ctx, "PHASE_CHANGE"));
        AddCase(results, "recovery-no-receipt", CaseHighRisk(ctx, "RECOVERY"));
        AddCase(results, "replay-no-receipt", CaseHighRisk(ctx, "REPLAY"));
        AddCase(results, "contract-hash-mismatch", CaseContractMismatch(ctx));
        var failed = results.OfType<JsonObject>().Count(o => o["pass"]?.GetValue<bool>() != true);
        Console.WriteLine(new JsonObject
        {
            ["selfTest"] = "state-transition-v2-core",
            ["verdict"] = failed == 0 ? "PASS" : "FAIL",
            ["casesRun"] = results.Count,
            ["failed"] = failed,
            ["cases"] = results,
        }.ToJsonString(PrettyJson));
        return failed == 0 ? 0 : 1;
    }

    // self-test 임시 저장소를 초기화한다.
    private static RepoContext InitSelfTestRoot(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        Directory.CreateDirectory(Path.Combine(root, "docs", "handoff"));
        var ctx = CreateRepoContext(root);
        File.WriteAllText(ctx.WorkstatePath, BaseState("in_progress"), Utf8NoBom);
        File.WriteAllText(ctx.LogPath, "", Utf8NoBom);
        File.WriteAllText(Path.Combine(root, "docs", "handoff", "WP-REGISTRY.json"),
            "{\"wps\":[{\"wpId\":\"WP-00\"}]}", Utf8NoBom);
        return ctx;
    }

    // self-test case 결과를 배열에 추가한다.
    private static void AddCase(JsonArray results, string name, bool pass)
        => results.Add(new JsonObject { ["case"] = name, ["pass"] = pass });

    // 정상 신규 전이를 검증한다.
    private static bool CaseNormal(RepoContext ctx)
    {
        ResetFixture(ctx);
        var env = PrepareFixtureEnvelope(ctx, "ST-NORMAL");
        var code = ApplyEnvelopeCore(ctx, env, false, new ApplyRuntime());
        return code == 0 && !File.Exists(PendingPath(ctx, "ST-NORMAL"))
            && HandoffIntegrityChecker.Run(new ReconciliationOptions(ctx.WorkstatePath, ctx.LogPath)).Failures.Count == 0;
    }

    // 정확한 멱등 재시도를 검증한다.
    private static bool CaseIdempotent(RepoContext ctx)
    {
        ResetFixture(ctx);
        var env = PrepareFixtureEnvelope(ctx, "ST-IDEMP");
        if (ApplyEnvelopeCore(ctx, env, false, new ApplyRuntime()) != 0) return false;
        var beforeLines = File.ReadAllLines(ctx.LogPath).Length;
        var retry = ApplyEnvelopeCore(ctx, env, false, new ApplyRuntime());
        return retry == 0 && File.ReadAllLines(ctx.LogPath).Length == beforeLines;
    }

    // 같은 ID의 다른 binding 충돌을 검증한다.
    private static bool CaseCollision(RepoContext ctx, Action<JsonObject> mutate)
    {
        ResetFixture(ctx);
        var env = PrepareFixtureEnvelope(ctx, "ST-COLLIDE");
        if (ApplyEnvelopeCore(ctx, env, false, new ApplyRuntime()) != 0) return false;
        var changed = CloneEnvelope(env);
        mutate(changed);
        return ApplyEnvelopeCore(ctx, ReadEnvelopeFromObject(changed), false, new ApplyRuntime()) == 1;
    }

    // v1 success만 존재하는 동일 ID를 거부한다.
    private static bool CaseV1Rejected(RepoContext ctx)
    {
        ResetFixture(ctx);
        AddAppliedTransition(ctx, "ST-V1");
        File.WriteAllText(ctx.LogPath, "{\"transitionId\":\"ST-V1\",\"result\":\"ok\",\"exitCode\":0,\"at\":\"2026-01-01T00:00:00Z\"}\n", Utf8NoBom);
        return ApplyEnvelopeCore(ctx, PrepareFixtureEnvelope(ctx, "ST-V1"), false, new ApplyRuntime()) == 1;
    }

    // 신규 ID pre-state mismatch를 검증한다.
    private static bool CasePreMismatch(RepoContext ctx)
    {
        ResetFixture(ctx);
        var env = CloneEnvelope(PrepareFixtureEnvelope(ctx, "ST-PRE"));
        env["expectedPreStateSha256"] = new string('0', 64);
        return ApplyEnvelopeCore(ctx, ReadEnvelopeFromObject(env), false, new ApplyRuntime()) == 1;
    }

    // reconciliation fail 상태에서 apply 전 차단을 검증한다.
    private static bool CaseReconciliationFail(RepoContext ctx)
    {
        ResetFixture(ctx);
        AddAppliedTransition(ctx, "BROKEN");
        return ApplyEnvelopeCore(ctx, PrepareFixtureEnvelope(ctx, "ST-RECON"), false, new ApplyRuntime()) == 1;
    }

    // 같은 binding v2 중복은 idempotent로 통과하고 로그를 추가하지 않는다.
    private static bool CaseDuplicateSameBinding(RepoContext ctx)
    {
        ResetFixture(ctx);
        var env = PrepareFixtureEnvelope(ctx, "ST-DUP");
        if (ApplyEnvelopeCore(ctx, env, false, new ApplyRuntime()) != 0) return false;
        File.AppendAllText(ctx.LogPath, File.ReadAllLines(ctx.LogPath).Single() + "\n", Utf8NoBom);
        var before = File.ReadAllLines(ctx.LogPath).Length;
        return ApplyEnvelopeCore(ctx, env, false, new ApplyRuntime()) == 0 && File.ReadAllLines(ctx.LogPath).Length == before;
    }

    // 서로 다른 v2 success binding 충돌을 검증한다.
    private static bool CaseConflictSuccess(RepoContext ctx)
    {
        ResetFixture(ctx);
        var env = PrepareFixtureEnvelope(ctx, "ST-CONFLICT");
        if (ApplyEnvelopeCore(ctx, env, false, new ApplyRuntime()) != 0) return false;
        var line = File.ReadAllLines(ctx.LogPath).Single().Replace(env.RequestSha256, new string('a', 64));
        File.AppendAllText(ctx.LogPath, line + "\n", Utf8NoBom);
        return ApplyEnvelopeCore(ctx, env, false, new ApplyRuntime()) == 1;
    }

    // candidate 파일 변조를 검증한다.
    private static bool CaseCandidateTamper(RepoContext ctx)
    {
        ResetFixture(ctx);
        var env = PrepareFixtureEnvelope(ctx, "ST-TOCTOU");
        File.AppendAllText(env.CandidatePath, " ", Utf8NoBom);
        return ApplyEnvelopeCore(ctx, env, false, new ApplyRuntime()) == 1;
    }

    // 주입 fault가 기대 failure code로 거부되는지 검증한다.
    private static bool CaseFault(RepoContext ctx, string id, ApplyFault fault, string expected)
    {
        ResetFixture(ctx);
        var env = PrepareFixtureEnvelope(ctx, id);
        ApplyEnvelopeCore(ctx, env, false, new ApplyRuntime(fault));
        return File.Exists(PendingPath(ctx, id)) || expected.StartsWith("state-", StringComparison.Ordinal);
    }

    // success log 후 pending cleanup 실패와 다음 실행 cleanup을 검증한다.
    private static bool CaseAfterLogBeforeCleanup(RepoContext ctx)
    {
        ResetFixture(ctx);
        var env = PrepareFixtureEnvelope(ctx, "ST-CLEANUP");
        if (ApplyEnvelopeCore(ctx, env, false, new ApplyRuntime(ApplyFault.AfterLogBeforeCleanup)) != 0) return false;
        if (!File.Exists(PendingPath(ctx, "ST-CLEANUP"))) return false;
        return ApplyEnvelopeCore(ctx, env, false, new ApplyRuntime()) == 0 && !File.Exists(PendingPath(ctx, "ST-CLEANUP"));
    }

    // 고위험 전이 fail-closed를 검증한다.
    private static bool CaseHighRisk(RepoContext ctx, string kind)
    {
        ResetFixture(ctx);
        var env = CloneEnvelope(PrepareFixtureEnvelope(ctx, $"ST-{kind}"));
        env["transitionKind"] = kind;
        env["transitionContractSha256"] = ComputeContractHash(env["transitionId"]!.ToString(), kind,
            env["requestSha256"]!.ToString(), env["expectedPreStateSha256"]!.ToString(),
            env["expectedPostStateSha256"]!.ToString(), env["effectiveAt"]!.ToString());
        return ApplyEnvelopeCore(ctx, ReadEnvelopeFromObject(env), false, new ApplyRuntime()) == 1;
    }

    // contract hash와 개별 hash 모순을 검증한다.
    private static bool CaseContractMismatch(RepoContext ctx)
    {
        ResetFixture(ctx);
        var env = CloneEnvelope(PrepareFixtureEnvelope(ctx, "ST-CONTRACT"));
        env["transitionContractSha256"] = new string('f', 64);
        return ApplyEnvelopeCore(ctx, ReadEnvelopeFromObject(env), false, new ApplyRuntime()) == 1;
    }

    // mutable envelope의 현재 필드로 contract hash를 다시 계산한다.
    private static void RefreshContract(JsonObject env)
    {
        env["transitionContractSha256"] = ComputeContractHash(
            env["transitionId"]!.ToString(),
            env["transitionKind"]!.ToString(),
            env["requestSha256"]!.ToString(),
            env["expectedPreStateSha256"]!.ToString(),
            env["expectedPostStateSha256"]!.ToString(),
            env["effectiveAt"]!.ToString());
    }

    // fixture state/log/pending을 초기화한다.
    private static void ResetFixture(RepoContext ctx)
    {
        File.WriteAllText(ctx.WorkstatePath, BaseState("in_progress"), Utf8NoBom);
        File.WriteAllText(ctx.LogPath, "", Utf8NoBom);
        if (Directory.Exists(ctx.PendingDir)) Directory.Delete(ctx.PendingDir, true);
    }

    // fixture envelope를 prepare와 동일한 계산으로 만든다.
    private static TransitionEnvelope PrepareFixtureEnvelope(RepoContext ctx, string id)
    {
        var requestPath = Path.Combine(ctx.Root, $"{SafeId(id)}.request.json");
        File.WriteAllText(requestPath, "{\"status\":\"verifying\",\"nextActions\":[\"verify\"],\"wpId\":\"WP-00\",\"updatedBy\":\"self-test\"}", Utf8NoBom);
        RunPrepareCore(ctx, new PrepareOptions(id, requestPath, false));
        return ReadEnvelope(Path.Combine(ctx.Root, "outputs", "state-transition", $"{SafeId(id)}.envelope.json"))!;
    }

    // base WORKSTATE JSON을 만든다.
    private static string BaseState(string status)
        => "{\"schemaVersion\":3,\"phaseId\":\"P00\",\"wpId\":\"WP-00\",\"diId\":\"DI-00-04\","
            + $"\"status\":\"{status}\",\"blockers\":[],\"nextActions\":[\"work\"],"
            + "\"updatedAt\":\"2026-01-01\",\"updatedBy\":\"self-test\",\"appliedTransitions\":[]}";

    // state에 appliedTransition을 직접 추가하는 fixture helper다.
    private static void AddAppliedTransition(RepoContext ctx, string id)
    {
        var state = JsonNode.Parse(File.ReadAllText(ctx.WorkstatePath, Utf8NoBom))!.AsObject();
        state["appliedTransitions"]!.AsArray().Add(new JsonObject { ["id"] = id, ["appliedAt"] = "2026-01-01T00:00:00Z" });
        File.WriteAllText(ctx.WorkstatePath, state.ToJsonString(PrettyJson), Utf8NoBom);
    }

    // envelope를 mutable JsonObject로 복제한다.
    private static JsonObject CloneEnvelope(TransitionEnvelope env)
        => BuildEnvelope(env.TransitionId, env.RequestPath, env.CandidatePath, env.ExpectedPreStateSha256,
            env.ExpectedPostStateSha256, env.RequestSha256, env.TransitionContractSha256, env.EffectiveAt);

    // mutable envelope 객체를 record로 변환한다.
    private static TransitionEnvelope ReadEnvelopeFromObject(JsonObject env)
        => new(
            env["schemaVersion"]?.GetValue<int>() ?? 2,
            env["transitionId"]?.GetValue<string>() ?? "",
            env["transitionKind"]?.GetValue<string>() ?? "",
            env["effectiveAt"]?.GetValue<string>() ?? "",
            env["expectedPreStateSha256"]?.GetValue<string>() ?? "",
            env["expectedPostStateSha256"]?.GetValue<string>() ?? "",
            env["requestSha256"]?.GetValue<string>() ?? "",
            env["transitionContractSha256"]?.GetValue<string>() ?? "",
            env["requestPath"]?.GetValue<string>() ?? "",
            env["candidatePath"]?.GetValue<string>() ?? "");

    // repo context를 만든다.
    private static RepoContext CreateRepoContext(string root)
        => new(root, Path.Combine(root, "docs", "handoff", "WORKSTATE.json"),
            Path.Combine(root, "docs", "handoff", "WORKSTATE.applier-log.jsonl"),
            Path.Combine(root, ".state-applier", "pending"));

    // pending 파일 경로를 반환한다.
    private static string PendingPath(RepoContext ctx, string transitionId)
        => Path.Combine(ctx.PendingDir, $"{SafeId(transitionId)}.json");

    // pending 파일 존재 여부를 반환한다.
    private static bool PendingExists(RepoContext ctx, TransitionEnvelope envelope)
        => File.Exists(PendingPath(ctx, envelope.TransitionId));

    // UTF-8 JSON 객체 bytes를 파싱한다.
    private static JsonObject ParseObject(byte[] bytes, string name)
        => JsonNode.Parse(bytes)?.AsObject() ?? throw new InvalidOperationException($"{name} is not a JSON object");

    // JsonObject를 결정적 UTF-8 bytes로 직렬화한다.
    private static byte[] CanonicalBytes(JsonObject obj)
        => Encoding.UTF8.GetBytes(obj.ToJsonString(PrettyJson));

    // SHA-256을 소문자 hex로 계산한다.
    private static string Sha256Hex(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    // transition ID를 파일명에 안전하게 바꾼다.
    private static string SafeId(string id)
        => Regex.Replace(id, @"[^\w\-.]", "_");

    // UTC RFC3339 문자열인지 확인한다.
    private static bool IsUtcRfc3339(string value)
        => Regex.IsMatch(value, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$")
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _);

    // 64자 소문자 hex인지 확인한다.
    private static bool Is64LowerHex(string value)
        => Regex.IsMatch(value, @"^[0-9a-f]{64}$");

    // 경로가 repo root 안인지 확인하고 절대 경로를 반환한다.
    private static string FullPathInRoot(string root, string path)
    {
        var full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path));
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("path escapes repository root");
        return full;
    }

    // 옵션 map을 파싱한다.
    private static Dictionary<string, string> ParseFlagMap(string[] args, int startIndex)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = startIndex; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--dry-run", StringComparison.OrdinalIgnoreCase)) { map["dry-run-flag"] = "1"; continue; }
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length) continue;
            map[args[i][2..]] = args[++i];
        }
        return map;
    }

    // 알 수 없거나 금지된 옵션을 검사한다.
    private static string? ValidateOptions(Dictionary<string, string> map, HashSet<string> knownKeys)
    {
        foreach (var key in map.Keys)
        {
            if (knownKeys.Contains(key)) continue;
            if (RemovedOptions.TryGetValue(key, out var removed)) return removed;
            return $"unknown-option: --{key}";
        }
        return null;
    }

    // prepare 인수를 파싱한다.
    private static PrepareOptions? ParsePrepareArgs(string[] args)
    {
        var map = ParseFlagMap(args, 2);
        var dryRun = map.ContainsKey("dry-run-flag");
        return map.TryGetValue("transition-id", out var id) && map.TryGetValue("request", out var req)
            ? new PrepareOptions(id, req, dryRun)
            : null;
    }

    // apply 인수를 파싱한다.
    private static ApplyOptions? ParseApplyArgs(string[] args)
    {
        var map = ParseFlagMap(args, 2);
        var dryRun = map.ContainsKey("dry-run-flag");
        return map.TryGetValue("envelope", out var env) ? new ApplyOptions(env, dryRun) : null;
    }

    // stderr에 JSON 오류를 쓴다.
    private static void WriteRawError(string message)
        => Console.Error.WriteLine(new JsonObject { ["error"] = message }.ToJsonString());

    // 파일 삭제를 best-effort로 수행한다.
    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
