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
    private record PrepareOptions(string TransitionId, string RequestPath, string? Root, bool DryRun);

    // apply 부속 명령 옵션.
    private record ApplyOptions(string EnvelopePath, string? VerdictPath, string? Root, bool DryRun);

    // 이전 단일-샷 CLI 옵션 (backward compat).
    private record LegacyOptions(
        string TransitionId,
        string ExpectedSha256,
        string RequestPath,
        string? VerdictPath,
        string? HumanDecisionPath,
        string? Root,
        bool DryRun);

    // state-transition 진입점. prepare/apply/legacy를 args[1]로 분기한다.
    internal static int Run(string[] args)
    {
        var sub = args.Length > 1 ? args[1] : "";
        if (string.Equals(sub, "prepare", StringComparison.OrdinalIgnoreCase))
            return RunPrepare(args);
        if (string.Equals(sub, "apply", StringComparison.OrdinalIgnoreCase))
            return RunApply(args);
        return RunLegacy(args);
    }

    // prepare: WORKSTATE를 읽어 결정적 candidate를 계산하고 envelope+candidate를 출력 디렉터리에 기록한다.
    private static int RunPrepare(string[] args)
    {
        var opts = ParsePrepareArgs(args);
        if (opts is null)
        {
            Console.Error.WriteLine("{\"error\":\"prepare에는 --transition-id, --request 가 필요합니다\"}");
            return 2;
        }
        try
        {
            var root = string.IsNullOrWhiteSpace(opts.Root) ? GitTools.FindRepoRoot() : opts.Root;
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
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{{\"error\":\"state-transition prepare 실패: {ex.Message}\"}}");
            return 2;
        }
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

    // apply: reconciliation-먼저 순서로 WORKSTATE에 전이를 적용한다. rollback + FATAL 분류 포함.
    private static int RunApply(string[] args)
    {
        var opts = ParseApplyArgs(args);
        if (opts is null)
        {
            Console.Error.WriteLine("{\"error\":\"apply에는 --envelope 이 필요합니다\"}");
            return 2;
        }

        // Envelope 읽기 — 이 단계는 atomic write 전이므로 예외 발생 시 rollback 불필요.
        var envelope = ReadEnvelope(opts.EnvelopePath);
        if (envelope is null) return 2;

        // high-risk transitionKind — receipt ledger 부재로 항상 fail-closed.
        if (HighRiskKinds.Contains(envelope.TransitionKind))
            return Fail(1, envelope.TransitionId, "trusted-human-receipt-required",
                $"transitionKind={envelope.TransitionKind}는 receipt ledger 부재로 이번 WP에서 fail-closed");

        string root, workstatePath, logPath;
        bool canonicalMode;
        try
        {
            root = string.IsNullOrWhiteSpace(opts.Root) ? GitTools.FindRepoRoot() : opts.Root;
            workstatePath = Path.Combine(root, "docs", "handoff", "WORKSTATE.json");
            logPath = Path.Combine(root, "docs", "handoff", "WORKSTATE.applier-log.jsonl");
            canonicalMode = string.IsNullOrWhiteSpace(opts.Root);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{{\"error\":\"root 결정 실패: {ex.Message}\"}}");
            return 2;
        }

        if (!File.Exists(workstatePath)) { WriteError("WORKSTATE.json not found"); return 2; }

        byte[] rawWorkstateBytes;
        JsonObject workstate;
        string rawWorkstateStr;
        try
        {
            rawWorkstateBytes = File.ReadAllBytes(workstatePath);
            rawWorkstateStr = Encoding.UTF8.GetString(rawWorkstateBytes);
            workstate = JsonNode.Parse(rawWorkstateStr)?.AsObject()
                ?? throw new InvalidOperationException("WORKSTATE.json is not a valid JSON object");
        }
        catch (Exception ex) { WriteError(ex.Message); return 2; }

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
            RunApplyValidatePhase(envelope, workstate, rawWorkstateStr, request!, opts.DryRun, root);
        if (validateExit.HasValue) return validateExit.Value;

        // Steps 7-10: atomic write + post-apply 검사 + v2 log.
        return RunApplyCommitPhase(
            envelope, workstatePath, logPath, canonicalMode,
            rawWorkstateBytes, workstate, recompCandidate!, recompBytes!);
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
        EnvelopeData envelope, string workstatePath, string logPath, bool canonicalMode,
        byte[] rawWorkstateBytes, JsonObject workstate, JsonObject recomputedCandidate, byte[] recomputedBytes)
    {
        // Step 7: preimage 저장 — atomic replace 전에 반드시 저장해야 한다.
        var preimage = rawWorkstateBytes;
        var preimageHash = Convert.ToHexString(SHA256.HashData(preimage)).ToLowerInvariant();

        // Step 8: atomic replace — 재계산 bytes만 사용(candidate 파일은 evidence, write source 아님).
        try { var t = workstatePath + ".stateapplier.tmp"; File.WriteAllBytes(t, recomputedBytes); File.Move(t, workstatePath, overwrite: true); }
        catch (Exception ex) { WriteError($"atomic write 실패: {ex.Message}"); return 2; }

        // 결정적 test seam — atomic replace 직후 실패 주입 (production 노출 플래그 아님).
        var seamFail = Environment.GetEnvironmentVariable("_ST_SEAM_FAIL_AFTER_WRITE") == "1"
            ? "test seam: _ST_SEAM_FAIL_AFTER_WRITE" : null;
        if (seamFail is not null)
            return Rollback(workstatePath, logPath, preimage, preimageHash, envelope, seamFail, canonicalMode);

        // Step 9: 적용후 검사.
        // canonical mode에서만 projection 실행(--root 사본에서 projection을 실행하면 실 저장소를 덮어쓴다).
        if (canonicalMode)
        {
            var projExit = ProjectionCli.Run(["projection"]);
            if (projExit != 0)
                return Rollback(workstatePath, logPath, preimage, preimageHash, envelope, "projection-failed", true);
        }

        // 내부 checker — PendingTransitionId로 pending 면제 경로를 실증한다.
        ReconciliationResult reconAfter;
        try { reconAfter = HandoffIntegrityChecker.Run(new ReconciliationOptions(workstatePath, logPath, envelope.TransitionId)); }
        catch (Exception ex)
        {
            return Rollback(workstatePath, logPath, preimage, preimageHash, envelope,
                $"post-apply checker 실행 실패: {ex.Message}", canonicalMode);
        }

        if (reconAfter.Failures.Count > 0 || reconAfter.HarnessErrors.Count > 0)
        {
            var codes = string.Join("; ", reconAfter.Failures.Concat(reconAfter.HarnessErrors).Select(f => f.Code));
            return Rollback(workstatePath, logPath, preimage, preimageHash, envelope,
                $"post-apply-integrity-failed: {codes}", canonicalMode);
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
        EnvelopeData envelope, string failureReason, bool canonicalMode)
    {
        try
        {
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

            // canonical mode에서만 rollback 후 projection 재생성 시도.
            if (canonicalMode)
            {
                var projExit = ProjectionCli.Run(["projection"]);
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

    // RunLegacy: 이전 단일-샷 방식(--transition-id 등). reconciliation이 추가됐다.
    private static int RunLegacy(string[] args)
    {
        var opts = ParseLegacyArgs(args);
        if (opts is null)
        {
            Console.Error.WriteLine("{\"error\":\"--transition-id, --expected-workstate-sha256, --request 가 모두 필요합니다\"}");
            return 2;
        }
        try
        {
            var root = string.IsNullOrWhiteSpace(opts.Root) ? GitTools.FindRepoRoot() : opts.Root;
            var workstatePath = Path.Combine(root, "docs", "handoff", "WORKSTATE.json");
            if (!File.Exists(workstatePath)) { WriteError("WORKSTATE.json not found"); return 2; }

            var raw = File.ReadAllText(workstatePath, new UTF8Encoding(false));
            var workstate = JsonNode.Parse(raw)?.AsObject();
            if (workstate is null) { WriteError("WORKSTATE.json is not a valid JSON object"); return 2; }

            // Step 1: 낙관적 동시성 — sha256 불일치 시 쓰지 않는다.
            var currentSha = ComputeSha256(raw);
            if (!string.Equals(currentSha, opts.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                return Fail(1, opts.TransitionId, "sha256-mismatch",
                    $"expected={Short(opts.ExpectedSha256)} actual={Short(currentSha)}");

            var logPath = Path.Combine(root, "docs", "handoff", "WORKSTATE.applier-log.jsonl");

            // Step 2: reconciliation — idempotency보다 먼저 실행해 가짜 id 공격을 막는다.
            var reconResult = HandoffIntegrityChecker.Run(new ReconciliationOptions(workstatePath, logPath));
            if (reconResult.Failures.Count > 0 || reconResult.HarnessErrors.Count > 0)
            {
                var codes = string.Join("; ", reconResult.Failures.Concat(reconResult.HarnessErrors).Select(f => f.Code));
                return Fail(1, opts.TransitionId, "state-corrupted-preapply", codes);
            }

            // Step 3: 멱등 판정 (reconciliation 통과 후).
            if (IsAlreadyAppliedWithLog(workstate, reconResult, opts.TransitionId, out var legacyIdempotentExit))
                return legacyIdempotentExit;

            var actualRoot = string.IsNullOrWhiteSpace(opts.Root) ? GitTools.FindRepoRoot() : root;
            return ApplyLegacy(workstatePath, logPath, workstate, opts, actualRoot);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{{\"error\":\"state-transition 실패: {ex.Message}\"}}");
            return 2;
        }
    }

    // ApplyLegacy: 이전 단일-샷 경로의 검증·쓰기·post-apply를 실행한다.
    private static int ApplyLegacy(
        string workstatePath, string logPath, JsonObject workstate,
        LegacyOptions opts, string actualRoot)
    {
        if (!File.Exists(opts.RequestPath)) { WriteError($"request file not found: {opts.RequestPath}"); return 2; }
        var request = JsonNode.Parse(File.ReadAllText(opts.RequestPath))?.AsObject();
        if (request is null) { WriteError("request JSON은 JSON 객체여야 한다"); return 2; }

        var validErr = ValidateRequest(request, opts, workstate, actualRoot);
        if (validErr is not null) return Fail(1, opts.TransitionId, "validation-failed", validErr);

        var effectiveAt = DateTime.UtcNow.ToString("o");
        var candidate = BuildCandidate(workstate, request, opts.TransitionId, effectiveAt);

        if (opts.DryRun) return ApplyLegacyDryRun(workstate, candidate, opts, actualRoot);

        var tmpPath = workstatePath + ".stateapplier.tmp";
        var candidateJson = candidate.ToJsonString(WriteOptions);
        File.WriteAllText(tmpPath, candidateJson, new UTF8Encoding(false));

        var candidateCheck = JsonNode.Parse(File.ReadAllText(tmpPath))?.AsObject();
        var recheckErr = candidateCheck is null ? "임시 파일 파싱 실패" : ValidateCandidate(candidateCheck, actualRoot);
        if (recheckErr is not null) { File.Delete(tmpPath); return Fail(1, opts.TransitionId, "candidate-invalid", recheckErr); }

        File.Move(tmpPath, workstatePath, overwrite: true);

        // canonical mode에서만 post-apply 실행.
        if (string.IsNullOrWhiteSpace(opts.Root))
        {
            var postExit = RunLegacyPostApply(logPath, opts);
            if (postExit != 0) return postExit;
        }

        var newSha = ComputeSha256(File.ReadAllText(workstatePath, new UTF8Encoding(false)));
        AppendApplierLog(logPath, opts.TransitionId, "ok", 0);
        Console.WriteLine(new JsonObject
        {
            ["ok"] = true,
            ["transitionId"] = opts.TransitionId,
            ["previousStatus"] = workstate["status"]?.ToString(),
            ["newStatus"] = candidate["status"]?.ToString(),
            ["workstateSha256"] = newSha,
        }.ToJsonString(WriteOptions));
        return 0;
    }

    // ApplyLegacyDryRun: candidate를 검증하고 결과를 출력하지만 아무것도 쓰지 않는다.
    private static int ApplyLegacyDryRun(JsonObject workstate, JsonObject candidate, LegacyOptions opts, string actualRoot)
    {
        var err = ValidateCandidate(candidate, actualRoot);
        if (err is not null) return Fail(1, opts.TransitionId, "candidate-invalid", err);
        Console.WriteLine(new JsonObject
        {
            ["ok"] = true, ["dryRun"] = true,
            ["transitionId"] = opts.TransitionId,
            ["previousStatus"] = workstate["status"]?.ToString(),
            ["newStatus"] = candidate["status"]?.ToString(),
        }.ToJsonString(WriteOptions));
        return 0;
    }

    // RunLegacyPostApply: projection과 handoff-integrity CLI를 실행한다.
    private static int RunLegacyPostApply(string logPath, LegacyOptions opts)
    {
        var projExit = ProjectionCli.Run(["projection"]);
        if (projExit != 0)
        {
            AppendApplierLog(logPath, opts.TransitionId, "projection-failed", projExit);
            Console.Error.WriteLine(LegacyPostApplyError(opts.TransitionId, "projection-failed", projExit, true, false));
            return 1;
        }
        var intExit = HandoffIntegrityCli.Run(["handoff-integrity"]);
        if (intExit != 0)
        {
            AppendApplierLog(logPath, opts.TransitionId, $"handoff-integrity-failed exit={intExit}", intExit);
            Console.Error.WriteLine(LegacyPostApplyError(opts.TransitionId, $"handoff-integrity exit={intExit}", intExit, true, true));
            return 1;
        }
        return 0;
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

    // 전이 ID가 이미 state에 있는지 판정하고 idempotent/collision/legacy-unverifiable exit를 반환한다.
    private static int? CheckExistingTransition(
        JsonObject workstate, ReconciliationResult reconResult, EnvelopeData envelope)
    {
        var isInState = workstate["appliedTransitions"] is JsonArray applied &&
            applied.OfType<JsonObject>().Any(o =>
                string.Equals(o["id"]?.ToString(), envelope.TransitionId, StringComparison.Ordinal));

        if (!isInState) return null; // 미적용 — 정상 경로로 진행.

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

        // v2 log — envelope의 contract hash와 비교.
        // envelope에 contractHash가 있으면 직접 비교, 없으면 계산.
        var envelopeContractHash = string.IsNullOrWhiteSpace(envelope.TransitionContractSha256)
            ? null : envelope.TransitionContractSha256;

        if (envelopeContractHash is null || !string.Equals(
            envelopeContractHash, logInfo.TransitionContractSha256, StringComparison.OrdinalIgnoreCase))
        {
            return Fail(1, envelope.TransitionId, "transition-id-collision",
                $"id '{envelope.TransitionId}'는 다른 contract hash로 이미 적용됨");
        }

        // 동일 contract hash — idempotent.
        return ReportIdempotent(envelope.TransitionId);
    }

    // 이전 단일-샷 경로의 멱등 판정. v2 log 항목이면 legacy-unverifiable, v1이면 idempotent.
    private static bool IsAlreadyAppliedWithLog(
        JsonObject workstate, ReconciliationResult reconResult, string transitionId, out int exitCode)
    {
        var applied = workstate["appliedTransitions"] as JsonArray;
        if (applied is null || !applied.OfType<JsonObject>().Any(o =>
            string.Equals(o["id"]?.ToString(), transitionId, StringComparison.OrdinalIgnoreCase)))
        {
            exitCode = 0;
            return false;
        }

        reconResult.SuccessLookup.TryGetValue(transitionId, out var logInfo);
        if (logInfo?.SchemaVersion >= 2)
        {
            exitCode = Fail(1, transitionId, "legacy-idempotency-unverifiable",
                $"id '{transitionId}'는 v2 log에 있음 — legacy 경로에서 재적용 불가");
            return true;
        }
        exitCode = ReportIdempotent(transitionId);
        return true;
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

        // apply 경로에서 human-decision 없이 전이 그래프를 검증한다.
        var transErr = ValidateStatusTransition(currentStatus, newStatus, currentDiId, requestDiId, null);
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

    // request JSON의 canonical ID 패턴, status enum, DI 경계·전이 그래프·verdict·human-decision을 검증한다.
    private static string? ValidateRequest(JsonObject request, LegacyOptions opts, JsonObject current, string root)
    {
        if (request["phaseId"] is JsonNode ph && !Regex.IsMatch(ph.ToString(), @"^P\d{2,}$"))
            return $"phaseId '{ph}'는 canonical 형식(P00, P01 …)이 아닙니다";
        if (request["wpId"] is JsonNode wp && !Regex.IsMatch(wp.ToString(), @"^WP-\d{2,}$"))
            return $"wpId '{wp}'는 canonical 형식(WP-00 …)이 아닙니다";
        if (request["diId"] is JsonNode di && !Regex.IsMatch(di.ToString(), @"^DI-\d{2}-\d{2,}$"))
            return $"diId '{di}'는 canonical 형식(DI-00-05 …)이 아닙니다";

        var newStatus = request["status"]?.ToString() ?? current["status"]?.ToString() ?? "";
        if (!ValidStatuses.Contains(newStatus))
            return $"status '{newStatus}'는 허용 목록(waiting|in_progress|verifying|completed|blocked)에 없습니다";

        var currentStatus = current["status"]?.ToString() ?? "";
        var currentDiId = current["diId"]?.ToString() ?? "";
        var requestDiId = request["diId"]?.ToString();

        var transErr = ValidateStatusTransition(currentStatus, newStatus, currentDiId, requestDiId,
            opts.HumanDecisionPath);
        if (transErr is not null) return transErr;

        var newBlockers = request["blockers"] as JsonArray ?? current["blockers"] as JsonArray ?? new JsonArray();
        var newNext = request["nextActions"] as JsonArray ?? current["nextActions"] as JsonArray ?? new JsonArray();

        if (string.Equals(newStatus, "blocked", StringComparison.OrdinalIgnoreCase) && newBlockers.Count == 0)
            return "status=blocked인데 blockers가 비어있습니다";
        if (NextActionsRequired.Contains(newStatus) && newNext.Count == 0)
            return $"status={newStatus}인데 nextActions가 비어있습니다 — 독립 재개가 실패하는 지점입니다";

        if (string.Equals(newStatus, "completed", StringComparison.OrdinalIgnoreCase))
        {
            var targetDiId = requestDiId ?? currentDiId;
            var wsUpdatedAt = current["updatedAt"]?.ToString() ?? "";
            var verdictErr = ValidateVerdict(opts.VerdictPath, targetDiId, wsUpdatedAt);
            if (verdictErr is not null) return verdictErr;
        }

        var curPhase = current["phaseId"]?.ToString();
        var newPhase = request["phaseId"]?.ToString();
        if (newPhase is not null && !string.Equals(newPhase, curPhase, StringComparison.OrdinalIgnoreCase))
        {
            var gateErr = ValidateHumanDecision(opts.HumanDecisionPath, $"Phase 전이({curPhase} → {newPhase})");
            if (gateErr is not null) return gateErr;
        }

        return null;
    }

    // DI 경계와 전이 그래프를 검사한다.
    private static string? ValidateStatusTransition(
        string currentStatus, string newStatus, string currentDiId, string? requestDiId,
        string? humanDecisionPath)
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

        // completed는 terminal — 이탈하려면 human-decision 필요 (legacy 경로).
        if (string.Equals(currentStatus, "completed", StringComparison.OrdinalIgnoreCase))
            return ValidateHumanDecision(humanDecisionPath, $"completed 상태 전이({currentStatus} → {newStatus})");

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

    // human-decision 파일이 approved=true인지 검증한다. 골격 유지 — 이 경로는 이번 WP에서 fail-closed다.
    private static string? ValidateHumanDecision(string? decPath, string context)
    {
        if (string.IsNullOrWhiteSpace(decPath))
            return $"{context}에는 --human-decision 파일이 필요합니다";
        if (!File.Exists(decPath)) return $"human-decision file not found: {decPath}";
        var d = JsonNode.Parse(File.ReadAllText(decPath))?.AsObject();
        if (d is null) return "human-decision JSON이 유효하지 않습니다";
        var approved = d["approved"]?.GetValue<bool>() ?? false;
        return !approved ? $"{context}가 사람에게 승인되지 않았습니다(approved={approved})" : null;
    }

    // candidate WORKSTATE의 사후 조건을 검증한다.
    private static string? ValidateCandidate(JsonObject candidate, string root)
    {
        var status = candidate["status"]?.ToString() ?? "";
        if (!ValidStatuses.Contains(status)) return $"candidate status '{status}'가 유효하지 않습니다";

        var blockers = candidate["blockers"] as JsonArray;
        if (string.Equals(status, "blocked", StringComparison.OrdinalIgnoreCase) && (blockers is null || blockers.Count == 0))
            return "candidate: status=blocked인데 blockers가 비어있습니다";

        var nextActions = candidate["nextActions"] as JsonArray;
        if (NextActionsRequired.Contains(status) && (nextActions is null || nextActions.Count == 0))
            return $"candidate: status={status}인데 nextActions가 비어있습니다";

        if (candidate["phaseId"] is JsonNode ph && !Regex.IsMatch(ph.ToString(), @"^P\d{2,}$"))
            return $"candidate phaseId '{ph}'는 canonical 형식이 아닙니다";
        if (candidate["wpId"] is JsonNode wp && !Regex.IsMatch(wp.ToString(), @"^WP-\d{2,}$"))
            return $"candidate wpId '{wp}'는 canonical 형식이 아닙니다";
        if (candidate["diId"] is JsonNode di && !Regex.IsMatch(di.ToString(), @"^DI-\d{2}-\d{2,}$"))
            return $"candidate diId '{di}'는 canonical 형식이 아닙니다";

        var wpId = candidate["wpId"]?.ToString();
        if (!string.IsNullOrWhiteSpace(wpId))
        {
            var registryErr = ValidateWpRegistry(root, wpId);
            if (registryErr is not null) return registryErr;
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

    // 전이 결과를 applier-log에 한 줄 추가한다(v1 format, legacy 경로용).
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

    // post-apply 단계 실패 메시지를 JSON 문자열로 만든다(legacy 경로용).
    private static string LegacyPostApplyError(string tid, string failure, int exitCode, bool wsApplied, bool projApplied)
    {
        var note = projApplied
            ? "WORKSTATE와 projection은 갱신됐으나 handoff-integrity가 실패했다"
            : "WORKSTATE는 갱신됐으나 projection이 실패했다 — projection을 수동으로 재실행하라";
        return new JsonObject
        {
            ["transitionId"] = tid, ["phase"] = "post-apply",
            ["failure"] = failure, ["exitCode"] = exitCode,
            ["workstateApplied"] = wsApplied, ["projectionApplied"] = projApplied,
            ["note"] = note,
        }.ToJsonString();
    }

    // prepare 인수를 파싱한다.
    private static PrepareOptions? ParsePrepareArgs(string[] args)
    {
        var map = ParseFlagMap(args, 2);
        var dryRun = map.ContainsKey("dry-run-flag"); map.Remove("dry-run-flag");
        if (!map.TryGetValue("transition-id", out var tid) || string.IsNullOrWhiteSpace(tid)) return null;
        if (!map.TryGetValue("request", out var req) || string.IsNullOrWhiteSpace(req)) return null;
        map.TryGetValue("root", out var root);
        return new PrepareOptions(tid, req, root, dryRun);
    }

    // apply 인수를 파싱한다.
    private static ApplyOptions? ParseApplyArgs(string[] args)
    {
        var map = ParseFlagMap(args, 2);
        var dryRun = map.ContainsKey("dry-run-flag"); map.Remove("dry-run-flag");
        if (!map.TryGetValue("envelope", out var env) || string.IsNullOrWhiteSpace(env)) return null;
        map.TryGetValue("verdict", out var verdict);
        map.TryGetValue("root", out var root);
        return new ApplyOptions(env, verdict, root, dryRun);
    }

    // legacy 인수를 파싱한다.
    private static LegacyOptions? ParseLegacyArgs(string[] args)
    {
        var map = ParseFlagMap(args, 1);
        var dryRun = map.ContainsKey("dry-run-flag"); map.Remove("dry-run-flag");
        if (!map.TryGetValue("transition-id", out var tid) || string.IsNullOrWhiteSpace(tid)) return null;
        if (!map.TryGetValue("expected-workstate-sha256", out var sha) || string.IsNullOrWhiteSpace(sha)) return null;
        if (!map.TryGetValue("request", out var req) || string.IsNullOrWhiteSpace(req)) return null;
        map.TryGetValue("verdict", out var verdict);
        map.TryGetValue("human-decision", out var humanDec);
        map.TryGetValue("root", out var root);
        return new LegacyOptions(tid, sha, req, verdict, humanDec, root, dryRun);
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
}
