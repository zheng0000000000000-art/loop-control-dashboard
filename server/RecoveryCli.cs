// RECOVERY fail-closed 진단과 사고 evidence 생성 CLI.
// 실제 state/log 복구는 수행하지 않고 fixture와 quarantine 자료만 만든다.
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class RecoveryCli
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // RECOVERY 진단 대상 파일 경로다.
    private record RecoveryFiles(string Root, string WorkstatePath, string LogPath, string PendingDir);

    // pending journal의 핵심 binding 필드다.
    private record PendingInfo(string Path, string TransitionId, string RequestSha256, string PreStateSha256, string PostStateSha256, string TransitionContractSha256);

    // recovery CLI 진입점이다.
    internal static int Run(string[] args)
    {
        var sub = args.Length > 1 ? args[1] : "";
        if (string.Equals(sub, "inspect", StringComparison.OrdinalIgnoreCase)) return RunInspect(args);
        if (string.Equals(sub, "evidence", StringComparison.OrdinalIgnoreCase)) return RunEvidence(args);
        if (string.Equals(sub, "--self-test", StringComparison.OrdinalIgnoreCase)) return RunSelfTest();
        Console.Error.WriteLine(new JsonObject { ["error"] = "recovery usage: inspect | evidence --out <dir> | --self-test" }.ToJsonString());
        return 2;
    }

    // 현재 repo 또는 fixture 경로의 RECOVERY 상태를 분류한다.
    private static int RunInspect(string[] args)
    {
        try
        {
            var files = ParseFiles(args, GitTools.FindRepoRoot());
            var report = InspectCore(files);
            Console.WriteLine(report.ToJsonString(JsonOptions));
            return ExitForReport(report);
        }
        catch (Exception ex) { return Error(ex.Message, 2); }
    }

    // 진단 결과를 기반으로 quarantine evidence package를 만든다.
    private static int RunEvidence(string[] args)
    {
        try
        {
            var outDir = Flag(args, "out");
            if (string.IsNullOrWhiteSpace(outDir)) return Error("recovery-evidence-out-required", 2);
            var files = ParseFiles(args, GitTools.FindRepoRoot());
            var report = InspectCore(files);
            WriteEvidencePackage(files, report, Path.GetFullPath(outDir));
            Console.WriteLine(new JsonObject { ["command"] = "recovery evidence", ["outDir"] = Path.GetFullPath(outDir), ["stateMutated"] = false, ["logMutated"] = false }.ToJsonString(JsonOptions));
            return ExitForReport(report);
        }
        catch (Exception ex) { return Error(ex.Message, 2); }
    }

    // RECOVERY fault fixture 전체를 임시 경로에서 검증한다.
    private static int RunSelfTest()
    {
        var cases = new JsonArray();
        Add(cases, "pending-pre-no-success", RunCase(CasePendingPreNoSuccess));
        Add(cases, "pending-post-success", RunCase(CasePendingPostSuccess));
        Add(cases, "pending-post-no-success", RunCase(CasePendingPostNoSuccess));
        Add(cases, "pending-ambiguous", RunCase(CasePendingAmbiguous));
        Add(cases, "state-only-gap", RunCase(CaseStateOnlyGap));
        Add(cases, "conflicting-success", RunCase(CaseConflict));
        Add(cases, "evidence-package", RunCase(CaseEvidencePackage));
        Add(cases, "high-risk-stays-closed", RunCase(CaseHighRiskClosed));
        var failed = cases.OfType<JsonObject>().Count(c => c["pass"]?.GetValue<bool>() != true);
        Console.WriteLine(new JsonObject { ["selfTest"] = "recovery-fault-infra", ["verdict"] = failed == 0 ? "PASS" : "FAIL", ["casesRun"] = cases.Count, ["failed"] = failed, ["cases"] = cases }.ToJsonString(JsonOptions));
        return failed == 0 ? 0 : 1;
    }

    // fixture마다 독립 임시 디렉터리를 사용한다.
    private static bool RunCase(Func<string, bool> test)
    {
        var root = Path.Combine(Path.GetTempPath(), $"recovery-fixture-{Guid.NewGuid():N}");
        try { Directory.CreateDirectory(root); return test(root); }
        catch { return false; }
        finally { TryDeleteDirectory(root); }
    }

    // pending이 pre-state에 남은 crash window를 검증한다.
    private static bool CasePendingPreNoSuccess(string root)
    {
        var files = CreateFixture(root, stateHasPost: false, logHasPost: false);
        WritePending(files, "RX-1");
        return HasPendingCode(InspectCore(files), "stale-pending-before-replace");
    }

    // pending과 post-state와 success log가 모두 있는 cleanup window를 검증한다.
    private static bool CasePendingPostSuccess(string root)
    {
        var files = CreateFixture(root, stateHasPost: true, logHasPost: true);
        WritePending(files, "RX-1");
        var report = InspectCore(files);
        return HasPendingCode(report, "completed-pending-cleanup-available");
    }

    // state 적용 후 success log 누락 window를 검증한다.
    private static bool CasePendingPostNoSuccess(string root)
    {
        var files = CreateFixture(root, stateHasPost: true, logHasPost: false);
        WritePending(files, "RX-1");
        return HasPendingCode(InspectCore(files), "pending-transition-recovery-required");
    }

    // pending hash와 현재 state가 맞지 않는 모호 상태를 검증한다.
    private static bool CasePendingAmbiguous(string root)
    {
        var files = CreateFixture(root, stateHasPost: false, logHasPost: false);
        WritePending(files, "RX-1", preHash: new string('8', 64), postHash: new string('9', 64));
        var report = InspectCore(files);
        return HasPendingCode(report, "pending-state-ambiguous");
    }

    // state-only transition gap을 recovery 대상으로 분류하는지 검증한다.
    private static bool CaseStateOnlyGap(string root)
    {
        var files = CreateFixture(root, stateHasPost: true, logHasPost: false);
        var report = InspectCore(files);
        return HasFailureCode(report, "state-transition-not-logged");
    }

    // conflicting success binding을 자동 복구 금지로 분류하는지 검증한다.
    private static bool CaseConflict(string root)
    {
        var files = CreateFixture(root, stateHasPost: true, logHasPost: true, conflict: true);
        var report = InspectCore(files);
        return HasFailureCode(report, "duplicate-success-log-conflict");
    }

    // quarantine evidence package가 state/log를 바꾸지 않고 생성되는지 검증한다.
    private static bool CaseEvidencePackage(string root)
    {
        var files = CreateFixture(root, stateHasPost: true, logHasPost: false);
        var beforeState = Sha256(File.ReadAllBytes(files.WorkstatePath));
        var beforeLog = Sha256(File.ReadAllBytes(files.LogPath));
        var outDir = Path.Combine(root, "quarantine");
        WriteEvidencePackage(files, InspectCore(files), outDir);
        return File.Exists(Path.Combine(outDir, "recovery-request.json"))
            && File.Exists(Path.Combine(outDir, "hard-rollback.json"))
            && beforeState == Sha256(File.ReadAllBytes(files.WorkstatePath))
            && beforeLog == Sha256(File.ReadAllBytes(files.LogPath));
    }

    // 06H 이후에도 high-risk recovery readiness가 false인지 검증한다.
    private static bool CaseHighRiskClosed(string root)
    {
        var files = CreateFixture(root, stateHasPost: false, logHasPost: false);
        var report = InspectCore(files);
        return report["recoveryApplyReady"]?.GetValue<bool>() == false
            && report["highRiskTransitionReady"]?.GetValue<bool>() == false
            && report["automatedExecutionReady"]?.GetValue<bool>() == false;
    }

    // RECOVERY 진단 report를 생성한다.
    private static JsonObject InspectCore(RecoveryFiles files)
    {
        var recon = HandoffIntegrityChecker.Run(new ReconciliationOptions(files.WorkstatePath, files.LogPath));
        var pending = PendingArray(files);
        return new JsonObject
        {
            ["command"] = "recovery inspect",
            ["recoveryApplyReady"] = false,
            ["highRiskTransitionReady"] = false,
            ["automatedExecutionReady"] = false,
            ["trustedBaselineRequired"] = true,
            ["reconciliationExitCode"] = recon.Failures.Count == 0 && recon.HarnessErrors.Count == 0 ? 0 : recon.HarnessErrors.Count > 0 ? 2 : 1,
            ["recommendedAction"] = recon.Failures.Count == 0 && pending.Count == 0 ? "none" : "quarantine-and-human-inbox",
            ["failures"] = FailureArray(recon.Failures),
            ["pending"] = pending,
            ["warnings"] = new JsonArray(recon.Warnings.Select(w => (JsonNode)new JsonObject { ["code"] = w.Code, ["subject"] = w.Subject }).ToArray()),
        };
    }

    // pending directory 전체를 분류한다.
    private static JsonArray PendingArray(RecoveryFiles files)
    {
        if (!Directory.Exists(files.PendingDir)) return [];
        var nodes = Directory.GetFiles(files.PendingDir, "*.json").OrderBy(p => p).Select(p => PendingNode(files, p)).ToArray();
        return new JsonArray(nodes);
    }

    // 단일 pending record를 crash window code로 분류한다.
    private static JsonNode PendingNode(RecoveryFiles files, string path)
    {
        var pending = ReadPending(path);
        if (pending is null) return new JsonObject { ["path"] = path, ["code"] = "pending-state-ambiguous" };
        var currentHash = Sha256(File.ReadAllBytes(files.WorkstatePath));
        var hasSuccess = HasMatchingSuccess(files, pending);
        var code = ClassifyPending(currentHash, hasSuccess, pending);
        return new JsonObject { ["path"] = path, ["transitionId"] = pending.TransitionId, ["code"] = code };
    }

    // pending hash와 success log 존재 여부로 crash window를 판정한다.
    private static string ClassifyPending(string currentHash, bool hasSuccess, PendingInfo pending)
    {
        if (currentHash == pending.PreStateSha256 && !hasSuccess) return "stale-pending-before-replace";
        if (currentHash == pending.PostStateSha256 && hasSuccess) return "completed-pending-cleanup-available";
        if (currentHash == pending.PostStateSha256 && !hasSuccess) return "pending-transition-recovery-required";
        return "pending-state-ambiguous";
    }

    // recovery evidence package를 지정 경로에 생성한다.
    private static void WriteEvidencePackage(RecoveryFiles files, JsonObject report, string outDir)
    {
        Directory.CreateDirectory(outDir);
        WriteJson(Path.Combine(outDir, "quarantine-manifest.json"), EvidenceManifest(files, report));
        WriteJson(Path.Combine(outDir, "recovery-request.json"), RecoveryRequest(report));
        WriteJson(Path.Combine(outDir, "hard-rollback.json"), HardRollback(files));
    }

    // quarantine manifest JSON을 만든다.
    private static JsonObject EvidenceManifest(RecoveryFiles files, JsonObject report) => new()
    {
        ["schemaVersion"] = 1,
        ["kind"] = "RECOVERY_QUARANTINE_EVIDENCE",
        ["workstateSha256"] = Sha256(File.ReadAllBytes(files.WorkstatePath)),
        ["applierLogSha256"] = Sha256(File.ReadAllBytes(files.LogPath)),
        ["stateMutated"] = false,
        ["logMutated"] = false,
        ["report"] = report.DeepClone(),
    };

    // 실행 불가 recovery request 초안을 만든다.
    private static JsonObject RecoveryRequest(JsonObject report) => new()
    {
        ["schemaVersion"] = 1,
        ["kind"] = "RECOVERY_REQUEST_DRAFT",
        ["status"] = "DRAFT_NOT_EXECUTABLE",
        ["requiresTrustedBaseline"] = true,
        ["requiresVerifiedHumanReceipt"] = true,
        ["recoveryApplyReady"] = false,
        ["report"] = report.DeepClone(),
    };

    // hard rollback 판단에 필요한 원본 hash 자료를 만든다.
    private static JsonObject HardRollback(RecoveryFiles files) => new()
    {
        ["schemaVersion"] = 1,
        ["kind"] = "HARD_ROLLBACK_EVIDENCE",
        ["workstatePath"] = Path.GetRelativePath(files.Root, files.WorkstatePath).Replace('\\', '/'),
        ["applierLogPath"] = Path.GetRelativePath(files.Root, files.LogPath).Replace('\\', '/'),
        ["workstateSha256"] = Sha256(File.ReadAllBytes(files.WorkstatePath)),
        ["applierLogSha256"] = Sha256(File.ReadAllBytes(files.LogPath)),
        ["automaticRollbackAllowed"] = false,
    };

    // fixture state/log/pending 경로를 구성한다.
    private static RecoveryFiles CreateFixture(string root, bool stateHasPost, bool logHasPost, bool conflict = false)
    {
        var files = new RecoveryFiles(root, Path.Combine(root, "WORKSTATE.json"), Path.Combine(root, "applier-log.jsonl"), Path.Combine(root, "pending"));
        var preHash = Sha256(Encoding.UTF8.GetBytes(StateJson(false)));
        var postHash = Sha256(Encoding.UTF8.GetBytes(StateJson(true)));
        File.WriteAllText(files.WorkstatePath, StateJson(stateHasPost), Utf8NoBom);
        var log = SuccessLine("BASE", preHash, preHash) + "\n";
        if (logHasPost) log += SuccessLine("RX-1", preHash, postHash) + "\n";
        File.WriteAllText(files.LogPath, log, Utf8NoBom);
        if (conflict) File.AppendAllText(files.LogPath, SuccessLine("RX-1", preHash, postHash, "a") + "\n", Utf8NoBom);
        return files;
    }

    // fixture WORKSTATE JSON을 만든다.
    private static string StateJson(bool includePost)
    {
        var ids = new JsonArray(new JsonObject { ["id"] = "BASE", ["appliedAt"] = "2026-01-01T00:00:00Z" });
        if (includePost) ids.Add(new JsonObject { ["id"] = "RX-1", ["appliedAt"] = "2026-01-01T00:01:00Z" });
        return new JsonObject { ["schemaVersion"] = 3, ["phaseId"] = "P00", ["wpId"] = "WP-STATE-INTEGRITY", ["diId"] = "06H", ["status"] = "blocked", ["appliedTransitions"] = ids }.ToJsonString(JsonOptions);
    }

    // fixture v2 success log line을 만든다.
    private static string SuccessLine(string id, string preHash, string postHash, string seed = "1") => new JsonObject
    {
        ["schemaVersion"] = 2,
        ["status"] = "success",
        ["result"] = "ok",
        ["exitCode"] = 0,
        ["transitionId"] = id,
        ["transitionKind"] = "NORMAL",
        ["requestSha256"] = new string(seed[0], 64),
        ["preStateSha256"] = preHash,
        ["postStateSha256"] = postHash,
        ["effectiveAt"] = "2026-01-01T00:00:00Z",
        ["transitionContractSha256"] = new string('4', 64),
        ["at"] = "2026-01-01T00:00:00Z",
    }.ToJsonString();

    // fixture pending journal record를 쓴다.
    private static void WritePending(RecoveryFiles files, string id, string? preHash = null, string? postHash = null)
    {
        Directory.CreateDirectory(files.PendingDir);
        var pre = preHash ?? Sha256(Encoding.UTF8.GetBytes(StateJson(false)));
        var post = postHash ?? Sha256(Encoding.UTF8.GetBytes(StateJson(true)));
        WriteJson(Path.Combine(files.PendingDir, id + ".json"), new JsonObject
        {
            ["schemaVersion"] = 1,
            ["transitionId"] = id,
            ["requestSha256"] = new string('1', 64),
            ["preStateSha256"] = pre,
            ["postStateSha256"] = post,
            ["transitionContractSha256"] = new string('4', 64),
        });
    }

    // pending record를 읽는다.
    private static PendingInfo? ReadPending(string path)
    {
        try
        {
            var obj = JsonNode.Parse(File.ReadAllText(path, Utf8NoBom))?.AsObject();
            if (obj is null) return null;
            return new PendingInfo(path, Read(obj, "transitionId"), Read(obj, "requestSha256"), Read(obj, "preStateSha256"), Read(obj, "postStateSha256"), Read(obj, "transitionContractSha256"));
        }
        catch { return null; }
    }

    // pending binding과 같은 v2 success log가 있는지 확인한다.
    private static bool HasMatchingSuccess(RecoveryFiles files, PendingInfo pending)
    {
        var result = HandoffIntegrityChecker.Run(new ReconciliationOptions(files.WorkstatePath, files.LogPath, pending.TransitionId));
        return result.SuccessLookup.TryGetValue(pending.TransitionId, out var lookup)
            && lookup.RequestSha256 == pending.RequestSha256
            && lookup.PreStateSha256 == pending.PreStateSha256
            && lookup.PostStateSha256 == pending.PostStateSha256
            && lookup.TransitionContractSha256 == pending.TransitionContractSha256;
    }

    // CLI 인자에서 검사 대상 파일 경로를 만든다.
    private static RecoveryFiles ParseFiles(string[] args, string root)
    {
        var ws = Flag(args, "workstate");
        var log = Flag(args, "applier-log");
        var pending = Flag(args, "pending-dir");
        return new RecoveryFiles(root, Resolve(root, ws, "docs/handoff/WORKSTATE.json"), Resolve(root, log, "docs/handoff/WORKSTATE.applier-log.jsonl"), Resolve(root, pending, ".state-applier/pending"));
    }

    // recovery report의 exit code를 계산한다.
    private static int ExitForReport(JsonObject report)
    {
        var code = report["reconciliationExitCode"]?.GetValue<int>() ?? 2;
        var pending = report["pending"] as JsonArray ?? [];
        return code == 2 ? 2 : code == 1 || pending.Count > 0 ? 1 : 0;
    }

    // reconciliation failure를 JSON으로 변환한다.
    private static JsonArray FailureArray(List<ReconciliationEntry> failures) => new(failures.Select(f => (JsonNode)new JsonObject { ["code"] = f.Code, ["subject"] = f.Subject, ["recoveryClass"] = RecoveryClass(f.Code) }).ToArray());

    // failure code별 recovery class를 반환한다.
    private static string RecoveryClass(string code) => code switch
    {
        "log-transition-missing-from-state" => "L2",
        "state-transition-not-logged" => "L2",
        "transition-id-collision" => "L3",
        "duplicate-success-log-conflict" => "L3",
        "duplicate-in-state" => "L3",
        "legacy-idempotency-unverifiable" => "L3",
        _ => "L4",
    };

    // report에 특정 failure code가 있는지 확인한다.
    private static bool HasFailureCode(JsonObject report, string code) => (report["failures"] as JsonArray ?? []).OfType<JsonObject>().Any(f => f["code"]?.ToString() == code);

    // report에 특정 pending code가 있는지 확인한다.
    private static bool HasPendingCode(JsonObject report, string code) => (report["pending"] as JsonArray ?? []).OfType<JsonObject>().Any(f => f["code"]?.ToString() == code);

    // JSON 파일을 UTF-8 no BOM으로 쓴다.
    private static void WriteJson(string path, JsonObject obj) => File.WriteAllText(path, obj.ToJsonString(JsonOptions), Utf8NoBom);

    // JsonObject 문자열 필드를 읽는다.
    private static string Read(JsonObject obj, string key) => obj[key]?.GetValue<string>() ?? "";

    // 경로 인자를 repo root 기준 절대 경로로 바꾼다.
    private static string Resolve(string root, string value, string fallback)
    {
        var selected = string.IsNullOrWhiteSpace(value) ? fallback : value;
        return Path.GetFullPath(Path.IsPathRooted(selected) ? selected : Path.Combine(root, selected));
    }

    // SHA-256 hex를 계산한다.
    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    // CLI flag 값을 찾는다.
    private static string Flag(string[] args, string name) { for (var i = 0; i + 1 < args.Length; i++) if (args[i] == "--" + name) return args[i + 1]; return ""; }

    // self-test case 결과를 배열에 추가한다.
    private static void Add(JsonArray cases, string name, bool pass) => cases.Add(new JsonObject { ["case"] = name, ["pass"] = pass });

    // JSON 오류를 stderr로 출력한다.
    private static int Error(string message, int code) { Console.Error.WriteLine(new JsonObject { ["error"] = message }.ToJsonString()); return code; }

    // 임시 fixture 디렉터리를 삭제한다.
    private static void TryDeleteDirectory(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { } }
}
