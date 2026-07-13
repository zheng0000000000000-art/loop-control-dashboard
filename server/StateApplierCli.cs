// WORKSTATE 상태 전이의 유일한 writer.
// prepare: 전이 계획(envelope+candidate)을 outputs/state-transition/에 기록한다.
// apply: reconciliation-먼저, 멱등 결속, rollback, FATAL 분류 포함 전이를 실행한다.
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

internal static class StateApplierCli
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly HashSet<string> ValidStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "waiting", "in_progress", "verifying", "completed", "blocked",
    };

    // 허용 전이 화이트리스트 — 이 표 밖의 전이는 전부 거부(completed는 terminal).
    private static readonly Dictionary<string, HashSet<string>> AllowedTransitions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["waiting"] = new(StringComparer.OrdinalIgnoreCase) { "in_progress", "blocked" },
        ["in_progress"] = new(StringComparer.OrdinalIgnoreCase) { "verifying", "blocked", "waiting" },
        ["verifying"] = new(StringComparer.OrdinalIgnoreCase) { "completed", "in_progress", "blocked" },
        ["blocked"] = new(StringComparer.OrdinalIgnoreCase) { "waiting", "in_progress", "verifying" },
        ["completed"] = new(StringComparer.OrdinalIgnoreCase) { },
    };

    // nextActions 필수 status 목록(completed 제외).
    private static readonly HashSet<string> NextActionsRequired = new(StringComparer.OrdinalIgnoreCase)
    {
        "waiting", "in_progress", "verifying", "blocked",
    };

    // 허용 transitionKind 목록 — 이 밖은 unknown-transition-kind로 fail-closed.
    private static readonly HashSet<string> ValidTransitionKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "NORMAL", "PHASE_CHANGE", "RECOVERY", "REPLAY",
    };

    // high-risk transitionKind — receipt ledger 부재로 이번 WP에서 fail-closed.
    private static readonly HashSet<string> HighRiskKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "PHASE_CHANGE", "RECOVERY", "REPLAY",
    };

    // 파싱된 envelope 데이터 레코드.
    private record EnvelopeData(
        int SchemaVersion,
        string TransitionKind,
        string TransitionId,
        string ExpectedPreStateSha256,
        string RequestPath,
        string RequestSha256,
        string EffectiveAt,
        string ExpectedPostStateSha256,
        string? TransitionContractSha256,
        string CandidatePath);

    // prepare 부속 명령 옵션.
    private record PrepareOptions(string TransitionId, string RequestPath, bool DryRun);

    // apply 부속 명령 옵션.
    private record ApplyOptions(string EnvelopePath, string? VerdictPath, bool DryRun);

    // 결정적 test seam — 같은 프로세스의 자기 시험 코드만 설정할 수 있다. production 진입점(CLI·환경변수·설정파일)에서 켤 수 없음.
    internal static Func<string?>? FailAfterWriteHook;

    // 결정적 projection seam — 자기 시험 코드만 설정. 환경변수·CLI에서 켤 수 없음.
    internal static Func<int>? ProjectionOverride;

    // 결정적 복원 실패 seam — rollback 내 복원 단계를 실패시킨다. 자기 시험 코드만 설정.
    internal static bool FailRestoreForTest;

    // 삭제된 옵션과 그 이유 메시지 — 일반 unknown-option과 구분한 명시적 오류.
    private static readonly Dictionary<string, string> RemovedOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["human-decision"] = "removed-option: --human-decision (06C-1-R1에서 삭제됨. PHASE_CHANGE/RECOVERY/REPLAY는 trusted-human-receipt-required로 fail-closed)",
        ["root"] = "removed-option: --root (06C-1-R1에서 삭제됨. 사본 시험은 process cwd를 바꿔서 하라)",
    };

    // prepare 허용 키 — 이 밖의 키는 ValidateOptions에서 거부한다.
    private static readonly HashSet<string> PrepareKnownKeys = new(StringComparer.OrdinalIgnoreCase)
        { "transition-id", "request", "dry-run-flag" };

    // apply 허용 키 — 이 밖의 키는 ValidateOptions에서 거부한다.
    private static readonly HashSet<string> ApplyKnownKeys = new(StringComparer.OrdinalIgnoreCase)
        { "envelope", "verdict", "dry-run-flag" };

    // state-transition 진입점. prepare/apply/--self-test를 args[1]로 분기하고 그 외는 exit 2.
    internal static int Run(string[] args)
    {
        var sub = args.Length > 1 ? args[1] : "";
        if (string.Equals(sub, "prepare", StringComparison.OrdinalIgnoreCase))
            return RunPrepare(args);
        if (string.Equals(sub, "apply", StringComparison.OrdinalIgnoreCase))
            return RunApply(args);
        if (string.Equals(sub, "--self-test", StringComparison.OrdinalIgnoreCase))
            return RunSelfTest();
        Console.Error.WriteLine("{\"error\":\"state-transition usage: prepare --transition-id <id> --request <file> | apply --envelope <file> | --self-test\"}");
        return 2;
    }

    // prepare: 옵션을 검증하고 root를 찾아 RunPrepareCore에 위임한다.
    private static int RunPrepare(string[] args)
    {
        var optErr = ValidateOptions(ParseFlagMap(args, 2), PrepareKnownKeys);
        if (optErr is not null) { Console.Error.WriteLine($"{{\"error\":\"{optErr}\"}}"); return 2; }
        var opts = ParsePrepareArgs(args);
        if (opts is null)
        {
            Console.Error.WriteLine("{\"error\":\"prepare에는 --transition-id, --request 가 필요합니다\"}");
            return 2;
        }
        try { return RunPrepareCore(GitTools.FindRepoRoot(), opts); }
        catch (Exception ex) { Console.Error.WriteLine($"{{\"error\":\"state-transition prepare 실패: {ex.Message}\"}}"); return 2; }
    }

    // prepare 핵심 — root를 명시적으로 받아 자기 시험과 production 경로 공유.
    private static int RunPrepareCore(string root, PrepareOptions opts)
    {
        var workstatePath = Path.Combine(root, "docs", "handoff", "WORKSTATE.json");
        if (!File.Exists(workstatePath)) { WriteError("WORKSTATE.json not found"); return 2; }

        var rawWorkstate = File.ReadAllText(workstatePath, new UTF8Encoding(false));
        var workstate = JsonNode.Parse(rawWorkstate)?.AsObject();
        if (workstate is null) { WriteError("WORKSTATE.json is not a valid JSON object"); return 2; }

        if (!File.Exists(opts.RequestPath)) { WriteError($"request file not found: {opts.RequestPath}"); return 2; }
        var rawRequest = File.ReadAllText(opts.RequestPath, new UTF8Encoding(false));
        var request = JsonNode.Parse(rawRequest)?.AsObject();
        if (request is null) { WriteError("request JSON must be a JSON object"); return 2; }

        // 해시 계산 + effectiveAt 발급 (NORMAL은 prepare가 현재 UTC를 발급, 외부 플래그 없음).
        var preStateSha256 = ComputeSha256(rawWorkstate);
        var requestSha256 = ComputeSha256(rawRequest);
        var effectiveAt = DateTime.UtcNow.ToString("o");

        // 결정적 candidate 계산 — UtcNow 호출 없이 effectiveAt을 파라미터로 받는다.
        var candidate = BuildCandidate(workstate, request, opts.TransitionId, effectiveAt);
        var candidateBytes = Encoding.UTF8.GetBytes(candidate.ToJsonString(WriteOptions));
        var postStateSha256 = Convert.ToHexString(SHA256.HashData(candidateBytes)).ToLowerInvariant();
        var contractHash = ComputeContractHash(
            opts.TransitionId, "NORMAL", requestSha256, preStateSha256, postStateSha256, effectiveAt);

        if (opts.DryRun)
        {
            Console.WriteLine(new JsonObject
            {
                ["ok"] = true, ["dryRun"] = true,
                ["transitionId"] = opts.TransitionId,
                ["preStateSha256"] = preStateSha256,
                ["expectedPostStateSha256"] = postStateSha256,
                ["transitionContractSha256"] = contractHash,
            }.ToJsonString(WriteOptions));
            return 0;
        }

        return WritePrepareOutput(root, opts, preStateSha256, requestSha256, postStateSha256, contractHash, candidateBytes, effectiveAt);
    }

    // envelope+candidate 파일을 outputs/state-transition/에 기록하고 결과를 출력한다.
    private static int WritePrepareOutput(
        string root, PrepareOptions opts, string preStateSha256, string requestSha256,
        string postStateSha256, string contractHash, byte[] candidateBytes, string effectiveAt)
    {
        var outDir = Path.Combine(root, "outputs", "state-transition");
        Directory.CreateDirectory(outDir);
        var safeTid = Regex.Replace(opts.TransitionId, @"[^\w\-]", "_");

        // candidate 파일 기록 — evidence용. apply는 이 파일을 canonical write에 쓰지 않는다.
        var candidatePath = Path.Combine(outDir, $"{safeTid}.candidate.json");
        File.WriteAllBytes(candidatePath, candidateBytes);

        var envelope = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["transitionKind"] = "NORMAL",
            ["transitionId"] = opts.TransitionId,
            ["expectedPreStateSha256"] = preStateSha256,
            ["requestPath"] = opts.RequestPath,
            ["requestSha256"] = requestSha256,
            ["effectiveAt"] = effectiveAt,
            ["expectedPostStateSha256"] = postStateSha256,
            ["transitionContractSha256"] = contractHash,
            ["candidatePath"] = candidatePath,
        };

        var envelopePath = Path.Combine(outDir, $"{safeTid}.envelope.json");
        File.WriteAllText(envelopePath, envelope.ToJsonString(WriteOptions), new UTF8Encoding(false));

        Console.WriteLine(new JsonObject
        {
            ["ok"] = true,
            ["transitionId"] = opts.TransitionId,
            ["envelopePath"] = envelopePath,
            ["candidatePath"] = candidatePath,
            ["preStateSha256"] = preStateSha256,
            ["expectedPostStateSha256"] = postStateSha256,
            ["transitionContractSha256"] = contractHash,
        }.ToJsonString(WriteOptions));
        return 0;
    }

    // apply: 옵션을 검증하고 ApplyEnvelopeCore에 위임한다.
    private static int RunApply(string[] args)
    {
        var optErr = ValidateOptions(ParseFlagMap(args, 2), ApplyKnownKeys);
        if (optErr is not null) { Console.Error.WriteLine($"{{\"error\":\"{optErr}\"}}"); return 2; }
        var opts = ParseApplyArgs(args);
        if (opts is null)
        {
            Console.Error.WriteLine("{\"error\":\"apply에는 --envelope 이 필요합니다\"}");
            return 2;
        }

        var envelope = ReadEnvelope(opts.EnvelopePath);
        if (envelope is null) return 2;

        var ctx = LoadWorkstateContextFromRoot(GitTools.FindRepoRoot());
        if (ctx is null) return 2;
        return ApplyEnvelopeCore(ctx.Value, envelope, opts.DryRun);
    }

    // apply 핵심 — root를 명시적으로 받아 자기 시험과 production 경로 공유.
    private static int ApplyEnvelopeCore(
        (string root, string workstatePath, string logPath, byte[] rawWorkstateBytes, JsonObject workstate, string rawWorkstateStr) ctx,
        EnvelopeData envelope, bool dryRun)
    {
        // unknown transitionKind는 fail-closed — NORMAL/PHASE_CHANGE/RECOVERY/REPLAY 외 거부.
        if (!ValidTransitionKinds.Contains(envelope.TransitionKind))
            return Fail(1, envelope.TransitionId, "unknown-transition-kind",
                $"transitionKind='{envelope.TransitionKind}'은 알 수 없는 종류입니다 (허용: NORMAL, PHASE_CHANGE, RECOVERY, REPLAY)");

        // high-risk transitionKind — receipt ledger 부재로 항상 fail-closed.
        if (HighRiskKinds.Contains(envelope.TransitionKind))
            return Fail(1, envelope.TransitionId, "trusted-human-receipt-required",
                $"transitionKind={envelope.TransitionKind}는 receipt ledger 부재로 이번 WP에서 fail-closed");

        var (root, workstatePath, logPath, rawWorkstateBytes, workstate, rawWorkstateStr) = ctx;

        // Step 1: request sha256 + candidate file hash 검증.
        var (_, request, evidenceExit) = VerifyApplyEvidence(envelope);
        if (evidenceExit.HasValue) return evidenceExit.Value;

        // Step 2: reconciliation — 실패하면 일반 전이를 거부한다.
        // ★ 순서 정정(§1): reconciliation이 existing-transition보다 먼저여야 가짜 id 공격을 막는다.
        ReconciliationResult reconResult;
        try { reconResult = HandoffIntegrityChecker.Run(new ReconciliationOptions(workstatePath, logPath)); }
        catch (Exception ex) { WriteError($"reconciliation 실행 실패: {ex.Message}"); return 2; }

        if (reconResult.Failures.Count > 0 || reconResult.HarnessErrors.Count > 0)
        {
            var codes = string.Join("; ", reconResult.Failures.Concat(reconResult.HarnessErrors).Select(f => f.Code));
            return Fail(1, envelope.TransitionId, "state-corrupted-preapply", $"reconciliation failed: {codes}");
        }

        // Step 3: existing-transition 판정 (idempotent / collision / legacy-unverifiable).
        // ★ 순서 정정(§1): existing-transition이 pre-state hash보다 먼저여야 재시도가 산다.
        var existingResult = CheckExistingTransition(workstate, reconResult, envelope);
        if (existingResult.HasValue) return existingResult.Value;

        // Steps 4-6: candidate 재계산 + 검증 + dry-run.
        var (recompCandidate, recompBytes, validateExit) =
            RunApplyValidatePhase(envelope, workstate, rawWorkstateStr, request!, dryRun, root);
        if (validateExit.HasValue) return validateExit.Value;

        // Steps 7-10: atomic write + post-apply 검사 + v2 log.
        return RunApplyCommitPhase(
            envelope, workstatePath, logPath,
            rawWorkstateBytes, workstate, recompCandidate!, recompBytes!);
    }

    // root를 받아 WORKSTATE를 로드한다. 실패 시 null 반환(오류는 stderr에 기록).
    private static (string root, string workstatePath, string logPath, byte[] rawBytes, JsonObject workstate, string rawStr)?
        LoadWorkstateContextFromRoot(string root)
    {
        try
        {
            var wsPath = Path.Combine(root, "docs", "handoff", "WORKSTATE.json");
            var lPath = Path.Combine(root, "docs", "handoff", "WORKSTATE.applier-log.jsonl");
            if (!File.Exists(wsPath)) { WriteError("WORKSTATE.json not found"); return null; }
            var bytes = File.ReadAllBytes(wsPath);
            var raw = Encoding.UTF8.GetString(bytes);
            var ws = JsonNode.Parse(raw)?.AsObject()
                ?? throw new InvalidOperationException("WORKSTATE.json is not a valid JSON object");
            return (root, wsPath, lPath, bytes, ws, raw);
        }
        catch (Exception ex) { WriteError(ex.Message); return null; }
    }

    // request sha256 + candidate file hash를 검증한다. 성공 시 (rawRequest, request, null) 반환.
    private static (string? rawRequest, JsonObject? request, int? exitCode) VerifyApplyEvidence(EnvelopeData envelope)
    {
        if (!File.Exists(envelope.RequestPath)) { WriteError($"request file not found: {envelope.RequestPath}"); return (null, null, 2); }
        string rawRequest;
        JsonObject request;
        try
        {
            rawRequest = File.ReadAllText(envelope.RequestPath, new UTF8Encoding(false));
            var actualRequestSha = ComputeSha256(rawRequest);
            if (!string.Equals(actualRequestSha, envelope.RequestSha256, StringComparison.OrdinalIgnoreCase))
                return (null, null, Fail(1, envelope.TransitionId, "request-sha256-mismatch",
                    "request file changed since prepare"));
            request = JsonNode.Parse(rawRequest)?.AsObject()
                ?? throw new InvalidOperationException("request JSON must be a JSON object");
        }
        catch (Exception ex) { WriteError(ex.Message); return (null, null, 2); }

        if (!File.Exists(envelope.CandidatePath)) { WriteError($"candidate file not found: {envelope.CandidatePath}"); return (null, null, 2); }
        try
        {
            var candidateFileHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(envelope.CandidatePath))).ToLowerInvariant();
            if (!string.Equals(candidateFileHash, envelope.ExpectedPostStateSha256, StringComparison.OrdinalIgnoreCase))
                return (null, null, Fail(1, envelope.TransitionId, "candidate-tampered",
                    "candidate file hash differs from envelope.expectedPostStateSha256 — evidence may have been tampered"));
        }
        catch (Exception ex) { WriteError($"candidate 파일 읽기 실패: {ex.Message}"); return (null, null, 2); }

        return (rawRequest, request, null);
    }

    // Steps 4-6: candidate 재계산 + hash 검증 + pre-state hash + request 검증 + dry-run 처리.
    private static (JsonObject? candidate, byte[]? bytes, int? earlyExit) RunApplyValidatePhase(
        EnvelopeData envelope, JsonObject workstate, string rawWorkstateStr,
        JsonObject request, bool dryRun, string root)
    {
        JsonObject recomputedCandidate;
        byte[] recomputedBytes;
        try
        {
            // Step 4: 메모리에서 candidate 재계산 + hash 검증 (TOCTOU 방어).
            recomputedCandidate = BuildCandidate(workstate, request, envelope.TransitionId, envelope.EffectiveAt);
            recomputedBytes = Encoding.UTF8.GetBytes(recomputedCandidate.ToJsonString(WriteOptions));
            var recomputedHash = Convert.ToHexString(SHA256.HashData(recomputedBytes)).ToLowerInvariant();
            if (!string.Equals(recomputedHash, envelope.ExpectedPostStateSha256, StringComparison.OrdinalIgnoreCase))
                return (null, null, Fail(1, envelope.TransitionId, "candidate-recompute-mismatch",
                    "recomputed candidate hash differs — WORKSTATE or request changed since prepare"));
        }
        catch (Exception ex) { WriteError($"candidate 재계산 실패: {ex.Message}"); return (null, null, 2); }

        // Step 5: pre-state hash 검증 (미적용 ID에 한해 실행).
        var currentHash = ComputeSha256(rawWorkstateStr);
        if (!string.Equals(currentHash, envelope.ExpectedPreStateSha256, StringComparison.OrdinalIgnoreCase))
            return (null, null, Fail(1, envelope.TransitionId, "pre-state-hash-mismatch",
                $"expected={Short(envelope.ExpectedPreStateSha256)} actual={Short(currentHash)}"));

        // Step 6: request 검증.
        var validErr = ValidateRequestForApply(request, envelope, workstate, root);
        if (validErr is not null)
            return (null, null, Fail(1, envelope.TransitionId, "validation-failed", validErr));

        if (dryRun)
        {
            Console.WriteLine(new JsonObject
            {
                ["ok"] = true, ["dryRun"] = true,
                ["transitionId"] = envelope.TransitionId,
                ["previousStatus"] = workstate["status"]?.ToString(),
                ["newStatus"] = recomputedCandidate["status"]?.ToString(),
            }.ToJsonString(WriteOptions));
            return (null, null, 0);
        }

        return (recomputedCandidate, recomputedBytes, null);
    }

    // Steps 7-10: preimage 저장 → atomic write → test seam → post-apply 검사 → v2 log append.
    private static int RunApplyCommitPhase(
        EnvelopeData envelope, string workstatePath, string logPath,
        byte[] rawWorkstateBytes, JsonObject workstate, JsonObject recomputedCandidate, byte[] recomputedBytes)
    {
        // Step 7: preimage 저장 — atomic replace 전에 반드시 저장해야 한다.
        var preimage = rawWorkstateBytes;
        var preimageHash = Convert.ToHexString(SHA256.HashData(preimage)).ToLowerInvariant();

        // Step 8: atomic replace — 재계산 bytes만 사용(candidate 파일은 evidence, write source 아님).
        try { var t = workstatePath + ".stateapplier.tmp"; File.WriteAllBytes(t, recomputedBytes); File.Move(t, workstatePath, overwrite: true); }
        catch (Exception ex) { WriteError($"atomic write 실패: {ex.Message}"); return 2; }

        // 결정적 test seam — in-process 훅만 허용. 환경변수는 읽지 않는다.
        var seamFail = FailAfterWriteHook?.Invoke();
        if (seamFail is not null)
            return Rollback(workstatePath, logPath, preimage, preimageHash, envelope, seamFail);

        // Step 9: 적용후 검사 — 항상 projection 실행.
        var projExit = RunProjection();
        if (projExit != 0)
            return Rollback(workstatePath, logPath, preimage, preimageHash, envelope, "projection-failed");

        // 내부 checker — PendingTransitionId로 pending 면제 경로를 실증한다.
        ReconciliationResult reconAfter;
        try { reconAfter = HandoffIntegrityChecker.Run(new ReconciliationOptions(workstatePath, logPath, envelope.TransitionId)); }
        catch (Exception ex)
        {
            return Rollback(workstatePath, logPath, preimage, preimageHash, envelope,
                $"post-apply checker 실행 실패: {ex.Message}");
        }

        if (reconAfter.Failures.Count > 0 || reconAfter.HarnessErrors.Count > 0)
        {
            var codes = string.Join("; ", reconAfter.Failures.Concat(reconAfter.HarnessErrors).Select(f => f.Code));
            return Rollback(workstatePath, logPath, preimage, preimageHash, envelope,
                $"post-apply-integrity-failed: {codes}");
        }

        // Step 10: v2 log append — 성공 경로에서만.
        string newSha;
        try { newSha = ComputeSha256(File.ReadAllText(workstatePath, new UTF8Encoding(false))); }
        catch (Exception ex) { WriteError($"적용 후 hash 계산 실패: {ex.Message}"); return 2; }

        var logContractHash = ComputeContractHash(
            envelope.TransitionId, envelope.TransitionKind, envelope.RequestSha256,
            envelope.ExpectedPreStateSha256, newSha, envelope.EffectiveAt);
        try
        {
            AppendApplierLogV2(logPath, envelope.TransitionId, "ok", 0, envelope.TransitionKind,
                envelope.RequestSha256, envelope.ExpectedPreStateSha256, newSha, logContractHash, envelope.EffectiveAt);
        }
        catch (Exception logEx)
        {
            Console.Error.WriteLine(new JsonObject
            {
                ["fatal"] = "AUDIT_LOG_STATE_UNKNOWN", ["transitionId"] = envelope.TransitionId,
                ["detail"] = $"v2 log append threw: {logEx.Message} — 다음 전이 금지, log 수동 검증 필요",
            }.ToJsonString());
            return 2;
        }

        Console.WriteLine(new JsonObject
        {
            ["ok"] = true, ["transitionId"] = envelope.TransitionId,
            ["previousStatus"] = workstate["status"]?.ToString(),
            ["newStatus"] = recomputedCandidate["status"]?.ToString(),
            ["workstateSha256"] = newSha,
            ["pendingExemptionUsed"] = reconAfter.Metrics?.PendingExemptionApplied,
        }.ToJsonString(WriteOptions));
        return 0;
    }

    // rollback: preimage를 복원하고 FATAL taxonomy에 따라 exit code와 로그를 결정한다.
    private static int Rollback(
        string workstatePath, string logPath, byte[] preimage, string preimageHash,
        EnvelopeData envelope, string failureReason)
    {
        try
        {
            // 결정적 복원 실패 seam — 자기 시험에서 FATAL_STATE_UNKNOWN 경로를 검증한다.
            if (FailRestoreForTest) throw new InvalidOperationException("self-test injected restore failure");
            File.WriteAllBytes(workstatePath, preimage);
            var restoredStr = File.ReadAllText(workstatePath, new UTF8Encoding(false));
            var restoredHash = ComputeSha256(restoredStr);

            if (!string.Equals(restoredHash, preimageHash, StringComparison.OrdinalIgnoreCase))
            {
                // 복원 후 hash 불일치 — FATAL.
                AppendLogSafe(logPath, envelope.TransitionId, "FATAL_STATE_UNKNOWN", 2);
                Console.Error.WriteLine(new JsonObject
                {
                    ["fatal"] = "FATAL_STATE_UNKNOWN", ["transitionId"] = envelope.TransitionId,
                    ["detail"] = "preimage 복원 hash 불일치 — 모든 자동작업 중단. HUMAN-INBOX 필요.",
                }.ToJsonString());
                return 2;
            }

            // rollback 후 항상 projection 재생성 시도.
            var projExit = RunProjection();
            if (projExit != 0)
            {
                AppendLogSafe(logPath, envelope.TransitionId, "STATE_RESTORED_PROJECTION_NOT_VERIFIED", 2);
                Console.Error.WriteLine(new JsonObject
                {
                    ["fatal"] = "STATE_RESTORED_PROJECTION_NOT_VERIFIED",
                    ["transitionId"] = envelope.TransitionId,
                    ["detail"] = "WORKSTATE 복원 성공, projection 재생성 실패 — HUMAN-INBOX 필요.",
                }.ToJsonString());
                return 2;
            }

            AppendLogSafe(logPath, envelope.TransitionId, "ROLLED_BACK", 1);
            Console.Error.WriteLine(new JsonObject
            {
                ["ok"] = false, ["result"] = "ROLLED_BACK",
                ["transitionId"] = envelope.TransitionId,
                ["failureReason"] = failureReason,
                ["restoredHash"] = restoredHash,
                ["detail"] = "WORKSTATE 복원 완료. 전이 효과 없음.",
            }.ToJsonString());
            return 1;
        }
        catch (Exception ex)
        {
            // 복원 자체 실패 — FATAL.
            AppendLogSafe(logPath, envelope.TransitionId, "FATAL_STATE_UNKNOWN", 2);
            Console.Error.WriteLine(new JsonObject
            {
                ["fatal"] = "FATAL_STATE_UNKNOWN", ["transitionId"] = envelope.TransitionId,
                ["detail"] = $"preimage 복원 실패: {ex.Message} — 모든 자동작업 중단.",
            }.ToJsonString());
            return 2;
        }
    }

    // envelope JSON을 읽어 EnvelopeData로 파싱한다. 실패 시 null 반환.
    private static EnvelopeData? ReadEnvelope(string envelopePath)
    {
        if (!File.Exists(envelopePath)) { WriteError($"envelope file not found: {envelopePath}"); return null; }
        JsonObject env;
        try
        {
            env = JsonNode.Parse(File.ReadAllText(envelopePath, new UTF8Encoding(false)))?.AsObject()
                ?? throw new InvalidOperationException("envelope is not a JSON object");
        }
        catch (Exception ex) { WriteError($"envelope parse 실패: {ex.Message}"); return null; }

        var kind = env["transitionKind"]?.GetValue<string>() ?? "NORMAL";
        var tid = env["transitionId"]?.GetValue<string>() ?? "";
        if (string.IsNullOrWhiteSpace(tid)) { WriteError("envelope.transitionId missing"); return null; }

        var preHash = env["expectedPreStateSha256"]?.GetValue<string>() ?? "";
        var reqPath = env["requestPath"]?.GetValue<string>() ?? "";
        var reqSha = env["requestSha256"]?.GetValue<string>() ?? "";
        var effAt = env["effectiveAt"]?.GetValue<string>() ?? "";
        var postHash = env["expectedPostStateSha256"]?.GetValue<string>() ?? "";
        var candidatePath = env["candidatePath"]?.GetValue<string>() ?? "";
        var contractSha = env["transitionContractSha256"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(preHash) || string.IsNullOrWhiteSpace(reqPath) ||
            string.IsNullOrWhiteSpace(reqSha) || string.IsNullOrWhiteSpace(effAt) ||
            string.IsNullOrWhiteSpace(postHash) || string.IsNullOrWhiteSpace(candidatePath))
        {
            WriteError("envelope 필수 필드 누락 (transitionId/expectedPreStateSha256/requestPath/requestSha256/effectiveAt/expectedPostStateSha256/candidatePath)");
            return null;
        }

        var sv = env["schemaVersion"]?.GetValue<int>() ?? 1;
        return new EnvelopeData(sv, kind, tid, preHash, reqPath, reqSha, effAt, postHash, contractSha, candidatePath);
    }

    // 전이 ID가 이미 state에 있는지 판정하고 idempotent/collision/envelope-mismatch/legacy-unverifiable exit를 반환한다.
    private static int? CheckExistingTransition(
        JsonObject workstate, ReconciliationResult reconResult, EnvelopeData envelope)
    {
        var isInState = workstate["appliedTransitions"] is JsonArray applied &&
            applied.OfType<JsonObject>().Any(o =>
                string.Equals(o["id"]?.ToString(), envelope.TransitionId, StringComparison.Ordinal));

        if (!isInState)
        {
            // 신규 전이 — self-reported contract hash가 재계산값과 일치하는지 검증 (envelope 자기신고 불신).
            if (!string.IsNullOrWhiteSpace(envelope.TransitionContractSha256))
            {
                var newEnvelopeHash = ComputeContractHash(
                    envelope.TransitionId, envelope.TransitionKind, envelope.RequestSha256,
                    envelope.ExpectedPreStateSha256, envelope.ExpectedPostStateSha256, envelope.EffectiveAt);
                if (!string.Equals(envelope.TransitionContractSha256, newEnvelopeHash, StringComparison.OrdinalIgnoreCase))
                    return Fail(1, envelope.TransitionId, "envelope-contract-mismatch",
                        "envelope.transitionContractSha256이 계산값과 다릅니다 — 위조 또는 수정됨");
            }
            return null; // 정상 경로로 진행.
        }

        // state에 있음 — log 조회.
        reconResult.SuccessLookup.TryGetValue(envelope.TransitionId, out var logInfo);
        if (logInfo is null || !logInfo.Exists)
        {
            // reconciliation이 PASS했으므로 state+log 불일치는 없어야 함 — 방어 코드.
            return Fail(1, envelope.TransitionId, "state-corrupted-preapply",
                $"id '{envelope.TransitionId}' in state but not in success log");
        }

        if (logInfo.SchemaVersion < 2 || string.IsNullOrWhiteSpace(logInfo.TransitionContractSha256))
        {
            // v1 log — 내용 결속 검증 불가.
            return Fail(1, envelope.TransitionId, "legacy-idempotency-unverifiable",
                $"id '{envelope.TransitionId}'는 v1 log에 있음 — contract binding 검증 불가");
        }

        // v2 log — envelope 구성 필드로부터 contract hash를 재계산해 비교한다 (envelope 자기신고 불신).
        var computed = ComputeContractHash(
            envelope.TransitionId, envelope.TransitionKind, envelope.RequestSha256,
            envelope.ExpectedPreStateSha256, envelope.ExpectedPostStateSha256, envelope.EffectiveAt);

        // 재계산값이 log hash와 다르면 — 다른 계약의 transition-id가 충돌함.
        if (!string.Equals(computed, logInfo.TransitionContractSha256, StringComparison.OrdinalIgnoreCase))
        {
            return Fail(1, envelope.TransitionId, "transition-id-collision",
                $"id '{envelope.TransitionId}'는 다른 contract hash로 이미 적용됨");
        }

        // envelope 자기신고 hash가 계산값과 다르면 — envelope 위조 탐지.
        if (!string.IsNullOrWhiteSpace(envelope.TransitionContractSha256) &&
            !string.Equals(envelope.TransitionContractSha256, computed, StringComparison.OrdinalIgnoreCase))
        {
            return Fail(1, envelope.TransitionId, "envelope-contract-mismatch",
                $"envelope.transitionContractSha256이 계산값과 다릅니다 — 위조 또는 수정됨");
        }

        // 동일 contract — idempotent.
        return ReportIdempotent(envelope.TransitionId);
    }

    // effectiveAt을 외부에서 받아 결정적 candidate를 구성한다. UtcNow 호출 없음.
    private static JsonObject BuildCandidate(
        JsonObject current, JsonObject request, string transitionId, string effectiveAt)
    {
        var candidate = JsonNode.Parse(current.ToJsonString(WriteOptions))!.AsObject()!;

        foreach (var key in new[] { "phaseId", "wpId", "diId", "status", "blockers", "nextActions", "updatedBy", "notes" })
        {
            if (request[key] is JsonNode node)
                candidate[key] = node.DeepClone();
        }

        // updatedAt은 effectiveAt의 날짜 부분으로 결정한다 — UtcNow 호출 없음.
        if (DateTimeOffset.TryParse(effectiveAt, null, DateTimeStyles.RoundtripKind, out var ea))
            candidate["updatedAt"] = ea.ToString("yyyy-MM-dd");

        if (candidate["appliedTransitions"] is not JsonArray applied)
        {
            applied = new JsonArray();
            candidate["appliedTransitions"] = applied;
        }

        applied.Add(new JsonObject
        {
            ["id"] = transitionId,
            ["appliedAt"] = effectiveAt,
        });

        return candidate;
    }

    // apply 경로용 request 검증 — human-decision 플래그 없이 동작한다.
    private static string? ValidateRequestForApply(
        JsonObject request, EnvelopeData envelope, JsonObject current, string root)
    {
        if (request["phaseId"] is JsonNode ph && !Regex.IsMatch(ph.ToString(), @"^P\d{2,}$"))
            return $"phaseId '{ph}'는 canonical 형식(P00, P01 …)이 아닙니다";
        if (request["wpId"] is JsonNode wp && !Regex.IsMatch(wp.ToString(), @"^WP-\d{2,}$"))
            return $"wpId '{wp}'는 canonical 형식(WP-00 …)이 아닙니다";
        if (request["diId"] is JsonNode di && !Regex.IsMatch(di.ToString(), @"^DI-\d{2}-\d{2,}$"))
            return $"diId '{di}'는 canonical 형식(DI-00-05 …)이 아닙니다";

        var newStatus = request["status"]?.ToString() ?? current["status"]?.ToString() ?? "";
        if (!ValidStatuses.Contains(newStatus))
            return $"status '{newStatus}'는 허용 목록에 없습니다";

        var currentStatus = current["status"]?.ToString() ?? "";
        var currentDiId = current["diId"]?.ToString() ?? "";
        var requestDiId = request["diId"]?.ToString();

        var transErr = ValidateStatusTransition(currentStatus, newStatus, currentDiId, requestDiId);
        if (transErr is not null) return transErr;

        var newBlockers = request["blockers"] as JsonArray ?? current["blockers"] as JsonArray ?? new JsonArray();
        var newNext = request["nextActions"] as JsonArray ?? current["nextActions"] as JsonArray ?? new JsonArray();

        if (string.Equals(newStatus, "blocked", StringComparison.OrdinalIgnoreCase) && newBlockers.Count == 0)
            return "status=blocked인데 blockers가 비어있습니다";
        if (NextActionsRequired.Contains(newStatus) && newNext.Count == 0)
            return $"status={newStatus}인데 nextActions가 비어있습니다";

        if (string.Equals(newStatus, "completed", StringComparison.OrdinalIgnoreCase))
        {
            var targetDiId = requestDiId ?? currentDiId;
            var wsUpdatedAt = current["updatedAt"]?.ToString() ?? "";
            var verdictErr = ValidateVerdict(null, targetDiId, wsUpdatedAt);
            if (verdictErr is not null) return verdictErr;
        }

        var curPhase = current["phaseId"]?.ToString();
        var newPhase = request["phaseId"]?.ToString();
        if (newPhase is not null && !string.Equals(newPhase, curPhase, StringComparison.OrdinalIgnoreCase))
            return $"Phase 전이({curPhase} → {newPhase})는 transitionKind=PHASE_CHANGE를 사용하세요 (이번 WP fail-closed)";

        if (!string.IsNullOrWhiteSpace(root))
        {
            var wpId = request["wpId"]?.ToString() ?? current["wpId"]?.ToString();
            if (!string.IsNullOrWhiteSpace(wpId))
            {
                var regErr = ValidateWpRegistry(root, wpId);
                if (regErr is not null) return regErr;
            }
        }
        return null;
    }

    // DI 경계와 전이 그래프를 검사한다.
    private static string? ValidateStatusTransition(
        string currentStatus, string newStatus, string currentDiId, string? requestDiId)
    {
        var isNewDi = requestDiId is not null &&
                      !string.Equals(requestDiId, currentDiId, StringComparison.OrdinalIgnoreCase);

        if (isNewDi)
        {
            if (!string.Equals(currentStatus, "completed", StringComparison.OrdinalIgnoreCase))
                return $"새 DI 착수({currentDiId} → {requestDiId})는 현재 DI가 completed여야 합니다(현재: {currentStatus})";
            if (!string.Equals(newStatus, "waiting", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(newStatus, "in_progress", StringComparison.OrdinalIgnoreCase))
                return $"새 DI 착수 시 status는 waiting 또는 in_progress만 허용합니다(요청: {newStatus})";
            return null;
        }

        if (string.Equals(currentStatus, newStatus, StringComparison.OrdinalIgnoreCase)) return null;

        // completed는 terminal.
        if (string.Equals(currentStatus, "completed", StringComparison.OrdinalIgnoreCase))
            return $"completed 상태 전이({currentStatus} → {newStatus})는 허용되지 않습니다 — completed는 terminal 상태입니다";

        if (AllowedTransitions.TryGetValue(currentStatus, out var allowed) && !allowed.Contains(newStatus))
        {
            var allowedStr = allowed.Count > 0 ? string.Join(", ", allowed) : "없음(terminal)";
            return $"status 전이 {currentStatus} → {newStatus}는 허용되지 않습니다(허용: {allowedStr})";
        }
        return null;
    }

    // gate.json 형식·gateVerdict·failureCount·taskId 일치·stale 여부를 검증한다.
    private static string? ValidateVerdict(string? verdictPath, string targetDiId, string workstateUpdatedAt)
    {
        if (string.IsNullOrWhiteSpace(verdictPath))
            return "status=completed 전이에는 --verdict 파일이 필요합니다";

        var normalized = verdictPath.Replace('\\', '/');
        var fileName = Path.GetFileName(normalized);
        if (!fileName.EndsWith(".gate.json", StringComparison.OrdinalIgnoreCase))
            return $"verdict 경로는 outputs/gates/<taskId>.gate.json 형식이어야 합니다(받음: {verdictPath})";

        var dirPart = Path.GetDirectoryName(normalized)?.Replace('\\', '/') ?? "";
        if (!dirPart.EndsWith("outputs/gates", StringComparison.OrdinalIgnoreCase))
            return $"verdict 경로는 outputs/gates/<taskId>.gate.json 형식이어야 합니다(받음: {verdictPath})";

        if (!File.Exists(verdictPath)) return $"verdict file not found: {verdictPath}";

        var gateJson = JsonNode.Parse(File.ReadAllText(verdictPath))?.AsObject();
        if (gateJson is null) return "gate JSON이 유효하지 않습니다";

        var gateVerdict = gateJson["gateVerdict"]?.ToString();
        if (!string.Equals(gateVerdict, "PASS", StringComparison.OrdinalIgnoreCase))
            return $"gate verdict가 PASS가 아닙니다(gateVerdict={gateVerdict ?? "null"})";

        var failureCount = gateJson["failureCount"]?.GetValue<int>() ?? -1;
        if (failureCount != 0) return $"gate failureCount가 0이 아닙니다(failureCount={failureCount})";

        var gateTaskId = gateJson["taskId"]?.ToString();
        if (!string.Equals(gateTaskId, targetDiId, StringComparison.OrdinalIgnoreCase))
            return $"gate taskId({gateTaskId ?? "null"})가 전이 대상 DI({targetDiId})와 일치하지 않습니다";

        var createdAtStr = gateJson["createdAt"]?.ToString();
        if (createdAtStr is not null &&
            DateTimeOffset.TryParse(createdAtStr, out var createdAt) &&
            DateTime.TryParse(workstateUpdatedAt, out var wsUpdated) &&
            createdAt.Date < wsUpdated.Date)
        {
            return $"gate 증거가 WORKSTATE updatedAt({workstateUpdatedAt})보다 오래됐습니다(gate createdAt={createdAt:yyyy-MM-dd})";
        }
        return null;
    }

    // wpId가 WP-REGISTRY.json에 등록되어 있는지 검사한다.
    private static string? ValidateWpRegistry(string root, string wpId)
    {
        var registryPath = Path.Combine(root, "docs", "handoff", "WP-REGISTRY.json");
        if (!File.Exists(registryPath)) return "WP-REGISTRY.json not found — WP 등록표가 없습니다";
        var reg = JsonNode.Parse(File.ReadAllText(registryPath))?.AsObject();
        if (reg is null) return "WP-REGISTRY.json이 유효한 JSON 객체가 아닙니다";
        var wps = reg["wps"] as JsonArray;
        if (wps is null) return "WP-REGISTRY.json에 'wps' 배열이 없습니다";
        var exists = wps.OfType<JsonObject>().Any(w =>
            string.Equals(w["wpId"]?.ToString(), wpId, StringComparison.OrdinalIgnoreCase));
        return exists ? null : $"wpId '{wpId}'가 WP-REGISTRY.json에 등록되지 않았습니다 — WP 추가는 사람 결재입니다";
    }

    // 고정 순서 JSON 직렬화로 계약 hash를 계산한다.
    private static string ComputeContractHash(
        string transitionId, string transitionKind, string requestSha256,
        string preStateSha256, string postStateSha256, string effectiveAt)
    {
        var sb = new StringBuilder();
        sb.Append("{\"effectiveAt\":\"").Append(effectiveAt).Append('"');
        sb.Append(",\"postStateSha256\":\"").Append(postStateSha256).Append('"');
        sb.Append(",\"preStateSha256\":\"").Append(preStateSha256).Append('"');
        sb.Append(",\"requestSha256\":\"").Append(requestSha256).Append('"');
        sb.Append(",\"transitionId\":\"").Append(transitionId).Append('"');
        sb.Append(",\"transitionKind\":\"").Append(transitionKind).Append("\"}");
        return ComputeSha256(sb.ToString());
    }

    // v2 format으로 applier-log에 항목을 추가한다.
    private static void AppendApplierLogV2(
        string logPath, string transitionId, string result, int exitCode,
        string transitionKind, string requestSha256, string preStateSha256,
        string postStateSha256, string contractSha256, string effectiveAt)
    {
        var entry = new JsonObject
        {
            ["transitionId"] = transitionId,
            ["result"] = result,
            ["exitCode"] = exitCode,
            ["at"] = DateTime.UtcNow.ToString("o"),
            ["schemaVersion"] = 2,
            ["transitionKind"] = transitionKind,
            ["requestSha256"] = requestSha256,
            ["preStateSha256"] = preStateSha256,
            ["postStateSha256"] = postStateSha256,
            ["transitionContractSha256"] = contractSha256,
            ["effectiveAt"] = effectiveAt,
        };
        File.AppendAllText(logPath, entry.ToJsonString() + "\n", new UTF8Encoding(false));
    }

    // rollback/error 이벤트를 applier-log에 한 줄 추가한다.
    private static void AppendApplierLog(string logPath, string transitionId, string result, int exitCode)
    {
        var entry = new JsonObject
        {
            ["transitionId"] = transitionId,
            ["result"] = result,
            ["exitCode"] = exitCode,
            ["at"] = DateTime.UtcNow.ToString("o"),
        };
        File.AppendAllText(logPath, entry.ToJsonString() + "\n", new UTF8Encoding(false));
    }

    // 예외를 삼키고 log append를 시도한다 — rollback 경로의 best-effort 로깅.
    private static void AppendLogSafe(string logPath, string transitionId, string result, int exitCode)
    {
        try { AppendApplierLog(logPath, transitionId, result, exitCode); } catch { }
    }

    // 이미 적용된 transition-id에 대한 idempotent 응답을 출력하고 0을 반환한다.
    private static int ReportIdempotent(string transitionId)
    {
        Console.WriteLine(new JsonObject
        {
            ["ok"] = true,
            ["transitionId"] = transitionId,
            ["idempotent"] = true,
            ["note"] = "이 transition-id는 이미 적용됐다 — 상태를 바꾸지 않는다",
        }.ToJsonString(WriteOptions));
        return 0;
    }

    // prepare 인수를 파싱한다.
    private static PrepareOptions? ParsePrepareArgs(string[] args)
    {
        var map = ParseFlagMap(args, 2);
        var dryRun = map.ContainsKey("dry-run-flag"); map.Remove("dry-run-flag");
        if (!map.TryGetValue("transition-id", out var tid) || string.IsNullOrWhiteSpace(tid)) return null;
        if (!map.TryGetValue("request", out var req) || string.IsNullOrWhiteSpace(req)) return null;
        return new PrepareOptions(tid, req, dryRun);
    }

    // apply 인수를 파싱한다.
    private static ApplyOptions? ParseApplyArgs(string[] args)
    {
        var map = ParseFlagMap(args, 2);
        var dryRun = map.ContainsKey("dry-run-flag"); map.Remove("dry-run-flag");
        if (!map.TryGetValue("envelope", out var env) || string.IsNullOrWhiteSpace(env)) return null;
        map.TryGetValue("verdict", out var verdict);
        return new ApplyOptions(env, verdict, dryRun);
    }

    // args[startIndex]부터 --key value 쌍을 파싱한다. --dry-run은 "dry-run-flag"="1"로 저장.
    private static Dictionary<string, string> ParseFlagMap(string[] args, int startIndex)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = startIndex; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--dry-run", StringComparison.OrdinalIgnoreCase))
            {
                map["dry-run-flag"] = "1";
                continue;
            }
            if (args[i].StartsWith("--", StringComparison.Ordinal) && i + 1 < args.Length)
            {
                map[args[i][2..]] = args[i + 1];
                i++;
            }
        }
        return map;
    }

    // 알 수 없거나 삭제된 옵션을 검사해 오류 메시지를 반환한다. 없으면 null.
    private static string? ValidateOptions(Dictionary<string, string> map, HashSet<string> knownKeys)
    {
        foreach (var key in map.Keys)
        {
            if (knownKeys.Contains(key)) continue;
            if (RemovedOptions.TryGetValue(key, out var msg)) return msg;
            return $"unknown-option: --{key}";
        }
        return null;
    }

    // ProjectionOverride가 있으면 그것을, 없으면 ProjectionCli를 실행한다.
    private static int RunProjection()
        => ProjectionOverride is not null ? ProjectionOverride() : ProjectionCli.Run(["projection"]);

    // 문자열의 SHA-256을 소문자 hex로 반환한다.
    private static string ComputeSha256(string content)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    // stderr에 JSON 오류 메시지를 출력한다.
    private static void WriteError(string message)
        => Console.Error.WriteLine($"{{\"error\":\"{message}\"}}");

    // 실패 결과를 stderr에 출력하고 지정 exit code를 반환한다.
    private static int Fail(int exitCode, string transitionId, string reason, string detail)
    {
        Console.Error.WriteLine(new JsonObject
        {
            ["ok"] = false,
            ["transitionId"] = transitionId,
            ["reason"] = reason,
            ["detail"] = detail,
        }.ToJsonString());
        return exitCode;
    }

    // sha256 문자열의 앞 16자 + "..."를 반환한다.
    private static string Short(string s) => s.Length > 16 ? s[..16] + "..." : s;

    // state-transition --self-test: rollback·정상·FATAL 경로를 in-process로 단언한다.
    private static int RunSelfTest()
    {
        var tmpBase = Path.Combine(Path.GetTempPath(), $"st-selftest-{Guid.NewGuid():N}");
        try { return RunSelfTestInDir(tmpBase); }
        finally
        {
            try { if (Directory.Exists(tmpBase)) Directory.Delete(tmpBase, recursive: true); } catch { }
        }
    }

    // 임시 저장소를 만들고 4개 case를 순서대로 실행한다.
    private static int RunSelfTestInDir(string tmpRoot)
    {
        var wsDir = Path.Combine(tmpRoot, "docs", "handoff");
        var outDir = Path.Combine(tmpRoot, "outputs", "state-transition");
        Directory.CreateDirectory(wsDir);
        Directory.CreateDirectory(outDir);
        Directory.CreateDirectory(Path.Combine(tmpRoot, ".git")); // GitTools.FindRepoRoot 기준점
        var wsPath = Path.Combine(wsDir, "WORKSTATE.json");
        var logPath = Path.Combine(wsDir, "WORKSTATE.applier-log.jsonl");
        var reqPath = Path.Combine(tmpRoot, "req.json");
        var wsContent = "{\"schemaVersion\":1,\"diId\":\"DI-00-04\",\"status\":\"in_progress\","
            + "\"blockers\":[],\"nextActions\":[\"test\"],\"phaseId\":\"P00\",\"wpId\":\"WP-00\","
            + "\"updatedAt\":\"2026-07-14\",\"updatedBy\":\"self-test\",\"appliedTransitions\":[]}";
        File.WriteAllText(wsPath, wsContent, new UTF8Encoding(false));
        File.WriteAllText(logPath, "", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(wsDir, "WP-REGISTRY.json"),
            "{\"wps\":[{\"wpId\":\"WP-00\",\"title\":\"Self Test\"}]}", new UTF8Encoding(false));
        File.WriteAllText(reqPath,
            "{\"status\":\"in_progress\",\"nextActions\":[\"test\"],\"phaseId\":\"P00\","
            + "\"wpId\":\"WP-00\",\"diId\":\"DI-00-04\",\"updatedBy\":\"self-test\"}",
            new UTF8Encoding(false));
        var mismatches = new JsonArray();
        var caseResults = new JsonArray();
        RunCaseNormalApply(tmpRoot, reqPath, wsPath, wsContent, logPath, outDir, caseResults, mismatches);
        RunCaseRollbackAfterWrite(tmpRoot, reqPath, wsPath, wsContent, logPath, outDir, caseResults, mismatches);
        RunCaseRollbackRestoresLog(logPath, caseResults, mismatches);
        RunCaseFatalRestoreFailed(tmpRoot, reqPath, wsPath, wsContent, logPath, outDir, caseResults, mismatches);
        ProjectionOverride = null;
        if (mismatches.Count == 0)
        {
            Console.WriteLine(new JsonObject { ["selfTest"] = "state-transition", ["verdict"] = "PASS",
                ["casesRun"] = caseResults.Count, ["cases"] = caseResults }.ToJsonString(WriteOptions));
            return 0;
        }
        Console.Error.WriteLine(new JsonObject { ["selfTest"] = "state-transition", ["verdict"] = "FAIL",
            ["mismatchCount"] = mismatches.Count, ["mismatches"] = mismatches,
            ["cases"] = caseResults }.ToJsonString(WriteOptions));
        return 1;
    }

    // case: normal-apply — 주입 없음, exit 0, v2 log 기록, WS 변경.
    private static void RunCaseNormalApply(string tmpRoot, string reqPath, string wsPath, string wsContent,
        string logPath, string outDir, JsonArray caseResults, JsonArray mismatches)
    {
        RestoreFixture(wsPath, wsContent, logPath);
        ProjectionOverride = () => 0; FailAfterWriteHook = null; FailRestoreForTest = false;
        var prepExit = RunPrepareCore(tmpRoot, new PrepareOptions("ST-NORMAL", reqPath, false));
        var preimageHash = ComputeSha256(File.ReadAllText(wsPath, new UTF8Encoding(false)));
        var env = ReadEnvelope(Path.Combine(outDir, "ST-NORMAL.envelope.json"));
        int? applyExit = null;
        if (env is not null && prepExit == 0)
        {
            var ctx = LoadWorkstateContextFromRoot(tmpRoot);
            if (ctx is not null) applyExit = ApplyEnvelopeCore(ctx.Value, env, false);
        }
        var logLines = File.Exists(logPath) ? File.ReadAllLines(logPath) : [];
        var okLogged = logLines.Any(l => l.Contains("\"ok\"") && l.Contains("ST-NORMAL"));
        var wsMoved = !string.Equals(ComputeSha256(File.ReadAllText(wsPath, new UTF8Encoding(false))), preimageHash);
        var ok = applyExit == 0 && okLogged && wsMoved;
        caseResults.Add(new JsonObject { ["case"] = "normal-apply", ["exit"] = applyExit, ["v2LogWritten"] = okLogged, ["wsChanged"] = wsMoved, ["pass"] = ok });
        if (!ok) mismatches.Add(new JsonObject { ["case"] = "normal-apply", ["expected"] = "exit=0, v2-log-ok, ws-changed", ["actual"] = $"exit={applyExit},log={okLogged},ws={wsMoved}" });
    }

    // case: rollback-after-write — 쓰기 직후 실패 주입, exit 1 ROLLED_BACK, hash==preimage, v2 ok 미기록.
    private static void RunCaseRollbackAfterWrite(string tmpRoot, string reqPath, string wsPath, string wsContent,
        string logPath, string outDir, JsonArray caseResults, JsonArray mismatches)
    {
        RestoreFixture(wsPath, wsContent, logPath);
        ProjectionOverride = () => 0; FailAfterWriteHook = () => "self-test-failure"; FailRestoreForTest = false;
        var prepExit = RunPrepareCore(tmpRoot, new PrepareOptions("ST-ROLLBACK", reqPath, false));
        var preimageHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(wsPath))).ToLowerInvariant();
        var env = ReadEnvelope(Path.Combine(outDir, "ST-ROLLBACK.envelope.json"));
        int? applyExit = null;
        if (env is not null && prepExit == 0)
        {
            var ctx = LoadWorkstateContextFromRoot(tmpRoot);
            if (ctx is not null) applyExit = ApplyEnvelopeCore(ctx.Value, env, false);
        }
        FailAfterWriteHook = null;
        var restoredHash = ComputeSha256(File.ReadAllText(wsPath, new UTF8Encoding(false)));
        var logLines = File.Exists(logPath) ? File.ReadAllLines(logPath) : [];
        var rolledBack = logLines.Any(l => l.Contains("ROLLED_BACK") && l.Contains("ST-ROLLBACK"));
        var noOkLog = !logLines.Any(l => l.Contains("\"ok\"") && l.Contains("ST-ROLLBACK"));
        var hashRestored = string.Equals(restoredHash, preimageHash, StringComparison.OrdinalIgnoreCase);
        var ok = applyExit == 1 && rolledBack && noOkLog && hashRestored;
        caseResults.Add(new JsonObject { ["case"] = "rollback-after-write", ["exit"] = applyExit, ["rolledBack"] = rolledBack, ["noOkLog"] = noOkLog, ["hashRestored"] = hashRestored, ["pass"] = ok });
        if (!ok) mismatches.Add(new JsonObject { ["case"] = "rollback-after-write", ["expected"] = "exit=1,ROLLED_BACK,no-ok-log,hash==preimage", ["actual"] = $"exit={applyExit},rolledBack={rolledBack},noOkLog={noOkLog},hashRestored={hashRestored}" });
    }

    // case: rollback-restores-log — rollback 후 v2 ok 항목이 없음을 재확인한다.
    private static void RunCaseRollbackRestoresLog(string logPath, JsonArray caseResults, JsonArray mismatches)
    {
        var logLines = File.Exists(logPath) ? File.ReadAllLines(logPath) : [];
        var noV2OkEntry = !logLines.Any(l => l.Contains("\"result\":\"ok\"") && l.Contains("ST-ROLLBACK"));
        caseResults.Add(new JsonObject { ["case"] = "rollback-restores-log", ["noV2OkEntry"] = noV2OkEntry, ["pass"] = noV2OkEntry });
        if (!noV2OkEntry) mismatches.Add(new JsonObject { ["case"] = "rollback-restores-log", ["expected"] = "v2-ok-log-not-written", ["actual"] = "v2-ok-log-was-written" });
    }

    // case: fatal-restore-failed — 복원 자체를 실패시켜 exit 2 FATAL_STATE_UNKNOWN을 검증한다.
    private static void RunCaseFatalRestoreFailed(string tmpRoot, string reqPath, string wsPath, string wsContent,
        string logPath, string outDir, JsonArray caseResults, JsonArray mismatches)
    {
        RestoreFixture(wsPath, wsContent, logPath);
        ProjectionOverride = () => 0; FailAfterWriteHook = () => "self-test-fatal"; FailRestoreForTest = true;
        var prepExit = RunPrepareCore(tmpRoot, new PrepareOptions("ST-FATAL", reqPath, false));
        var env = ReadEnvelope(Path.Combine(outDir, "ST-FATAL.envelope.json"));
        int? applyExit = null;
        if (env is not null && prepExit == 0)
        {
            var ctx = LoadWorkstateContextFromRoot(tmpRoot);
            if (ctx is not null) applyExit = ApplyEnvelopeCore(ctx.Value, env, false);
        }
        FailAfterWriteHook = null; FailRestoreForTest = false;
        var logLines = File.Exists(logPath) ? File.ReadAllLines(logPath) : [];
        var fatalLogged = logLines.Any(l => l.Contains("FATAL_STATE_UNKNOWN") && l.Contains("ST-FATAL"));
        var ok = applyExit == 2 && fatalLogged;
        caseResults.Add(new JsonObject { ["case"] = "fatal-restore-failed", ["exit"] = applyExit, ["fatalLogged"] = fatalLogged, ["pass"] = ok });
        if (!ok) mismatches.Add(new JsonObject { ["case"] = "fatal-restore-failed", ["expected"] = "exit=2,FATAL_STATE_UNKNOWN", ["actual"] = $"exit={applyExit},fatal={fatalLogged}" });
    }

    // 자기 시험용 fixture 초기화 — WORKSTATE를 preimage로 되돌리고 log를 비운다.
    private static void RestoreFixture(string wsPath, string wsContent, string logPath)
    {
        File.WriteAllText(wsPath, wsContent, new UTF8Encoding(false));
        File.WriteAllText(logPath, "", new UTF8Encoding(false));
    }
}
