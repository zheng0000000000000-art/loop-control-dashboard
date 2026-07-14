// trust-origin 신뢰 원점 부트스트랩 선언 CLI.
// declare: 선행 10조건 검사 후 trust-origin record를 생성한다. WORKSTATE·applier-log는 수정하지 않는다.
// --self-test: $TEMP 사본에서 18개 case를 in-process로 검증한다.
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class TrustOriginCli
{
    // record 고정 경로 — 이 밖 경로에는 절대 쓰지 않는다.
    private const string RecordRelPath = "docs/handoff/trust-origin/TO-2026-001.json";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // knownException 항목 데이터 레코드.
    private record KnownException(string Code, string Subject, string What, string Why, string WhyNotReplayed);

    // 자기 시험 전용 precondition 주입 레코드 — CLI·환경변수로 켤 수 없음.
    internal record PreconOverride(
        bool BuildVerified, bool FilesTracked, bool NoDirectMod,
        string CommitHash, bool AutoLauncherOff, bool HighRiskClosed);

    // 자기 시험 seam — in-process 훅만. production 진입점에서 설정 불가.
    internal static bool NonInteractiveOverride;
    internal static PreconOverride? TestPrecon;

    // trust-origin 진입점.
    internal static int Run(string[] args)
    {
        var sub = args.Length > 1 ? args[1] : "";
        if (string.Equals(sub, "declare", StringComparison.OrdinalIgnoreCase))
            return RunDeclare(args);
        if (string.Equals(sub, "--self-test", StringComparison.OrdinalIgnoreCase))
            return RunSelfTest();
        Console.Error.WriteLine(
            "{\"error\":\"trust-origin usage: declare --ack BOOTSTRAP_TRUST_ORIGIN [--known-exceptions-file <path>] | --self-test\"}");
        return 2;
    }

    // declare: 비대화형 거부(약한 안전장치), ack 확인, precondition 검사, record 생성.
    private static int RunDeclare(string[] args)
    {
        // 약한 안전장치(신원 증명 아님) — 비대화형 실행 기본 거부.
        // 프로그램은 사람과 AI를 구분 못 한다(HUMAN_DECLARED_NOT_CRYPTOGRAPHICALLY_VERIFIED).
        if (!NonInteractiveOverride && (Console.IsInputRedirected || !Environment.UserInteractive))
        {
            Console.Error.WriteLine(
                "{\"error\":\"비대화형 실행 기본 거부\",\"code\":\"non-interactive-rejected\","
                + "\"note\":\"HUMAN_DECLARED_NOT_CRYPTOGRAPHICALLY_VERIFIED — 프로그램은 사람과 AI를 구분하지 못합니다\"}");
            return 1;
        }
        var map = ParseFlagMap(args, 2);
        if (!map.TryGetValue("ack", out var ackVal) ||
            !string.Equals(ackVal, "BOOTSTRAP_TRUST_ORIGIN", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("{\"error\":\"--ack BOOTSTRAP_TRUST_ORIGIN 필요\",\"code\":\"ack-missing\"}");
            return 1;
        }
        string root;
        try { root = GitTools.FindRepoRoot(); }
        catch (Exception ex) { WriteError($"repo root 탐색 실패: {ex.Message}"); return 2; }

        List<KnownException> knownExceptions = [];
        if (map.TryGetValue("known-exceptions-file", out var efile))
        {
            try { knownExceptions = ParseKnownExceptions(JsonNode.Parse(File.ReadAllText(efile, new UTF8Encoding(false)))); }
            catch (Exception ex) { WriteError($"known-exceptions-file 읽기 실패: {ex.Message}"); return 2; }
        }
        return RunDeclareCore(root, knownExceptions);
    }

    // declare 핵심 — root를 명시적으로 받아 자기 시험과 production 경로를 공유한다.
    private static int RunDeclareCore(string root, List<KnownException> knownExceptions)
    {
        var recordPath = Path.GetFullPath(
            Path.Combine(root, RecordRelPath.Replace('/', Path.DirectorySeparatorChar)));
        var wsPath = Path.Combine(root, "docs", "handoff", "WORKSTATE.json");
        var logPath = Path.Combine(root, "docs", "handoff", "WORKSTATE.applier-log.jsonl");
        var precon = TestPrecon;

        // 기존 epoch 확인: trustEpoch >= 1 이면 거부.
        var epochExit = CheckEpochGuard(recordPath);
        if (epochExit.HasValue) return epochExit.Value;

        // WORKSTATE 읽기.
        if (!File.Exists(wsPath)) { WriteError("WORKSTATE.json not found"); return 2; }
        string wsContent;
        try { wsContent = File.ReadAllText(wsPath, new UTF8Encoding(false)); }
        catch (Exception ex) { WriteError($"WORKSTATE.json 읽기 실패: {ex.Message}"); return 2; }

        // 선행조건 2: reconciliation 실행 + 부분집합 검사.
        var (reconResult, reconExit) = RunReconciliationCheck(wsPath, logPath, knownExceptions);
        if (reconExit.HasValue) return reconExit.Value;

        // 선행조건 1·3·4·9·10 검사.
        var preconExit = CheckAllPreconditions(root, precon);
        if (preconExit.HasValue) return preconExit.Value;

        // 선행조건 5: WORKSTATE hash / 선행조건 6: applier-log hash.
        var workstateSha256 = ComputeSha256(wsContent);
        var logContent = File.Exists(logPath) ? File.ReadAllText(logPath, new UTF8Encoding(false)) : "";
        var applierLogSha256 = ComputeSha256(logContent);

        // 선행조건 7: baseline commit hash.
        var (baselineCommit, commitExit) = GetBaselineCommit(root, precon);
        if (commitExit.HasValue) return commitExit.Value;

        // record 생성 — declarationCommit 없음(자기참조 방지). 이 경로 외에는 쓰지 않는다.
        return BuildAndWriteRecord(
            recordPath, baselineCommit!, workstateSha256, applierLogSha256, reconResult!, knownExceptions);
    }

    // 기존 epoch 확인 — trustEpoch >= 1 이면 exit 1, 없으면 null.
    private static int? CheckEpochGuard(string recordPath)
    {
        if (!File.Exists(recordPath)) return null;
        try
        {
            var existing = JsonNode.Parse(File.ReadAllText(recordPath))?.AsObject();
            if ((existing?["trustEpoch"]?.GetValue<int>() ?? 0) >= 1)
            {
                WriteError("trustEpoch >= 1 이미 존재 — 재선언 거부. 새 trust origin은 VERIFIED_HUMAN_APPROVAL receipt 필요");
                return 1;
            }
        }
        catch { /* 파싱 불가 시 아래 CreateNew에서 처리 */ }
        return null;
    }

    // reconciliation을 실행하고 failure ⊆ knownExceptions 부분집합 검사를 수행한다.
    private static (ReconciliationResult? result, int? exitCode) RunReconciliationCheck(
        string wsPath, string logPath, List<KnownException> knownExceptions)
    {
        ReconciliationResult reconResult;
        try { reconResult = HandoffIntegrityChecker.Run(new ReconciliationOptions(wsPath, logPath)); }
        catch (Exception ex) { WriteError($"reconciliation 실행 실패: {ex.Message}"); return (null, 2); }

        // 하네스 오류 시 별도 중단 — subject가 없어 부분집합 검사를 우회할 수 있다.
        if (reconResult.HarnessErrors.Count > 0)
        {
            var errs = string.Join("; ", reconResult.HarnessErrors.Select(e => e.Code));
            WriteError($"reconciliation 하네스 오류 — 선행조건 2 검증 불가: {errs}");
            return (null, 2);
        }

        // 정확한 집합 검사: failures == knownExceptions. 미명시 failure 또는 extra known exception → 선언 거부.
        var failureSubjects = reconResult.Failures
            .Select(f => f.Subject).Where(s => !string.IsNullOrWhiteSpace(s))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var knownSubjects = knownExceptions.Select(e => e.Subject).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unlisted = failureSubjects.Except(knownSubjects).ToList();
        var extraKnown = knownSubjects.Except(failureSubjects).ToList();
        if (unlisted.Count > 0 || extraKnown.Count > 0)
        {
            Console.Error.WriteLine(new JsonObject
            {
                ["error"] = "knownExceptions가 실제 failure와 정확히 일치하지 않음 — 선언 거부",
                ["code"] = unlisted.Count > 0 ? "unlisted-reconciliation-failure" : "extra-known-exceptions",
                ["unlistedSubjects"] = new JsonArray(unlisted.Select(s => (JsonNode?)s).ToArray()),
                ["extraKnownSubjects"] = new JsonArray(extraKnown.Select(s => (JsonNode?)s).ToArray()),
            }.ToJsonString());
            return (null, 1);
        }
        return (reconResult, null);
    }

    // 선행조건 1·3·4·9·10을 검사한다. production 경로(precon=null)에서는 실제 확인을 수행한다. 미충족 시 exit code 반환.
    private static int? CheckAllPreconditions(string root, PreconOverride? precon)
    {
        // 선행조건 1: production에서 실제 dotnet build 실행. 기본값 true 금지.
        var buildOk = precon?.BuildVerified ?? CheckBuildProduction(root);
        if (!buildOk)
            { WriteError("선행조건 1 미충족 — clean build exit 0 필요 (dotnet build server -c Release -nologo)"); return 1; }
        var filesTracked = precon?.FilesTracked ?? CheckFilesTracked(root);
        if (!filesTracked)
            { WriteError("선행조건 3 미충족 — StateApplierCli.cs 또는 HandoffIntegrityChecker.cs가 git-tracked 상태가 아님"); return 1; }
        var noDirectMod = precon?.NoDirectMod ?? CheckNoDirectMod(root);
        if (!noDirectMod)
            { WriteError("선행조건 4 미충족 — WORKSTATE.json 또는 applier-log.jsonl가 working tree에서 직접 수정됨"); return 1; }
        // 선행조건 9: production에서 .claude/settings*.json hooks 확인. 기본값 true 금지.
        var autoOff = precon?.AutoLauncherOff ?? CheckAutoLauncherOff(root);
        if (!autoOff)
            { WriteError("선행조건 9 미충족 — 자동 launcher hooks 발견 또는 확인 불가"); return 1; }
        // 선행조건 10: production에서 PHASE_CHANGE·RECOVERY·REPLAY 세 kind를 full envelope로 apply → trusted-human-receipt-required exit 1 확인. 기본값 true 금지.
        var highRiskClosed = precon?.HighRiskClosed ?? CheckHighRiskClosed(root);
        if (!highRiskClosed)
            { WriteError("선행조건 10 미충족 — high-risk transition(PHASE_CHANGE/RECOVERY/REPLAY)이 fail-closed 되지 않음 또는 확인 불가"); return 1; }
        return null;
    }

    // 선행조건 1: dotnet build server -c Release -nologo 실행 후 exit 0 여부를 반환한다.
    private static bool CheckBuildProduction(string root)
    {
        try
        {
            using var p = new Process();
            p.StartInfo = new ProcessStartInfo("dotnet", "build server -c Release -nologo")
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            p.Start();
            p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            p.WaitForExit(120_000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    // 선행조건 9: .claude/settings*.json에서 hooks 필드 유무로 자동 launcher 비활성을 판단한다. 파일 있음 + malformed JSON → fail-closed. hooks 필드가 존재하면 값이 무엇이든 fail-closed.
    private static bool CheckAutoLauncherOff(string root)
    {
        foreach (var path in GetClaudeSettingsPaths(root))
        {
            if (!File.Exists(path)) continue;
            try
            {
                var node = JsonNode.Parse(File.ReadAllText(path, new System.Text.UTF8Encoding(false)));
                if (node is JsonObject obj && obj.ContainsKey("hooks")) return false;
            }
            catch { return false; } // 파싱 불가 → 확인 불가 → fail-closed
        }
        return true;
    }

    // 선행조건 9 보조: 검사 대상 Claude settings 경로 목록.
    private static System.Collections.Generic.IEnumerable<string> GetClaudeSettingsPaths(string root)
    {
        yield return Path.Combine(root, ".claude", "settings.json");
        yield return Path.Combine(root, ".claude", "settings.local.json");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home))
        {
            yield return Path.Combine(home, ".claude", "settings.json");
            yield return Path.Combine(home, ".claude", "settings.local.json");
        }
    }

    // 선행조건 10: PHASE_CHANGE·RECOVERY·REPLAY 세 kind를 full envelope로 apply하여 모두 exit 1이고 trusted-human-receipt-required reason이 있는지 확인한다.
    private static bool CheckHighRiskClosed(string root)
    {
        foreach (var kind in new[] { "PHASE_CHANGE", "RECOVERY", "REPLAY" })
        {
            var (exit, _, reasonMatched) = RunHighRiskEnvelopeCheck(kind);
            if (exit != 1 || !reasonMatched) return false;
        }
        return true;
    }

    // 지정 kind의 full envelope로 state-transition apply를 실행하고 exit code·stderr 내용·reason 일치 여부를 반환한다.
    // 주의: StateApplierCli.RunApply는 canonical repo root WORKSTATE를 로드한다. high-risk 분기는 그 이후 request/candidate 파일 읽기 전에 실행된다.
    private static (int exit, string stderrText, bool reasonMatched) RunHighRiskEnvelopeCheck(string kind)
    {
        var tmpEnv = Path.Combine(Path.GetTempPath(), $"to-hr-{kind}-{Guid.NewGuid():N}.json");
        try
        {
            // full envelope — 필수 필드 모두 포함. 플레이스홀더 해시·경로 OK: high-risk 분기는 파일 읽기 전에 반환한다.
            File.WriteAllText(tmpEnv,
                "{\"schemaVersion\":1,\"transitionKind\":\"" + kind + "\","
                + "\"transitionId\":\"trust-origin-high-risk-check\","
                + "\"expectedPreStateSha256\":\"0000000000000000000000000000000000000000000000000000000000000001\","
                + "\"requestPath\":\"placeholder\","
                + "\"requestSha256\":\"0000000000000000000000000000000000000000000000000000000000000002\","
                + "\"effectiveAt\":\"2026-01-01T00:00:00Z\","
                + "\"expectedPostStateSha256\":\"0000000000000000000000000000000000000000000000000000000000000003\","
                + "\"candidatePath\":\"placeholder\"}",
                new System.Text.UTF8Encoding(false));
            var origErr = Console.Error;
            var errSb = new StringBuilder();
            using var errWriter = new System.IO.StringWriter(errSb);
            Console.SetError(errWriter);
            int exit;
            try { exit = StateApplierCli.Run(["state-transition", "apply", "--envelope", tmpEnv]); }
            finally { Console.SetError(origErr); }
            var stderrText = errSb.ToString();
            // stderr 각 줄을 JSON으로 파싱해 reason 또는 code 필드가 정확히 "trusted-human-receipt-required"인지 확인한다.
            var reasonMatched = stderrText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Any(line =>
                {
                    try
                    {
                        var node = JsonNode.Parse(line.Trim())?.AsObject();
                        if (node is null) return false;
                        var reason = node["reason"]?.GetValue<string>();
                        var code = node["code"]?.GetValue<string>();
                        return string.Equals(reason, "trusted-human-receipt-required", StringComparison.Ordinal)
                            || string.Equals(code, "trusted-human-receipt-required", StringComparison.Ordinal);
                    }
                    catch { return false; }
                });
            return (exit, stderrText, reasonMatched);
        }
        catch { return (-1, "", false); }
        finally
        {
            try { if (File.Exists(tmpEnv)) File.Delete(tmpEnv); } catch { }
        }
    }

    // 선행조건 7: baseline commit hash를 반환한다. 시험은 TestPrecon, production은 git.
    private static (string? commit, int? exitCode) GetBaselineCommit(string root, PreconOverride? precon)
    {
        if (precon is not null) return (precon.CommitHash, null);
        try { return (RunGitOutput(root, "rev-parse HEAD").Trim(), null); }
        catch (Exception ex) { WriteError($"git rev-parse HEAD 실패: {ex.Message}"); return (null, 2); }
    }

    // record를 구성하고 고정 경로에 atomic-create로 쓴다.
    private static int BuildAndWriteRecord(
        string recordPath, string baselineCommit, string workstateSha256, string applierLogSha256,
        ReconciliationResult reconResult, List<KnownException> knownExceptions)
    {
        var reconVerdict = reconResult.Failures.Count == 0
            ? "VERIFIED_PASS" : "VERIFIED_PASS_WITH_KNOWN_EXCEPTIONS";
        // declarationCommit 필드 미포함 — 자기참조 방지. annotated tag로 연결.
        var record = new JsonObject
        {
            ["schemaVersion"] = 1, ["trustOriginId"] = "TO-2026-001", ["trustEpoch"] = 1,
            ["declarationType"] = "BOOTSTRAP_TRUST_ORIGIN", ["baselineCommit"] = baselineCommit,
            ["workstateSha256"] = workstateSha256, ["applierLogSha256"] = applierLogSha256,
            ["stateApplierSchemaVersion"] = 2, ["reconciliationSchemaVersion"] = 2,
            ["legacyHistory"] = "NOT_EXACTLY_REPLAY_VERIFIED",
            ["buildVerdict"] = "VERIFIED_PASS", ["reconciliationVerdict"] = reconVerdict,
            ["knownExceptions"] = BuildExceptionsArray(knownExceptions),
            ["normalTransitionReady"] = true, ["verifiedHumanApprovalReady"] = false,
            ["recoveryApplyReady"] = false, ["automatedExecutionReady"] = false,
            ["phaseChangeReady"] = false, ["replayReady"] = false,
            ["declaredBy"] = new JsonObject
            {
                ["actorType"] = "human", ["actorId"] = "bootstrap-operator",
                ["actorPath"] = "local-manual",
            },
            ["declarationStatus"] = "HUMAN_DECLARED_NOT_CRYPTOGRAPHICALLY_VERIFIED",
            ["declaredAt"] = DateTime.UtcNow.ToString("o"),
        };
        Directory.CreateDirectory(Path.GetDirectoryName(recordPath)!);
        try
        {
            using var fs = new FileStream(recordPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            fs.Write(Encoding.UTF8.GetBytes(record.ToJsonString(WriteOptions)));
        }
        catch (IOException ex) { WriteError($"record atomic-create 실패 — 이미 존재하거나 쓰기 불가: {ex.Message}"); return 1; }

        Console.WriteLine(new JsonObject
        {
            ["ok"] = true, ["trustOriginId"] = "TO-2026-001", ["trustEpoch"] = 1,
            ["baselineCommit"] = baselineCommit, ["workstateSha256"] = workstateSha256,
            ["applierLogSha256"] = applierLogSha256, ["recordPath"] = recordPath,
            ["reconciliationVerdict"] = reconVerdict,
            ["note"] = "선언 완료. 다음: git commit -m '[trust-origin] bootstrap TO-2026-001' → git tag trust-origin/TO-2026-001 <commitHash>",
        }.ToJsonString(WriteOptions));
        return 0;
    }

    // --self-test: $TEMP 사본에서 18개 case를 in-process로 검증한다.
    private static int RunSelfTest()
    {
        var tmpBase = Path.Combine(Path.GetTempPath(), $"to-selftest-{Guid.NewGuid():N}");
        try { return RunSelfTestInDir(tmpBase); }
        finally
        {
            try { if (Directory.Exists(tmpBase)) Directory.Delete(tmpBase, recursive: true); } catch { }
            NonInteractiveOverride = false;
            TestPrecon = null;
        }
    }

    // 임시 디렉토리에서 18개 case를 순서대로 실행하고 결과를 보고한다.
    private static int RunSelfTestInDir(string tmpRoot)
    {
        var wsDir = Path.Combine(tmpRoot, "docs", "handoff");
        var trustDir = Path.Combine(tmpRoot, "docs", "handoff", "trust-origin");
        Directory.CreateDirectory(wsDir);
        Directory.CreateDirectory(trustDir);
        Directory.CreateDirectory(Path.Combine(tmpRoot, ".git")); // FindRepoRoot 기준점
        var wsPath = Path.Combine(wsDir, "WORKSTATE.json");
        var logPath = Path.Combine(wsDir, "WORKSTATE.applier-log.jsonl");
        var recordPath = Path.Combine(trustDir, "TO-2026-001.json");

        // knownExceptions에 명시된 오염 — LISTED-TRANSITION이 state에 있고 log에 없음 (declare-ok용)
        const string WsDirty = "{\"schemaVersion\":1,\"diId\":\"DI-00-04\",\"status\":\"in_progress\","
            + "\"blockers\":[],\"nextActions\":[\"test\"],\"phaseId\":\"P00\",\"wpId\":\"WP-00\","
            + "\"updatedAt\":\"2026-07-14\",\"updatedBy\":\"self-test\","
            + "\"appliedTransitions\":[{\"id\":\"LISTED-TRANSITION\",\"appliedAt\":\"2026-07-14T00:00:00Z\"}]}";
        // 깨끗한 WORKSTATE — appliedTransitions 없음 → reconciliation PASS
        const string WsClean = "{\"schemaVersion\":1,\"diId\":\"DI-00-04\",\"status\":\"in_progress\","
            + "\"blockers\":[],\"nextActions\":[\"test\"],\"phaseId\":\"P00\",\"wpId\":\"WP-00\","
            + "\"updatedAt\":\"2026-07-14\",\"updatedBy\":\"self-test\",\"appliedTransitions\":[]}";
        // 미명시 오염 — UNLISTED-TRANSITION이 state·log·knownExceptions 어디에도 없음 (unlisted-failure용)
        const string WsUnlisted = "{\"schemaVersion\":1,\"diId\":\"DI-00-04\",\"status\":\"in_progress\","
            + "\"blockers\":[],\"nextActions\":[\"test\"],\"phaseId\":\"P00\",\"wpId\":\"WP-00\","
            + "\"updatedAt\":\"2026-07-14\",\"updatedBy\":\"self-test\","
            + "\"appliedTransitions\":[{\"id\":\"UNLISTED-TRANSITION\",\"appliedAt\":\"2026-07-14T00:00:00Z\"}]}";

        var goodPrecon = new PreconOverride(true, true, true, "abc123def456abc123def456abc123def456abc1", true, true);
        var mismatches = new JsonArray();
        var caseResults = new JsonArray();
        // 실행 순서: declare-ok → no-self-reference → extra-known-exception → unlisted-failure → redeclare → record-path-fixed → record-ready-flags-complete → build-verdict-not-forged → production-preconditions-not-default-true → high-risk 3종 → launcher-settings-malformed → launcher hooks 형태 5종
        RunCaseDeclareOk(tmpRoot, wsPath, logPath, recordPath, WsDirty, goodPrecon, caseResults, mismatches);
        RunCaseNoSelfReference(recordPath, caseResults, mismatches);
        RunCaseExtraKnownException(tmpRoot, wsPath, logPath, recordPath, WsDirty, goodPrecon, caseResults, mismatches);
        RunCaseUnlistedFailure(tmpRoot, wsPath, logPath, recordPath, WsUnlisted, goodPrecon, caseResults, mismatches);
        RunCaseRedeclare(tmpRoot, wsPath, logPath, recordPath, WsClean, goodPrecon, caseResults, mismatches);
        RunCaseRecordPathFixed(tmpRoot, wsPath, logPath, recordPath, WsClean, goodPrecon, caseResults, mismatches);
        RunCaseRecordReadyFlagsComplete(recordPath, caseResults, mismatches);
        RunCaseBuildVerdictNotForged(tmpRoot, wsPath, logPath, recordPath, WsClean, goodPrecon, caseResults, mismatches);
        RunCaseProductionPreconNotDefaultTrue(tmpRoot, wsPath, logPath, recordPath, WsClean, caseResults, mismatches);
        RunCaseHighRiskFullEnvelope("PHASE_CHANGE", caseResults, mismatches);
        RunCaseHighRiskFullEnvelope("RECOVERY", caseResults, mismatches);
        RunCaseHighRiskFullEnvelope("REPLAY", caseResults, mismatches);
        RunCaseLauncherSettingsMalformed(tmpRoot, caseResults, mismatches);
        RunCaseLauncherSettingsHooksVariant(tmpRoot, "launcher-settings-hooks-empty-object", "{\"hooks\":{}}", false, caseResults, mismatches);
        RunCaseLauncherSettingsHooksVariant(tmpRoot, "launcher-settings-hooks-array", "{\"hooks\":[]}", false, caseResults, mismatches);
        RunCaseLauncherSettingsHooksVariant(tmpRoot, "launcher-settings-hooks-string", "{\"hooks\":\"x\"}", false, caseResults, mismatches);
        RunCaseLauncherSettingsHooksVariant(tmpRoot, "launcher-settings-hooks-null", "{\"hooks\":null}", false, caseResults, mismatches);
        RunCaseLauncherSettingsHooksVariant(tmpRoot, "launcher-settings-no-hooks", "{}", true, caseResults, mismatches);
        NonInteractiveOverride = false; TestPrecon = null;
        if (mismatches.Count == 0)
        {
            Console.WriteLine(new JsonObject { ["selfTest"] = "trust-origin", ["verdict"] = "PASS",
                ["casesRun"] = caseResults.Count, ["cases"] = caseResults,
                ["selfFalsificationNote"] = "기대값을 틀리게 적으면 mismatches > 0 → exit 1. 판정선 4: 기대 epoch를 2로 바꿔 FAIL 실증.",
            }.ToJsonString(WriteOptions));
            return 0;
        }
        Console.Error.WriteLine(new JsonObject { ["selfTest"] = "trust-origin", ["verdict"] = "FAIL",
            ["mismatchCount"] = mismatches.Count, ["mismatches"] = mismatches, ["cases"] = caseResults,
        }.ToJsonString(WriteOptions));
        return 1;
    }

    // case: declare-ok — failure가 knownExceptions에 명시돼 있으면 record 생성, trustEpoch=1.
    private static void RunCaseDeclareOk(
        string tmpRoot, string wsPath, string logPath, string recordPath,
        string wsDirty, PreconOverride precon, JsonArray caseResults, JsonArray mismatches)
    {
        ResetFixture(wsPath, wsDirty, logPath, recordPath);
        NonInteractiveOverride = true; TestPrecon = precon;
        var exceptions = new List<KnownException>
        {
            new("state-transition-not-logged", "LISTED-TRANSITION",
                "자기 시험용 fixture — state에 있지만 log에 없는 전이", "self-test fixture", "self-test only"),
        };
        var exit = RunDeclareCore(tmpRoot, exceptions);
        var recExists = File.Exists(recordPath);
        var epoch = 0; string? commit = null; var hasDC = false; string? verdict = null;
        if (recExists)
        {
            var rec = JsonNode.Parse(File.ReadAllText(recordPath))?.AsObject();
            epoch = rec?["trustEpoch"]?.GetValue<int>() ?? 0;
            commit = rec?["baselineCommit"]?.GetValue<string>();
            hasDC = rec?.ContainsKey("declarationCommit") ?? false;
            verdict = rec?["reconciliationVerdict"]?.GetValue<string>();
        }
        var ok = exit == 0 && recExists && epoch == 1 && commit == precon.CommitHash
            && !hasDC && verdict == "VERIFIED_PASS_WITH_KNOWN_EXCEPTIONS";
        caseResults.Add(new JsonObject { ["case"] = "declare-ok", ["exit"] = exit, ["recordExists"] = recExists,
            ["trustEpoch"] = epoch, ["commitMatch"] = (commit == precon.CommitHash),
            ["noDeclarationCommit"] = !hasDC, ["reconciliationVerdict"] = verdict, ["pass"] = ok });
        if (!ok) mismatches.Add(new JsonObject { ["case"] = "declare-ok",
            ["expected"] = "exit=0, record, epoch=1, commitMatch, no-DC, verdict=VERIFIED_PASS_WITH_KNOWN_EXCEPTIONS",
            ["actual"] = $"exit={exit},exists={recExists},epoch={epoch},commit={commit},hasDC={hasDC},verdict={verdict}" });
    }

    // case: no-self-reference — declare-ok가 생성한 record에 declarationCommit 필드가 없다.
    private static void RunCaseNoSelfReference(string recordPath, JsonArray caseResults, JsonArray mismatches)
    {
        var recExists = File.Exists(recordPath);
        var hasDC = false;
        if (recExists)
        {
            try { var rec = JsonNode.Parse(File.ReadAllText(recordPath))?.AsObject(); hasDC = rec?.ContainsKey("declarationCommit") ?? false; }
            catch { hasDC = true; }
        }
        var ok = recExists && !hasDC;
        caseResults.Add(new JsonObject { ["case"] = "no-self-reference", ["recordExists"] = recExists, ["hasDeclarationCommit"] = hasDC, ["pass"] = ok });
        if (!ok) mismatches.Add(new JsonObject { ["case"] = "no-self-reference",
            ["expected"] = "record exists, declarationCommit 없음", ["actual"] = $"recExists={recExists},hasDC={hasDC}" });
    }

    // case: unlisted-failure — reconciliation failure가 knownExceptions에 없으면 선언 거부.
    private static void RunCaseUnlistedFailure(
        string tmpRoot, string wsPath, string logPath, string recordPath,
        string wsUnlisted, PreconOverride precon, JsonArray caseResults, JsonArray mismatches)
    {
        ResetFixture(wsPath, wsUnlisted, logPath, recordPath);
        NonInteractiveOverride = true; TestPrecon = precon;
        var exit = RunDeclareCore(tmpRoot, []); // knownExceptions 비어 있음 → UNLISTED-TRANSITION 미명시 → 거부
        var recCreated = File.Exists(recordPath);
        var ok = exit == 1 && !recCreated;
        caseResults.Add(new JsonObject { ["case"] = "unlisted-failure", ["exit"] = exit, ["recordCreated"] = recCreated, ["pass"] = ok });
        if (!ok) mismatches.Add(new JsonObject { ["case"] = "unlisted-failure",
            ["expected"] = "exit=1 (선언 거부), record 미생성", ["actual"] = $"exit={exit},recordCreated={recCreated}" });
    }

    // case: redeclare — trustEpoch >= 1 이미 존재하면 거부.
    private static void RunCaseRedeclare(
        string tmpRoot, string wsPath, string logPath, string recordPath,
        string wsClean, PreconOverride precon, JsonArray caseResults, JsonArray mismatches)
    {
        ResetFixture(wsPath, wsClean, logPath, recordPath);
        Directory.CreateDirectory(Path.GetDirectoryName(recordPath)!);
        File.WriteAllText(recordPath, "{\"schemaVersion\":1,\"trustOriginId\":\"TO-2026-001\",\"trustEpoch\":1}", new UTF8Encoding(false));
        NonInteractiveOverride = true; TestPrecon = precon;
        var exit = RunDeclareCore(tmpRoot, []);
        var ok = exit == 1;
        caseResults.Add(new JsonObject { ["case"] = "redeclare", ["exit"] = exit, ["pass"] = ok });
        if (!ok) mismatches.Add(new JsonObject { ["case"] = "redeclare",
            ["expected"] = "exit=1 (재선언 거부)", ["actual"] = $"exit={exit}" });
    }

    // case: record-path-fixed — 선언은 canonical record 경로 외 다른 파일을 쓰지 않는다.
    private static void RunCaseRecordPathFixed(
        string tmpRoot, string wsPath, string logPath, string recordPath,
        string wsClean, PreconOverride precon, JsonArray caseResults, JsonArray mismatches)
    {
        ResetFixture(wsPath, wsClean, logPath, recordPath);
        NonInteractiveOverride = true; TestPrecon = precon;
        var beforeFiles = Directory.GetFiles(tmpRoot, "*", SearchOption.AllDirectories)
            .Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var exit = RunDeclareCore(tmpRoot, []);
        var afterFiles = Directory.GetFiles(tmpRoot, "*", SearchOption.AllDirectories)
            .Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newFiles = afterFiles.Except(beforeFiles).ToList();
        var canonicalPath = Path.GetFullPath(recordPath);
        var onlyCanonical = exit == 0 && newFiles.Count == 1 &&
            string.Equals(newFiles[0], canonicalPath, StringComparison.OrdinalIgnoreCase);
        caseResults.Add(new JsonObject { ["case"] = "record-path-fixed", ["exit"] = exit,
            ["newFileCount"] = newFiles.Count, ["onlyCanonicalPath"] = onlyCanonical, ["pass"] = onlyCanonical });
        if (!onlyCanonical) mismatches.Add(new JsonObject { ["case"] = "record-path-fixed",
            ["expected"] = "exit=0, 신규 파일 1개만 (canonical path)",
            ["actual"] = $"exit={exit},newFiles=[{string.Join(";", newFiles)}]" });
    }

    // case: extra-known-exception — knownExceptions가 실제 failure보다 많으면 선언 거부.
    private static void RunCaseExtraKnownException(
        string tmpRoot, string wsPath, string logPath, string recordPath,
        string wsDirty, PreconOverride precon, JsonArray caseResults, JsonArray mismatches)
    {
        ResetFixture(wsPath, wsDirty, logPath, recordPath);
        NonInteractiveOverride = true; TestPrecon = precon;
        var exceptions = new List<KnownException>
        {
            new("state-transition-not-logged", "LISTED-TRANSITION", "fixture 알려진 오염", "fixture", "fixture"),
            new("extra-never-observed", "FAKE-NEVER-OBSERVED", "실제 failure 없는 가짜 예외 — extra", "fixture", "fixture"),
        };
        var exit = RunDeclareCore(tmpRoot, exceptions);
        var recCreated = File.Exists(recordPath);
        var ok = exit == 1 && !recCreated;
        caseResults.Add(new JsonObject { ["case"] = "extra-known-exception", ["exit"] = exit,
            ["recordCreated"] = recCreated, ["pass"] = ok });
        if (!ok) mismatches.Add(new JsonObject { ["case"] = "extra-known-exception",
            ["expected"] = "exit=1 (extra known exception 거부), record 미생성",
            ["actual"] = $"exit={exit},recordCreated={recCreated}" });
    }

    // case: record-ready-flags-complete — record에 phaseChangeReady:false, replayReady:false 존재.
    private static void RunCaseRecordReadyFlagsComplete(string recordPath, JsonArray caseResults, JsonArray mismatches)
    {
        var recExists = File.Exists(recordPath);
        bool hasPCR = false, hasRR = false, pcrFalse = false, rrFalse = false;
        if (recExists)
        {
            try
            {
                var rec = JsonNode.Parse(File.ReadAllText(recordPath))?.AsObject();
                hasPCR = rec?.ContainsKey("phaseChangeReady") ?? false;
                hasRR = rec?.ContainsKey("replayReady") ?? false;
                pcrFalse = rec?["phaseChangeReady"]?.GetValue<bool>() == false;
                rrFalse = rec?["replayReady"]?.GetValue<bool>() == false;
            }
            catch { }
        }
        var ok = recExists && hasPCR && hasRR && pcrFalse && rrFalse;
        caseResults.Add(new JsonObject { ["case"] = "record-ready-flags-complete",
            ["recordExists"] = recExists, ["hasPhaseChangeReady"] = hasPCR, ["hasReplayReady"] = hasRR,
            ["phaseChangeFalse"] = pcrFalse, ["replayFalse"] = rrFalse, ["pass"] = ok });
        if (!ok) mismatches.Add(new JsonObject { ["case"] = "record-ready-flags-complete",
            ["expected"] = "record에 phaseChangeReady:false, replayReady:false 존재",
            ["actual"] = $"exists={recExists},hasPCR={hasPCR},hasRR={hasRR},pcrFalse={pcrFalse},rrFalse={rrFalse}" });
    }

    // case: build-verdict-not-forged — BuildVerified=false이면 exit 1, record 미생성.
    private static void RunCaseBuildVerdictNotForged(
        string tmpRoot, string wsPath, string logPath, string recordPath,
        string wsClean, PreconOverride precon, JsonArray caseResults, JsonArray mismatches)
    {
        var badPrecon = precon with { BuildVerified = false };
        ResetFixture(wsPath, wsClean, logPath, recordPath);
        NonInteractiveOverride = true; TestPrecon = badPrecon;
        var exit = RunDeclareCore(tmpRoot, []);
        var recCreated = File.Exists(recordPath);
        var ok = exit == 1 && !recCreated;
        caseResults.Add(new JsonObject { ["case"] = "build-verdict-not-forged", ["exit"] = exit,
            ["recordCreated"] = recCreated, ["pass"] = ok,
            ["note"] = "BuildVerified=false → 선언 거부 → VERIFIED_PASS record 생성 불가" });
        if (!ok) mismatches.Add(new JsonObject { ["case"] = "build-verdict-not-forged",
            ["expected"] = "exit=1, record 미생성 (build 미확인 경로는 VERIFIED_PASS 기록 불가)",
            ["actual"] = $"exit={exit},recordCreated={recCreated}" });
    }

    // case: production-preconditions-not-default-true — TestPrecon=null 시 production path가 기본 true로 통과하지 못함.
    private static void RunCaseProductionPreconNotDefaultTrue(
        string tmpRoot, string wsPath, string logPath, string recordPath,
        string wsClean, JsonArray caseResults, JsonArray mismatches)
    {
        ResetFixture(wsPath, wsClean, logPath, recordPath);
        NonInteractiveOverride = true; TestPrecon = null; // production 경로 강제
        var exit = RunDeclareCore(tmpRoot, []);
        var recCreated = File.Exists(recordPath);
        // tmpRoot에는 dotnet 프로젝트 없음 → CheckBuildProduction 실패 → exit != 0
        var ok = exit != 0 && !recCreated;
        caseResults.Add(new JsonObject { ["case"] = "production-preconditions-not-default-true",
            ["exit"] = exit, ["recordCreated"] = recCreated, ["pass"] = ok,
            ["note"] = "TestPrecon=null: production path에서 build check 실행 — tmpRoot에 dotnet 프로젝트 없어 실패" });
        if (!ok) mismatches.Add(new JsonObject { ["case"] = "production-preconditions-not-default-true",
            ["expected"] = "exit!=0 (production precondition 실패), record 미생성",
            ["actual"] = $"exit={exit},recordCreated={recCreated}" });
    }

    // case: high-risk-full-envelope-<kind> — full envelope로 고위험 전이를 apply → exit 1, trusted-human-receipt-required reason 일치.
    private static void RunCaseHighRiskFullEnvelope(string kind, JsonArray caseResults, JsonArray mismatches)
    {
        var caseName = $"high-risk-full-envelope-{kind.ToLowerInvariant().Replace('_', '-')}";
        var (exit, stderrText, reasonMatched) = RunHighRiskEnvelopeCheck(kind);
        var ok = exit == 1 && reasonMatched;
        caseResults.Add(new JsonObject { ["case"] = caseName, ["kind"] = kind, ["exit"] = exit,
            ["reasonMatched"] = reasonMatched, ["pass"] = ok,
            ["note"] = "full envelope → exit 1, reason 필드 정확 매칭. StateApplierCli.RunApply가 canonical WORKSTATE를 로드하나 high-risk 분기는 request/candidate 읽기 전에 실행된다." });
        if (!ok) mismatches.Add(new JsonObject { ["case"] = caseName,
            ["expected"] = "exit=1, reasonMatched=true (trusted-human-receipt-required)",
            ["actual"] = $"exit={exit},reasonMatched={reasonMatched}" });
    }

    // case: launcher-settings-malformed — malformed settings.json이 있으면 CheckAutoLauncherOff → false (fail-closed).
    private static void RunCaseLauncherSettingsMalformed(string tmpRoot, JsonArray caseResults, JsonArray mismatches)
    {
        var claudeDir = Path.Combine(tmpRoot, ".claude");
        Directory.CreateDirectory(claudeDir);
        var settingsPath = Path.Combine(claudeDir, "settings.json");
        File.WriteAllText(settingsPath, "malformed-not-json", new System.Text.UTF8Encoding(false));
        try
        {
            var result = CheckAutoLauncherOff(tmpRoot);
            var ok = !result; // malformed → fail-closed → false
            caseResults.Add(new JsonObject { ["case"] = "launcher-settings-malformed",
                ["checkAutoLauncherOffResult"] = result, ["pass"] = ok,
                ["note"] = "malformed settings.json → 확인 불가 → fail-closed(false)" });
            if (!ok) mismatches.Add(new JsonObject { ["case"] = "launcher-settings-malformed",
                ["expected"] = "false (malformed JSON → fail-closed)", ["actual"] = result.ToString() });
        }
        finally
        {
            try { File.Delete(settingsPath); } catch { }
        }
    }

    // case: launcher-settings-hooks-variant — 다양한 hooks 값 형태로 CheckAutoLauncherOff 반환값을 검증한다.
    private static void RunCaseLauncherSettingsHooksVariant(
        string tmpRoot, string caseName, string settingsJson, bool expectedResult,
        JsonArray caseResults, JsonArray mismatches)
    {
        var claudeDir = Path.Combine(tmpRoot, ".claude");
        Directory.CreateDirectory(claudeDir);
        var settingsPath = Path.Combine(claudeDir, "settings.json");
        File.WriteAllText(settingsPath, settingsJson, new System.Text.UTF8Encoding(false));
        try
        {
            var result = CheckAutoLauncherOff(tmpRoot);
            var ok = result == expectedResult;
            caseResults.Add(new JsonObject { ["case"] = caseName, ["settingsJson"] = settingsJson,
                ["checkAutoLauncherOffResult"] = result, ["expectedResult"] = expectedResult, ["pass"] = ok });
            if (!ok) mismatches.Add(new JsonObject { ["case"] = caseName,
                ["expected"] = expectedResult.ToString().ToLowerInvariant(),
                ["actual"] = result.ToString().ToLowerInvariant() });
        }
        finally
        {
            try { File.Delete(settingsPath); } catch { }
        }
    }

    // 자기 시험용 fixture 초기화 — WORKSTATE·log를 고정값으로, record를 삭제.
    private static void ResetFixture(string wsPath, string wsContent, string logPath, string recordPath)
    {
        File.WriteAllText(wsPath, wsContent, new UTF8Encoding(false));
        File.WriteAllText(logPath, "", new UTF8Encoding(false));
        if (File.Exists(recordPath)) File.Delete(recordPath);
    }

    // knownExceptions JSON 배열을 파싱한다.
    private static List<KnownException> ParseKnownExceptions(JsonNode? node)
    {
        var list = new List<KnownException>();
        if (node is not JsonArray arr) return list;
        foreach (var item in arr.OfType<JsonObject>())
        {
            var subject = item["subject"]?.GetValue<string>() ?? "";
            if (string.IsNullOrWhiteSpace(subject)) continue;
            list.Add(new KnownException(
                Code: item["code"]?.GetValue<string>() ?? "", Subject: subject,
                What: item["what"]?.GetValue<string>() ?? "", Why: item["why"]?.GetValue<string>() ?? "",
                WhyNotReplayed: item["whyNotReplayed"]?.GetValue<string>() ?? ""));
        }
        return list;
    }

    // knownExceptions 목록을 JsonArray로 변환한다.
    private static JsonArray BuildExceptionsArray(List<KnownException> exceptions)
    {
        var arr = new JsonArray();
        foreach (var e in exceptions)
            arr.Add(new JsonObject { ["code"] = e.Code, ["subject"] = e.Subject, ["what"] = e.What,
                ["why"] = e.Why, ["whyNotReplayed"] = e.WhyNotReplayed });
        return arr;
    }

    // StateApplierCli·HandoffIntegrityChecker가 git-tracked 상태인지 확인한다.
    private static bool CheckFilesTracked(string root)
    {
        foreach (var rel in new[] { "server/StateApplierCli.cs", "server/Harness/HandoffIntegrityChecker.cs" })
            if (RunGitExitCode(root, $"ls-files --error-unmatch \"{rel}\"") != 0) return false;
        return true;
    }

    // WORKSTATE·applier-log가 working tree에서 직접 수정되지 않았는지 확인한다.
    private static bool CheckNoDirectMod(string root)
    {
        var output = RunGitOutput(root,
            "status --porcelain -- docs/handoff/WORKSTATE.json docs/handoff/WORKSTATE.applier-log.jsonl");
        return string.IsNullOrWhiteSpace(output);
    }

    // git 명령의 exit code를 반환한다.
    private static int RunGitExitCode(string root, string arguments)
    {
        using var p = new Process();
        p.StartInfo = new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = root, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false,
        };
        p.Start(); p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd(); p.WaitForExit();
        return p.ExitCode;
    }

    // git 명령의 stdout을 반환한다.
    private static string RunGitOutput(string root, string arguments)
    {
        using var p = new Process();
        p.StartInfo = new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = root, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false,
        };
        p.Start(); var output = p.StandardOutput.ReadToEnd(); p.WaitForExit();
        return output;
    }

    // --key value 쌍을 파싱한다.
    private static Dictionary<string, string> ParseFlagMap(string[] args, int startIndex)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = startIndex; i < args.Length; i++)
            if (args[i].StartsWith("--", StringComparison.Ordinal) && i + 1 < args.Length)
            { map[args[i][2..]] = args[i + 1]; i++; }
        return map;
    }

    // SHA-256을 소문자 hex로 계산한다.
    private static string ComputeSha256(string content)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    // stderr에 JSON 오류 메시지를 출력한다.
    private static void WriteError(string message)
        => Console.Error.WriteLine($"{{\"error\":\"{message}\"}}");
}
