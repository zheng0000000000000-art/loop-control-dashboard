// WORKSTATE 상태 전이의 유일한 writer. 낙관적 동시성·멱등 전이-ID·canonical 검증·전이 그래프·WP-REGISTRY 검사를 강제한다.
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

    // CLI 인수를 담는 불변 레코드.
    private record ApplierOptions(
        string TransitionId,
        string ExpectedSha256,
        string RequestPath,
        string? VerdictPath,
        string? HumanDecisionPath,
        string? Root,    // 지정 시 이 경로를 저장소 루트로 사용한다.
        bool DryRun);    // true면 검증만 하고 아무것도 쓰지 않는다.

    // state-transition 진입점. exit 0=성공, 1=검증 실패, 2=치명 오류.
    internal static int Run(string[] args)
    {
        var opts = ParseArgs(args);
        if (opts is null)
        {
            Console.Error.WriteLine("{\"error\":\"--transition-id, --expected-workstate-sha256, --request 가 모두 필요합니다\"}");
            return 2;
        }

        try
        {
            var root = string.IsNullOrWhiteSpace(opts.Root) ? GitTools.FindRepoRoot() : opts.Root;
            var workstatePath = Path.Combine(root, "docs", "handoff", "WORKSTATE.json");
            if (!File.Exists(workstatePath))
            {
                WriteError("WORKSTATE.json not found");
                return 2;
            }

            var raw = File.ReadAllText(workstatePath, new UTF8Encoding(false));
            var workstate = JsonNode.Parse(raw)?.AsObject();
            if (workstate is null)
            {
                WriteError("WORKSTATE.json is not a valid JSON object");
                return 2;
            }

            // Step 1: 낙관적 동시성 — sha256 불일치 시 쓰지 않는다.
            var currentSha = ComputeSha256(raw);
            if (!string.Equals(currentSha, opts.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                return Fail(1, opts.TransitionId, "sha256-mismatch",
                    $"expected={opts.ExpectedSha256[..Math.Min(16, opts.ExpectedSha256.Length)]}... " +
                    $"actual={currentSha[..Math.Min(16, currentSha.Length)]}...");

            // Step 2: 멱등 — 같은 transition-id는 한 번만 적용한다.
            if (IsAlreadyApplied(workstate, opts.TransitionId))
                return ReportIdempotent(opts.TransitionId);

            var logPath = Path.Combine(root, "docs", "handoff", "WORKSTATE.applier-log.jsonl");
            return ApplyAndVerify(workstatePath, logPath, workstate, opts, GitTools.FindRepoRoot());
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{{\"error\":\"state-transition 실패: {ex.Message}\"}}");
            return 2;
        }
    }

    // request 검증, candidate 구성, (dry-run이 아니면) atomic replace·post-apply를 순서대로 실행한다.
    private static int ApplyAndVerify(
        string workstatePath, string logPath, JsonObject workstate,
        ApplierOptions opts, string actualRoot)
    {
        if (!File.Exists(opts.RequestPath))
        {
            WriteError($"request file not found: {opts.RequestPath}");
            return 2;
        }
        var request = JsonNode.Parse(File.ReadAllText(opts.RequestPath))?.AsObject();
        if (request is null) { WriteError("request JSON은 JSON 객체여야 한다"); return 2; }

        var validErr = ValidateRequest(request, opts, workstate, actualRoot);
        if (validErr is not null)
            return Fail(1, opts.TransitionId, "validation-failed", validErr);

        var candidate = BuildCandidate(workstate, request, opts);
        if (opts.DryRun) return ApplyDryRun(workstate, candidate, opts, actualRoot);

        var tmpPath = workstatePath + ".stateapplier.tmp";
        File.WriteAllText(tmpPath, candidate.ToJsonString(WriteOptions), new UTF8Encoding(false));

        var candidateCheck = JsonNode.Parse(File.ReadAllText(tmpPath))?.AsObject();
        var recheckErr = candidateCheck is null ? "임시 파일 파싱 실패" : ValidateCandidate(candidateCheck, actualRoot);
        if (recheckErr is not null) { File.Delete(tmpPath); return Fail(1, opts.TransitionId, "candidate-invalid", recheckErr); }

        File.Move(tmpPath, workstatePath, overwrite: true);

        // 커스텀 루트에서는 projection·handoff-integrity를 건너뛴다(GitTools로 실 저장소를 찾으므로 사본에 의미 없음).
        if (string.IsNullOrWhiteSpace(opts.Root))
        {
            var postExit = RunPostApply(logPath, opts);
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

    // dry-run 모드: candidate를 검증하고 결과를 출력하지만 아무것도 쓰지 않는다.
    private static int ApplyDryRun(JsonObject workstate, JsonObject candidate, ApplierOptions opts, string actualRoot)
    {
        var err = ValidateCandidate(candidate, actualRoot);
        if (err is not null) return Fail(1, opts.TransitionId, "candidate-invalid", err);
        Console.WriteLine(new JsonObject
        {
            ["ok"] = true,
            ["dryRun"] = true,
            ["transitionId"] = opts.TransitionId,
            ["previousStatus"] = workstate["status"]?.ToString(),
            ["newStatus"] = candidate["status"]?.ToString(),
        }.ToJsonString(WriteOptions));
        return 0;
    }

    // projection과 handoff-integrity를 실행하고 실패 시 applier-log에 기록하고 exit code를 반환한다.
    private static int RunPostApply(string logPath, ApplierOptions opts)
    {
        var projExit = ProjectionCli.Run(["projection"]);
        if (projExit != 0)
        {
            AppendApplierLog(logPath, opts.TransitionId, "projection-failed", projExit);
            Console.Error.WriteLine(PostApplyError(opts.TransitionId, "projection-failed", projExit, true, false));
            return 1;
        }
        var intExit = HandoffIntegrityCli.Run(["handoff-integrity"]);
        if (intExit != 0)
        {
            AppendApplierLog(logPath, opts.TransitionId, $"handoff-integrity-failed exit={intExit}", intExit);
            Console.Error.WriteLine(PostApplyError(opts.TransitionId, $"handoff-integrity exit={intExit}", intExit, true, true));
            return 1;
        }
        return 0;
    }

    // request JSON의 canonical ID 패턴, status enum, 사전조건(blocked/nextActions/verdict/human-decision)을 검증한다.
    private static string? ValidateRequest(JsonObject request, ApplierOptions opts, JsonObject current, string root)
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

        // Rule NEW: 허용 전이 그래프 검사 — 역방향 및 비허용 전이를 exit 1로 차단.
        var currentStatus = current["status"]?.ToString() ?? "";
        if (!string.Equals(currentStatus, newStatus, StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(currentStatus, "completed", StringComparison.OrdinalIgnoreCase))
            {
                // completed는 terminal — 사람 결재(human-decision approved:true) 없으면 차단.
                var completedErr = ValidateHumanDecision(opts.HumanDecisionPath,
                    $"completed 상태 전이({currentStatus} → {newStatus})");
                if (completedErr is not null) return completedErr;
            }
            else if (AllowedTransitions.TryGetValue(currentStatus, out var allowed) && !allowed.Contains(newStatus))
            {
                var allowedStr = allowed.Count > 0 ? string.Join(", ", allowed) : "없음(terminal)";
                return $"status 전이 {currentStatus} → {newStatus}는 허용되지 않습니다(허용: {allowedStr})";
            }
        }

        var newBlockers = request["blockers"] as JsonArray ?? current["blockers"] as JsonArray ?? new JsonArray();
        var newNext = request["nextActions"] as JsonArray ?? current["nextActions"] as JsonArray ?? new JsonArray();

        // Rule 4: blocked → blockers 비어있으면 실패.
        if (string.Equals(newStatus, "blocked", StringComparison.OrdinalIgnoreCase) && newBlockers.Count == 0)
            return "status=blocked인데 blockers가 비어있습니다";

        // Rule 5: non-terminal → nextActions 비어있으면 실패 (독립 재개 FAIL 방어선).
        if (NextActionsRequired.Contains(newStatus) && newNext.Count == 0)
            return $"status={newStatus}인데 nextActions가 비어있습니다 — 독립 재개가 실패하는 지점입니다";

        // Rule 6: completed → 독립 검증 PASS 필수.
        if (string.Equals(newStatus, "completed", StringComparison.OrdinalIgnoreCase))
        {
            var verdictErr = ValidateVerdict(opts.VerdictPath);
            if (verdictErr is not null) return verdictErr;
        }

        // Rule 7: Phase gate → 사람 결정 필수.
        var curPhase = current["phaseId"]?.ToString();
        var newPhase = request["phaseId"]?.ToString();
        if (newPhase is not null && !string.Equals(newPhase, curPhase, StringComparison.OrdinalIgnoreCase))
        {
            var gateErr = ValidateHumanDecision(opts.HumanDecisionPath, $"Phase 전이({curPhase} → {newPhase})");
            if (gateErr is not null) return gateErr;
        }

        return null;
    }

    // verdict 파일이 verificationPassed=true, exitCode=0인지 검증한다.
    private static string? ValidateVerdict(string? verdictPath)
    {
        if (string.IsNullOrWhiteSpace(verdictPath))
            return "status=completed 전이에는 --verdict 파일이 필요합니다";
        if (!File.Exists(verdictPath))
            return $"verdict file not found: {verdictPath}";
        var v = JsonNode.Parse(File.ReadAllText(verdictPath))?.AsObject();
        if (v is null) return "verdict JSON이 유효하지 않습니다";
        var passed = v["verificationPassed"]?.GetValue<bool>() ?? false;
        var exitCode = v["exitCode"]?.GetValue<int>() ?? -1;
        return (!passed || exitCode != 0) ? $"verdict: verificationPassed={passed}, exitCode={exitCode} — PASS가 아닙니다" : null;
    }

    // human-decision 파일이 approved=true인지 검증한다. context는 오류 메시지에 포함된다.
    private static string? ValidateHumanDecision(string? decPath, string context)
    {
        if (string.IsNullOrWhiteSpace(decPath))
            return $"{context}에는 --human-decision 파일이 필요합니다";
        if (!File.Exists(decPath))
            return $"human-decision file not found: {decPath}";
        var d = JsonNode.Parse(File.ReadAllText(decPath))?.AsObject();
        if (d is null) return "human-decision JSON이 유효하지 않습니다";
        var approved = d["approved"]?.GetValue<bool>() ?? false;
        return !approved ? $"{context}가 사람에게 승인되지 않았습니다(approved={approved})" : null;
    }

    // request 필드를 current WORKSTATE에 병합해 candidate를 만들고 appliedTransitions에 기록한다.
    private static JsonObject BuildCandidate(JsonObject current, JsonObject request, ApplierOptions opts)
    {
        var candidate = JsonNode.Parse(current.ToJsonString(WriteOptions))!.AsObject()!;

        foreach (var key in new[] { "phaseId", "wpId", "diId", "status", "blockers", "nextActions", "updatedBy", "notes" })
        {
            if (request[key] is JsonNode node)
                candidate[key] = node.DeepClone();
        }

        candidate["updatedAt"] = DateTime.UtcNow.ToString("yyyy-MM-dd");

        if (candidate["appliedTransitions"] is not JsonArray applied)
        {
            applied = new JsonArray();
            candidate["appliedTransitions"] = applied;
        }

        applied.Add(new JsonObject
        {
            ["id"] = opts.TransitionId,
            ["appliedAt"] = DateTime.UtcNow.ToString("o"),
        });

        return candidate;
    }

    // candidate WORKSTATE의 사후 조건(status enum, blocked→blockers, non-terminal→nextActions, canonical ID 패턴, WP-REGISTRY 등록 여부)을 검증한다.
    private static string? ValidateCandidate(JsonObject candidate, string root)
    {
        var status = candidate["status"]?.ToString() ?? "";
        if (!ValidStatuses.Contains(status))
            return $"candidate status '{status}'가 유효하지 않습니다";

        var blockers = candidate["blockers"] as JsonArray;
        if (string.Equals(status, "blocked", StringComparison.OrdinalIgnoreCase) && (blockers is null || blockers.Count == 0))
            return "candidate: status=blocked인데 blockers가 비어있습니다";

        var nextActions = candidate["nextActions"] as JsonArray;
        if (NextActionsRequired.Contains(status) && (nextActions is null || nextActions.Count == 0))
            return $"candidate: status={status}인데 nextActions가 비어있습니다";

        // canonical 패턴 검사 — 정지 상태(at rest)에서도 강제한다(요청에 없어 상속된 값 포함).
        if (candidate["phaseId"] is JsonNode ph && !Regex.IsMatch(ph.ToString(), @"^P\d{2,}$"))
            return $"candidate phaseId '{ph}'는 canonical 형식(P00, P01 …)이 아닙니다";

        if (candidate["wpId"] is JsonNode wp && !Regex.IsMatch(wp.ToString(), @"^WP-\d{2,}$"))
            return $"candidate wpId '{wp}'는 canonical 형식(WP-00 …)이 아닙니다";

        if (candidate["diId"] is JsonNode di && !Regex.IsMatch(di.ToString(), @"^DI-\d{2}-\d{2,}$"))
            return $"candidate diId '{di}'는 canonical 형식(DI-00-05 …)이 아닙니다";

        // WP-REGISTRY 검사 — wpId가 등록된 WP인지 확인한다.
        var wpId = candidate["wpId"]?.ToString();
        if (!string.IsNullOrWhiteSpace(wpId))
        {
            var registryErr = ValidateWpRegistry(root, wpId);
            if (registryErr is not null) return registryErr;
        }

        return null;
    }

    // wpId가 WP-REGISTRY.json에 등록되어 있는지 검사한다. 없으면 오류 메시지를 반환한다.
    private static string? ValidateWpRegistry(string root, string wpId)
    {
        var registryPath = Path.Combine(root, "docs", "handoff", "WP-REGISTRY.json");
        if (!File.Exists(registryPath))
            return "WP-REGISTRY.json not found — WP 등록표가 없습니다";

        var reg = JsonNode.Parse(File.ReadAllText(registryPath))?.AsObject();
        if (reg is null) return "WP-REGISTRY.json이 유효한 JSON 객체가 아닙니다";

        var wps = reg["wps"] as JsonArray;
        if (wps is null) return "WP-REGISTRY.json에 'wps' 배열이 없습니다";

        var exists = wps.OfType<JsonObject>().Any(w =>
            string.Equals(w["wpId"]?.ToString(), wpId, StringComparison.OrdinalIgnoreCase));

        return exists ? null : $"wpId '{wpId}'가 WP-REGISTRY.json에 등록되지 않았습니다 — WP 추가는 사람 결재입니다";
    }

    // CLI 인수를 파싱한다. 필수 인수(transition-id·expected-workstate-sha256·request)가 없으면 null 반환.
    // --root <경로>: 저장소 루트를 지정한다(사본 시험용). --dry-run: 검증만 하고 아무것도 쓰지 않는다.
    private static ApplierOptions? ParseArgs(string[] args)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var dryRun = false;
        for (var i = 1; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--dry-run", StringComparison.OrdinalIgnoreCase))
            {
                dryRun = true;
                continue;
            }
            if (args[i].StartsWith("--", StringComparison.Ordinal) && i + 1 < args.Length)
            {
                map[args[i][2..]] = args[i + 1];
                i++;
            }
        }

        if (!map.TryGetValue("transition-id", out var tid) || string.IsNullOrWhiteSpace(tid)) return null;
        if (!map.TryGetValue("expected-workstate-sha256", out var sha) || string.IsNullOrWhiteSpace(sha)) return null;
        if (!map.TryGetValue("request", out var req) || string.IsNullOrWhiteSpace(req)) return null;

        map.TryGetValue("verdict", out var verdict);
        map.TryGetValue("human-decision", out var humanDec);
        map.TryGetValue("root", out var root);
        return new ApplierOptions(tid, sha, req, verdict, humanDec, root, dryRun);
    }

    // appliedTransitions에서 transition-id 중복 여부를 확인한다.
    private static bool IsAlreadyApplied(JsonObject workstate, string transitionId)
    {
        var applied = workstate["appliedTransitions"] as JsonArray;
        if (applied is null) return false;
        return applied.OfType<JsonObject>().Any(o =>
            string.Equals(o["id"]?.ToString(), transitionId, StringComparison.OrdinalIgnoreCase));
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

    // post-apply 단계 실패 메시지를 JSON 문자열로 만든다.
    private static string PostApplyError(string tid, string failure, int exitCode, bool wsApplied, bool projApplied)
    {
        var note = projApplied
            ? "WORKSTATE와 projection은 갱신됐으나 handoff-integrity가 실패했다"
            : "WORKSTATE는 갱신됐으나 projection이 실패했다 — projection을 수동으로 재실행하라";
        return new JsonObject
        {
            ["transitionId"] = tid,
            ["phase"] = "post-apply",
            ["failure"] = failure,
            ["exitCode"] = exitCode,
            ["workstateApplied"] = wsApplied,
            ["projectionApplied"] = projApplied,
            ["note"] = note,
        }.ToJsonString();
    }

    // 전이 결과를 WORKSTATE.applier-log.jsonl에 한 줄 추가한다(append-only).
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

    // 문자열의 SHA-256을 소문자 hex로 반환한다.
    private static string ComputeSha256(string content)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
}
