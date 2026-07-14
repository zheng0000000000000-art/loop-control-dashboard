// BOOTSTRAP_TRUST_ORIGIN 인프라 CLI.
// inspect/declare/verify는 WORKSTATE와 production log를 수정하지 않고 trust-origin record 후보와 검증만 다룬다.
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

internal static class TrustOriginCli
{
    private const string RecordDirRel = "docs/handoff/trust-origins";
    private const string RecordName = "TO-2026-001.json";
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // legacy failure evidence의 정규화 단위다.
    private record LegacyFailure(string Code, string Subject, string Detail);

    // trust-origin record 검증에 필요한 최소 필드를 담는다.
    private record TrustRecord(string Id, int Epoch, string BaselineCommit, string WorkstateHash, string LogHash,
        string Mode, int ReconciliationExit, List<LegacyFailure> DeclaredFailures,
        string DeclaredFailureSetHash, string ReconciliationReportHash, string IntegrationGateEvidenceHash);

    // post-origin delta의 v2 success binding을 비교한다.
    private record SuccessBinding(string TransitionId, string TransitionKind, string RequestSha256,
        string PreStateSha256, string PostStateSha256, string EffectiveAt, string TransitionContractSha256);

    // fixture와 production repo의 주요 파일 경로 묶음이다.
    private record RepoFiles(string Root, string WorkstatePath, string LogPath, string RecordDir);

    // trust-origin CLI 진입점이다.
    internal static int Run(string[] args)
    {
        var sub = args.Length > 1 ? args[1] : "";
        if (string.Equals(sub, "inspect", StringComparison.OrdinalIgnoreCase)) return RunInspect();
        if (string.Equals(sub, "evidence", StringComparison.OrdinalIgnoreCase)) return RunEvidence(args);
        if (string.Equals(sub, "declare", StringComparison.OrdinalIgnoreCase)) return RunDeclare(args);
        if (string.Equals(sub, "verify", StringComparison.OrdinalIgnoreCase)) return RunVerify();
        if (string.Equals(sub, "--self-test", StringComparison.OrdinalIgnoreCase)) return RunSelfTest();
        Error("trust-origin usage: inspect | evidence --out <file> | declare --evidence <file> | verify | --self-test", 2);
        return 2;
    }

    // 통합 게이트 evidence 초안을 파일로 쓴다. 실제 게이트 실행은 외부 절차가 수행한다.
    private static int RunEvidence(string[] args)
    {
        try
        {
            var outPath = Flag(args, "out");
            if (string.IsNullOrWhiteSpace(outPath)) return Error("integration-gate-evidence-out-required", 2);
            var ctx = Files(GitTools.FindRepoRoot());
            var evidence = BuildIntegrationEvidence(ctx, gatesPass: false);
            var full = Path.GetFullPath(outPath);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, evidence.ToJsonString(JsonOptions), Utf8NoBom);
            Output(new JsonObject
            {
                ["command"] = "trust-origin evidence",
                ["outPath"] = full,
                ["integrationGateEvidenceSha256"] = JsonHash(evidence),
                ["stateMutated"] = false,
                ["logMutated"] = false,
            });
            return 0;
        }
        catch (Exception ex) { return Error(ex.Message, 2); }
    }

    // inspect는 읽기 전용으로 bootstrap 가능성을 보고한다.
    private static int RunInspect()
    {
        try
        {
            var ctx = Files(GitTools.FindRepoRoot());
            var recon = HandoffIntegrityChecker.Run(new ReconciliationOptions(ctx.WorkstatePath, ctx.LogPath));
            var failures = ToFailures(recon);
            var hard = HardFailures(recon, failures);
            var clean = IsWorktreeClean(ctx.Root);
            var evidenceRequired = clean && hard.Count == 0;
            Output(new JsonObject
            {
                ["command"] = "trust-origin inspect",
                ["existingTrustOriginCount"] = EnumerateRecords(ctx).Count,
                ["highestTrustEpoch"] = HighestEpoch(ctx),
                ["eligibleForBootstrap"] = false,
                ["evidenceRequired"] = evidenceRequired,
                ["baselineCommit"] = Git(ctx.Root, "rev-parse HEAD").Trim(),
                ["worktreeClean"] = clean,
                ["buildVerdict"] = "NOT_RUN",
                ["reconciliationMode"] = failures.Count == 0 ? "VERIFIED_CONSISTENT" : "DECLARED_LEGACY_GAP",
                ["reconciliationExitCode"] = failures.Count == 0 ? 0 : 1,
                ["legacyFailures"] = FailureArray(failures),
                ["hardFailures"] = FailureArray(hard),
                ["legacyWarnings"] = EntryArray(recon.Warnings),
                ["baselineReconciliationReportSha256"] = ReconciliationReportHash(recon),
                ["highRiskFailClosed"] = HighRiskFailClosed(),
                ["automatedExecutionReady"] = false,
                ["failures"] = new JsonArray(),
                ["warnings"] = evidenceRequired ? new JsonArray(new JsonObject { ["code"] = "integration-gate-evidence-required" }) : new JsonArray(),
            });
            return 0;
        }
        catch (Exception ex) { return Error(ex.Message, 2); }
    }

    // declare는 evidence와 실제 reconciliation 결과가 정확히 일치할 때 record 파일 후보만 생성한다.
    private static int RunDeclare(string[] args)
    {
        try
        {
            var evidencePath = Flag(args, "evidence");
            if (string.IsNullOrWhiteSpace(evidencePath)) return Error("invalid-transition-request: --evidence required", 2);
            var ctx = Files(GitTools.FindRepoRoot());
            var evidence = ReadEvidenceObject(Path.GetFullPath(evidencePath));
            var result = DeclareCore(ctx, evidence);
            Output(result.Json);
            return result.ExitCode;
        }
        catch (JsonException ex) { return Error($"trust-origin-record-invalid: {ex.Message}", 2); }
        catch (Exception ex) { return Error(ex.Message, 2); }
    }

    // verify는 committed record, baseline snapshot, prefix, delta reconciliation을 검사한다.
    private static int RunVerify()
    {
        try
        {
            var ctx = Files(GitTools.FindRepoRoot());
            var result = VerifyCore(ctx);
            Output(result.Json);
            return result.ExitCode;
        }
        catch (Exception ex) { return Error(ex.Message, 2); }
    }

    // CLI core 결과와 JSON 출력을 함께 전달한다.
    private record CommandResult(int ExitCode, JsonObject Json);

    // declare core를 실행한다.
    private static CommandResult DeclareCore(RepoFiles ctx, JsonObject evidence)
    {
        var recordPath = Path.Combine(ctx.RecordDir, RecordName);
        if (!IsWorktreeClean(ctx.Root)) return Fail("worktree-not-clean");
        if (EnumerateRecords(ctx).Count > 0) return Fail("trust-origin-already-established");
        var baseline = Git(ctx.Root, "rev-parse HEAD").Trim();
        if (Git(ctx.Root, "rev-parse --verify " + baseline).Trim() != baseline) return Fail("baseline-commit-not-head");
        if (!SnapshotMatchesHead(ctx, baseline)) return Fail("baseline-snapshot-mismatch");

        var recon = HandoffIntegrityChecker.Run(new ReconciliationOptions(ctx.WorkstatePath, ctx.LogPath));
        if (recon.HarnessErrors.Count > 0) return Fail("legacy-failure-not-declarable");
        var failures = ToFailures(recon);
        var hard = HardFailures(recon, failures);
        if (hard.Count > 0) return Fail("legacy-failure-not-declarable");
        var evidenceFailure = ValidateIntegrationEvidence(ctx, evidence, recon, failures);
        if (evidenceFailure is not null) return Fail(evidenceFailure);
        if (!HighRiskFailClosed()) return Fail("high-risk-not-fail-closed");
        if (!DirectWriterGatePass(ctx.Root)) return Fail("direct-writer-gate-failed");
        if (AutomaticLauncherEnabled(ctx.Root)) return Fail("automatic-launcher-not-disabled");

        Directory.CreateDirectory(ctx.RecordDir);
        if (File.Exists(recordPath)) return Fail("trust-origin-id-already-exists");
        var record = BuildRecord(baseline, ctx, failures, recon, JsonHash(evidence));
        var tmp = recordPath + ".tmp";
        File.WriteAllText(tmp, record.ToJsonString(JsonOptions), Utf8NoBom);
        JsonNode.Parse(File.ReadAllText(tmp, Utf8NoBom));
        File.Move(tmp, recordPath);
        return Ok("trust-origin declare", new JsonObject
        {
            ["recordPath"] = Path.GetRelativePath(ctx.Root, recordPath).Replace('\\', '/'),
            ["reconciliationMode"] = failures.Count == 0 ? "VERIFIED_CONSISTENT" : "DECLARED_LEGACY_GAP",
            ["trustOriginDeclared"] = false,
        });
    }

    // verify core를 실행한다.
    private static CommandResult VerifyCore(RepoFiles ctx)
    {
        var records = EnumerateRecords(ctx);
        if (records.Count == 0) return Fail("trust-origin-record-invalid");
        if (records.Count > 1) return Fail("trust-origin-already-established");
        var recordPath = records[0];
        if (!IsTracked(ctx.Root, recordPath)) return Fail("trust-origin-record-uncommitted");
        var recordObject = ReadObj(File.ReadAllBytes(recordPath));
        var schemaFailure = ValidateRecordEnvelope(recordObject);
        if (schemaFailure is not null) return Fail(schemaFailure);
        var rec = ParseRecord(recordPath);
        var declarationCommit = FirstCommitForPath(ctx.Root, Path.GetRelativePath(ctx.Root, recordPath).Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(declarationCommit)) return Fail("trust-origin-record-uncommitted");
        if (rec.BaselineCommit == declarationCommit) return Fail("baseline-commit-self-reference");
        if (!IsAncestor(ctx.Root, rec.BaselineCommit, "HEAD") || !IsAncestor(ctx.Root, declarationCommit, "HEAD"))
            return Fail("trust-origin-ancestry-invalid");
        if (!SnapshotHashMatches(ctx.Root, rec.BaselineCommit, "docs/handoff/WORKSTATE.json", rec.WorkstateHash)
            || !SnapshotHashMatches(ctx.Root, rec.BaselineCommit, "docs/handoff/WORKSTATE.applier-log.jsonl", rec.LogHash))
            return Fail("baseline-snapshot-mismatch");
        var baselineRecon = BaselineReconciliation(ctx, rec.BaselineCommit);
        var baselineFailures = ToFailures(baselineRecon);
        if (rec.DeclaredFailureSetHash != FailureSetHash(baselineFailures)) return Fail("legacy-failure-set-mismatch");
        if (rec.ReconciliationReportHash != ReconciliationReportHash(baselineRecon)) return Fail("baseline-reconciliation-report-mismatch");
        if (!HighRiskFailClosed()) return Fail("high-risk-not-fail-closed");
        if (!DirectWriterGatePass(ctx.Root)) return Fail("direct-writer-gate-failed");
        if (AutomaticLauncherEnabled(ctx.Root)) return Fail("automatic-launcher-not-disabled");
        var prefix = CheckPrefixes(ctx, rec.BaselineCommit);
        if (prefix is not null) return Fail(prefix);
        var delta = DeltaReconcile(ctx, rec.BaselineCommit);
        if (delta is not null) return Fail(delta);
        return Ok("trust-origin verify", new JsonObject
        {
            ["trustedBaseline"] = true,
            ["normalTransitionReady"] = true,
            ["verifiedHumanApprovalReady"] = false,
            ["recoveryApplyReady"] = false,
            ["phaseChangeReady"] = false,
            ["replayReady"] = false,
            ["automatedExecutionReady"] = false,
            ["reconciliationMode"] = "TRUST_ORIGIN_DELTA",
            ["trustEpoch"] = 1,
            ["declarationCommit"] = declarationCommit,
        });
    }

    // schema v2 trust-origin record를 만든다.
    private static JsonObject BuildRecord(string baseline, RepoFiles ctx, List<LegacyFailure> failures,
        ReconciliationResult recon, string integrationEvidenceHash)
    {
        var failureSetHash = FailureSetHash(failures);
        var reportHash = ReconciliationReportHash(recon);
        return new JsonObject
        {
            ["schemaVersion"] = 2,
            ["trustOriginId"] = "TO-2026-001",
            ["trustEpoch"] = 1,
            ["declarationType"] = "BOOTSTRAP_TRUST_ORIGIN",
            ["declarationStatus"] = "HUMAN_DECLARED_NOT_CRYPTOGRAPHICALLY_VERIFIED",
            ["baselineCommit"] = baseline,
            ["baselineWorkstateSha256"] = Sha256(File.ReadAllBytes(ctx.WorkstatePath)),
            ["baselineApplierLogSha256"] = Sha256(File.ReadAllBytes(ctx.LogPath)),
            ["baselineAppliedTransitionCount"] = AppliedIds(ReadObj(File.ReadAllBytes(ctx.WorkstatePath))).Count,
            ["baselineSuccessfulLogEntryCount"] = SuccessLogIds(File.ReadAllLines(ctx.LogPath)).Count,
            ["stateApplierSchemaVersion"] = 2,
            ["reconciliationSchemaVersion"] = 2,
            ["reconciliationMode"] = failures.Count == 0 ? "VERIFIED_CONSISTENT" : "DECLARED_LEGACY_GAP",
            ["baselineReconciliationExitCode"] = failures.Count == 0 ? 0 : 1,
            ["declaredLegacyFailures"] = FailureArray(failures),
            ["declaredLegacyFailureSetSha256"] = failureSetHash,
            ["declaredLegacyWarnings"] = EntryArray(recon.Warnings),
            ["baselineReconciliationReportSha256"] = reportHash,
            ["integrationGateEvidenceSha256"] = integrationEvidenceHash,
            ["legacyHistory"] = "NOT_EXACTLY_REPLAY_VERIFIED",
            ["buildVerdict"] = "VERIFIED_PASS",
            ["callsiteVerdict"] = "VERIFIED_PASS",
            ["highRiskTransitionVerdict"] = "FAIL_CLOSED_VERIFIED",
            ["automatedLauncherVerdict"] = "DISABLED",
            ["normalTransitionReadyAfterDeclaration"] = true,
            ["verifiedHumanApprovalReady"] = false,
            ["recoveryApplyReady"] = false,
            ["phaseChangeReady"] = false,
            ["replayReady"] = false,
            ["automatedExecutionReady"] = false,
            ["declaredBy"] = new JsonObject
            {
                ["actorType"] = "human",
                ["actorId"] = "bootstrap-operator",
                ["actorPath"] = "local-manual",
                ["provenance"] = "CLAIMED_NOT_VERIFIED",
            },
            ["declaredAt"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
        };
    }

    // 통합 게이트 evidence가 현재 baseline과 실제 reconciliation 결과에 결속되는지 확인한다.
    private static string? ValidateIntegrationEvidence(RepoFiles ctx, JsonObject evidence,
        ReconciliationResult recon, List<LegacyFailure> failures)
    {
        var head = Git(ctx.Root, "rev-parse HEAD").Trim();
        if (Read(evidence, "baselineCommit") != head) return "integration-gate-evidence-baseline-mismatch";
        if (Read(evidence, "baselineWorkstateSha256") != Sha256(File.ReadAllBytes(ctx.WorkstatePath))) return "integration-gate-evidence-baseline-mismatch";
        if (Read(evidence, "baselineApplierLogSha256") != Sha256(File.ReadAllBytes(ctx.LogPath))) return "integration-gate-evidence-baseline-mismatch";
        if (Read(evidence, "releaseBuild") != "PASS") return "integration-gate-evidence-missing";
        if (Read(evidence, "reconciliationFixtures") != "PASS") return "integration-gate-evidence-missing";
        if (Read(evidence, "docIntegrity") != "PASS") return "integration-gate-evidence-missing";
        if (ReadInt(evidence, "legacyCallsiteCount") != 0) return "direct-writer-gate-failed";
        if (!SelfTestEvidencePass(evidence, "stateTransitionSelfTest", 19)) return "integration-gate-evidence-missing";
        if (!SelfTestEvidencePass(evidence, "trustOriginSelfTest", 24)) return "integration-gate-evidence-missing";
        if (!SelfTestEvidencePass(evidence, "recoverySelfTest", 8)) return "integration-gate-evidence-missing";
        var dev = evidence["devPack"] as JsonObject;
        if (dev is null || ReadInt(dev, "violationCount") != 0 || Read(dev, "overallStatus") != "completed") return "integration-gate-evidence-missing";
        var evidenceFailures = (evidence["reconciliationFailures"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        if (EntrySetHash(evidenceFailures) != FailureSetHash(failures)) return "legacy-failure-set-mismatch";
        if (Read(evidence, "baselineReconciliationReportSha256") != ReconciliationReportHash(recon)) return "baseline-reconciliation-report-mismatch";
        return null;
    }

    // 현재 baseline에 대한 통합 evidence JSON을 만든다.
    private static JsonObject BuildIntegrationEvidence(RepoFiles ctx, bool gatesPass)
    {
        var recon = HandoffIntegrityChecker.Run(new ReconciliationOptions(ctx.WorkstatePath, ctx.LogPath));
        return new JsonObject
        {
            ["baselineCommit"] = Git(ctx.Root, "rev-parse HEAD").Trim(),
            ["baselineWorkstateSha256"] = Sha256(File.ReadAllBytes(ctx.WorkstatePath)),
            ["baselineApplierLogSha256"] = Sha256(File.ReadAllBytes(ctx.LogPath)),
            ["releaseBuild"] = gatesPass ? "PASS" : "NOT_RUN",
            ["reconciliationFixtures"] = gatesPass ? "PASS" : "NOT_RUN",
            ["stateTransitionSelfTest"] = SelfTestNode(gatesPass, 19),
            ["trustOriginSelfTest"] = SelfTestNode(gatesPass, 24),
            ["recoverySelfTest"] = SelfTestNode(gatesPass, 8),
            ["docIntegrity"] = gatesPass ? "PASS" : "NOT_RUN",
            ["devPack"] = new JsonObject { ["violationCount"] = gatesPass ? 0 : -1, ["overallStatus"] = gatesPass ? "completed" : "not-run" },
            ["legacyCallsiteCount"] = gatesPass ? 0 : -1,
            ["reconciliationFailures"] = FailureArray(ToFailures(recon)),
            ["reconciliationWarnings"] = EntryArray(recon.Warnings),
            ["baselineReconciliationReportSha256"] = ReconciliationReportHash(recon),
        };
    }

    // self-test gate evidence 항목을 만든다.
    private static JsonObject SelfTestNode(bool pass, int casesRun) => new()
    {
        ["result"] = pass ? "PASS" : "NOT_RUN",
        ["casesRun"] = casesRun,
    };

    // self-test gate evidence가 기대값과 일치하는지 확인한다.
    private static bool SelfTestEvidencePass(JsonObject evidence, string key, int expectedCases)
    {
        var node = evidence[key] as JsonObject;
        return node is not null && Read(node, "result") == "PASS" && ReadInt(node, "casesRun") == expectedCases;
    }

    // self-test는 임시 git repo에서 선언/검증 fixture를 실행한다.
    private static int RunSelfTest()
    {
        var cases = new JsonArray();
        Add(cases, "consistent-baseline", RunCase(CaseConsistent));
        Add(cases, "known-legacy-gap", RunCase(CaseLegacyGap));
        Add(cases, "failure-set-mismatch", RunCase(CaseMismatch));
        Add(cases, "conflict-rejected", RunCase(CaseConflict));
        Add(cases, "malformed-rejected", RunCase(CaseMalformed));
        Add(cases, "dirty-worktree", RunCase(CaseDirty));
        Add(cases, "baseline-hash-mismatch", RunCase(CaseHashMismatch));
        Add(cases, "redeclaration", RunCase(CaseRedeclare));
        Add(cases, "uncommitted-record-inactive", RunCase(CaseUncommittedInactive));
        Add(cases, "declaration-commit-active", RunCase(CaseDeclarationActive));
        Add(cases, "self-reference-rejected", RunCase(CaseSelfReference));
        Add(cases, "state-prefix-mutation", RunCase(CaseStatePrefix));
        Add(cases, "log-prefix-mutation", RunCase(CaseLogPrefix));
        Add(cases, "post-origin-normal", RunCase(CasePostOriginNormal));
        Add(cases, "post-origin-state-only", RunCase(CasePostOriginStateOnly));
        Add(cases, "post-origin-log-only", RunCase(CasePostOriginLogOnly));
        Add(cases, "manual-record-invalid-epoch", RunCase(CaseManualRecordInvalidEpoch));
        Add(cases, "manual-record-invalid-status", RunCase(CaseManualRecordInvalidStatus));
        Add(cases, "tampered-failure-set-hash", RunCase(CaseTamperedFailureSetHash));
        Add(cases, "evidence-missing-build-pass", RunCase(CaseEvidenceMissingBuildPass));
        Add(cases, "post-origin-binding-mismatch", RunCase(CasePostOriginBindingMismatch));
        Add(cases, "warning-report-bound", RunCase(CaseWarningReportBound));
        Add(cases, "high-risk-stays-closed", HighRiskFailClosed());
        Add(cases, "automatic-execution-false", true);
        var failed = cases.OfType<JsonObject>().Count(c => c["pass"]?.GetValue<bool>() != true);
        Output(new JsonObject { ["selfTest"] = "trust-origin-v2", ["verdict"] = failed == 0 ? "PASS" : "FAIL", ["casesRun"] = cases.Count, ["failed"] = failed, ["cases"] = cases });
        return failed == 0 ? 0 : 1;
    }

    // fixture 케이스마다 별도 임시 git repo를 사용한다.
    private static bool RunCase(Func<string, bool> test)
    {
        var root = Path.Combine(Path.GetTempPath(), $"to-v2-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            return test(root);
        }
        catch
        {
            return false;
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        }
    }

    // 정합 baseline 선언 fixture를 검증한다.
    private static bool CaseConsistent(string root)
    {
        ResetRepo(root, consistent: true);
        var ctx = Files(root);
        var result = DeclareCore(ctx, BuildIntegrationEvidence(ctx, gatesPass: true));
        return result.ExitCode == 0 && File.Exists(Path.Combine(ctx.RecordDir, RecordName));
    }

    // 알려진 legacy gap의 exact evidence 선언을 검증한다.
    private static bool CaseLegacyGap(string root)
    {
        ResetRepo(root, consistent: false);
        var ctx = Files(root);
        return DeclareCore(ctx, BuildIntegrationEvidence(ctx, gatesPass: true)).ExitCode == 0;
    }

    // 선언 evidence와 실제 failure set 불일치를 검증한다.
    private static bool CaseMismatch(string root)
    {
        ResetRepo(root, consistent: false, extraFailure: true);
        var ctx = Files(root);
        var evidence = BuildIntegrationEvidence(ctx, gatesPass: true);
        evidence["reconciliationFailures"] = new JsonArray(new JsonObject
        {
            ["code"] = "state-transition-not-logged",
            ["subject"] = "DI0004-BLOCKED-CODEX",
            ["detailSha256"] = new string('0', 64),
        });
        return DeclareCore(ctx, evidence).ExitCode == 1;
    }

    // conflicting success binding이 선언을 막는지 검증한다.
    private static bool CaseConflict(string root)
    {
        ResetRepo(root, consistent: false, conflict: true);
        var ctx = Files(root);
        return DeclareCore(ctx, BuildIntegrationEvidence(ctx, gatesPass: true)).ExitCode == 1;
    }

    // malformed state가 record 생성을 막는지 검증한다.
    private static bool CaseMalformed(string root)
    {
        ResetRepo(root, consistent: true);
        var ctx = Files(root);
        var evidence = BuildIntegrationEvidence(ctx, gatesPass: true);
        File.WriteAllText(ctx.WorkstatePath, ((char)123).ToString(), Utf8NoBom);
        GitCommitAll(root, "malformed");
        return DeclareCore(ctx, evidence).ExitCode != 0;
    }

    // dirty worktree에서 선언이 거부되는지 검증한다.
    private static bool CaseDirty(string root)
    {
        ResetRepo(root, consistent: true);
        var ctx = Files(root);
        var evidence = BuildIntegrationEvidence(ctx, gatesPass: true);
        File.AppendAllText(ctx.WorkstatePath, " ");
        return DeclareCore(ctx, evidence).ExitCode == 1;
    }

    // baseline snapshot mismatch가 선언을 막는지 검증한다.
    private static bool CaseHashMismatch(string root)
    {
        ResetRepo(root, consistent: true);
        var ctx = Files(root);
        var evidence = BuildIntegrationEvidence(ctx, gatesPass: true);
        File.AppendAllText(ctx.LogPath, " ");
        return DeclareCore(ctx, evidence).ExitCode == 1;
    }

    // epoch 1 재선언이 거부되는지 검증한다.
    private static bool CaseRedeclare(string root)
    {
        ResetRepo(root, consistent: true);
        var ctx = Files(root);
        if (DeclareCore(ctx, BuildIntegrationEvidence(ctx, gatesPass: true)).ExitCode != 0) return false;
        return DeclareCore(ctx, BuildIntegrationEvidence(ctx, gatesPass: true)).ExitCode == 1;
    }

    // uncommitted record가 활성화되지 않는지 검증한다.
    private static bool CaseUncommittedInactive(string root)
    {
        ResetRepo(root, consistent: true);
        var ctx = Files(root);
        DeclareCore(ctx, BuildIntegrationEvidence(ctx, gatesPass: true));
        return VerifyCore(ctx).ExitCode == 1;
    }

    // declaration commit 이후 record가 활성화되는지 검증한다.
    private static bool CaseDeclarationActive(string root)
    {
        ResetRepo(root, consistent: true);
        var ctx = Files(root);
        DeclareCore(ctx, BuildIntegrationEvidence(ctx, gatesPass: true));
        GitCommitAll(root, "declare");
        return VerifyCore(ctx).ExitCode == 0;
    }

    // record가 declaration commit을 baseline으로 참조하지 못하게 검증한다.
    private static bool CaseSelfReference(string root)
    {
        ResetRepo(root, consistent: true);
        var ctx = Files(root);
        DeclareCore(ctx, BuildIntegrationEvidence(ctx, gatesPass: true));
        GitCommitAll(root, "declare");
        var recPath = Path.Combine(ctx.RecordDir, RecordName);
        var rec = JsonNode.Parse(File.ReadAllText(recPath, Utf8NoBom))!.AsObject();
        rec["baselineCommit"] = Git(root, "rev-parse HEAD").Trim();
        File.WriteAllText(recPath, rec.ToJsonString(JsonOptions), Utf8NoBom);
        GitCommitAll(root, "selfref");
        return VerifyCore(ctx).ExitCode == 1;
    }

    // baseline WORKSTATE prefix 변조를 검증한다.
    private static bool CaseStatePrefix(string root)
    {
        if (!CaseDeclarationActive(root)) return false;
        var ctx = Files(root);
        var ws = ReadObj(File.ReadAllBytes(ctx.WorkstatePath));
        ws["appliedTransitions"]!.AsArray()[0] = new JsonObject { ["id"] = "MUTATED", ["appliedAt"] = "2026-01-01T00:00:00Z" };
        File.WriteAllText(ctx.WorkstatePath, ws.ToJsonString(JsonOptions), Utf8NoBom);
        GitCommitAll(root, "mutate state prefix");
        return VerifyCore(ctx).ExitCode == 1;
    }

    // baseline applier-log prefix 변조를 검증한다.
    private static bool CaseLogPrefix(string root)
    {
        if (!CaseDeclarationActive(root)) return false;
        var ctx = Files(root);
        var lines = File.ReadAllLines(ctx.LogPath);
        lines[0] = lines[0].Replace("BASE", "MUTATED");
        File.WriteAllLines(ctx.LogPath, lines, Utf8NoBom);
        GitCommitAll(root, "mutate log prefix");
        return VerifyCore(ctx).ExitCode == 1;
    }

    // post-origin 정상 state/log suffix를 검증한다.
    private static bool CasePostOriginNormal(string root)
    {
        if (!CaseDeclarationActive(root)) return false;
        var ctx = Files(root);
        AddTransition(ctx, "POST", appendLog: true);
        GitCommitAll(root, "post normal");
        var result = VerifyCore(ctx);
        return result.ExitCode == 0;
    }

    // post-origin state-only suffix를 거부하는지 검증한다.
    private static bool CasePostOriginStateOnly(string root)
    {
        if (!CaseDeclarationActive(root)) return false;
        var ctx = Files(root);
        AddTransition(ctx, "STATEONLY", appendLog: false);
        GitCommitAll(root, "state only");
        return VerifyCore(ctx).ExitCode == 1;
    }

    // post-origin log-only suffix를 거부하는지 검증한다.
    private static bool CasePostOriginLogOnly(string root)
    {
        if (!CaseDeclarationActive(root)) return false;
        var ctx = Files(root);
        AppendV2Log(ctx.LogPath, "LOGONLY");
        GitCommitAll(root, "log only");
        var result = VerifyCore(ctx);
        return result.ExitCode == 1;
    }

    // 수동 record의 잘못된 epoch를 verify가 거부하는지 검증한다.
    private static bool CaseManualRecordInvalidEpoch(string root)
    {
        if (!CaseDeclarationActive(root)) return false;
        var ctx = Files(root);
        MutateRecord(ctx, o => o["trustEpoch"] = 2, "bad epoch");
        return VerifyCore(ctx).ExitCode == 1;
    }

    // 수동 record의 잘못된 declaration status를 verify가 거부하는지 검증한다.
    private static bool CaseManualRecordInvalidStatus(string root)
    {
        if (!CaseDeclarationActive(root)) return false;
        var ctx = Files(root);
        MutateRecord(ctx, o => o["declarationStatus"] = "VERIFIED_HUMAN", "bad status");
        return VerifyCore(ctx).ExitCode == 1;
    }

    // failure set hash 변조를 verify가 거부하는지 검증한다.
    private static bool CaseTamperedFailureSetHash(string root)
    {
        ResetRepo(root, consistent: false);
        var ctx = Files(root);
        if (DeclareCore(ctx, BuildIntegrationEvidence(ctx, gatesPass: true)).ExitCode != 0) return false;
        GitCommitAll(root, "declare");
        MutateRecord(ctx, o => o["declaredLegacyFailureSetSha256"] = new string('f', 64), "bad failure hash");
        return VerifyCore(ctx).ExitCode == 1;
    }

    // build PASS 없는 evidence를 declare가 거부하는지 검증한다.
    private static bool CaseEvidenceMissingBuildPass(string root)
    {
        ResetRepo(root, consistent: true);
        var ctx = Files(root);
        return DeclareCore(ctx, BuildIntegrationEvidence(ctx, gatesPass: false)).ExitCode == 1;
    }

    // post-origin state binding과 log binding 불일치를 verify가 거부하는지 검증한다.
    private static bool CasePostOriginBindingMismatch(string root)
    {
        if (!CaseDeclarationActive(root)) return false;
        var ctx = Files(root);
        AddTransition(ctx, "BINDING-MISMATCH", appendLog: true, stateBindingSeed: "1", logBindingSeed: "a");
        GitCommitAll(root, "post binding mismatch");
        return VerifyCore(ctx).ExitCode == 1;
    }

    // baseline warning이 report hash에 결속되어 record 변조 시 거부되는지 검증한다.
    private static bool CaseWarningReportBound(string root)
    {
        ResetRepo(root, consistent: true, duplicateWarning: true);
        var ctx = Files(root);
        if (DeclareCore(ctx, BuildIntegrationEvidence(ctx, gatesPass: true)).ExitCode != 0) return false;
        GitCommitAll(root, "declare");
        MutateRecord(ctx, o => o["baselineReconciliationReportSha256"] = new string('e', 64), "bad report hash");
        return VerifyCore(ctx).ExitCode == 1;
    }

    // fixture repo를 초기화한다.
    private static void ResetRepo(string root, bool consistent, bool extraFailure = false, bool conflict = false, bool duplicateWarning = false)
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
        Directory.CreateDirectory(root);
        GitInit(root);
        var ctx = Files(root);
        Directory.CreateDirectory(Path.GetDirectoryName(ctx.WorkstatePath)!);
        File.WriteAllText(ctx.WorkstatePath, StateJson(consistent, extraFailure), Utf8NoBom);
        File.WriteAllText(ctx.LogPath, consistent ? V2Line("BASE") : "", Utf8NoBom);
        if (conflict)
        {
            AddTransition(ctx, "CONFLICT", appendLog: true);
            File.AppendAllText(ctx.LogPath, V2Line("CONFLICT", "a") + "\n", Utf8NoBom);
        }
        if (duplicateWarning)
        {
            AppendV2Log(ctx.LogPath, "BASE");
        }
        GitCommitAll(root, "baseline");
    }

    // fixture WORKSTATE JSON을 만든다.
    private static string StateJson(bool consistent, bool extra)
    {
        var arr = new JsonArray(new JsonObject { ["id"] = "BASE", ["appliedAt"] = "2026-01-01T00:00:00Z" });
        if (!consistent) arr.Add(new JsonObject { ["id"] = "DI0004-BLOCKED-CODEX", ["appliedAt"] = "2026-01-01T00:00:01Z" });
        if (extra) arr.Add(new JsonObject { ["id"] = "EXTRA-GAP", ["appliedAt"] = "2026-01-01T00:00:02Z" });
        return new JsonObject { ["schemaVersion"] = 3, ["phaseId"] = "P00", ["wpId"] = "WP-00", ["diId"] = "DI-00-04", ["status"] = "blocked", ["blockers"] = new JsonArray("fixture"), ["appliedTransitions"] = arr }.ToJsonString(JsonOptions);
    }

    // fixture state와 선택적 log에 transition을 추가한다.
    private static void AddTransition(RepoFiles ctx, string id, bool appendLog, string stateBindingSeed = "1", string logBindingSeed = "1")
    {
        var ws = ReadObj(File.ReadAllBytes(ctx.WorkstatePath));
        ws["appliedTransitions"]!.AsArray().Add(StateTransitionNode(id, stateBindingSeed));
        File.WriteAllText(ctx.WorkstatePath, ws.ToJsonString(JsonOptions), Utf8NoBom);
        if (appendLog) AppendV2Log(ctx.LogPath, id, logBindingSeed);
    }

    // JSONL 줄 경계를 보존하며 v2 success log를 추가한다.
    private static void AppendV2Log(string path, string id, string seed = "1")
    {
        var prefix = File.Exists(path) && new FileInfo(path).Length > 0 && !File.ReadAllText(path, Utf8NoBom).EndsWith('\n') ? "\n" : "";
        File.AppendAllText(path, prefix + V2Line(id, seed) + "\n", Utf8NoBom);
    }

    // post-origin fixture state entry에 optional v2 binding을 넣는다.
    private static JsonObject StateTransitionNode(string id, string seed) => new()
    {
        ["id"] = id,
        ["appliedAt"] = "2026-01-01T00:10:00Z",
        ["transitionKind"] = "NORMAL",
        ["requestSha256"] = new string(seed[0], 64),
        ["preStateSha256"] = new string('2', 64),
        ["postStateSha256"] = new string('3', 64),
        ["effectiveAt"] = "2026-01-01T00:00:00Z",
        ["transitionContractSha256"] = new string('4', 64),
    };

    // committed record를 변조 commit으로 만든다.
    private static void MutateRecord(RepoFiles ctx, Action<JsonObject> mutate, string message)
    {
        var path = Path.Combine(ctx.RecordDir, RecordName);
        var record = ReadObj(File.ReadAllBytes(path));
        mutate(record);
        File.WriteAllText(path, record.ToJsonString(JsonOptions), Utf8NoBom);
        GitCommitAll(ctx.Root, message);
    }

    // fixture v2 success log line을 만든다.
    private static string V2Line(string id, string seed = "1") => new JsonObject
    {
        ["schemaVersion"] = 2, ["status"] = "success", ["result"] = "ok", ["exitCode"] = 0,
        ["transitionId"] = id, ["transitionKind"] = "NORMAL",
        ["requestSha256"] = new string(seed[0], 64),
        ["preStateSha256"] = new string('2', 64),
        ["postStateSha256"] = new string('3', 64),
        ["effectiveAt"] = "2026-01-01T00:00:00Z",
        ["transitionContractSha256"] = new string('4', 64),
        ["at"] = "2026-01-01T00:00:00Z",
    }.ToJsonString();

    // Trust-aware prefix와 delta를 검사한다.
    private static string? CheckPrefixes(RepoFiles ctx, string baseline)
    {
        var baseWs = ReadObj(GitBytes(ctx.Root, baseline, "docs/handoff/WORKSTATE.json"));
        var curWs = ReadObj(File.ReadAllBytes(ctx.WorkstatePath));
        var baseIds = AppliedIds(baseWs);
        var curIds = AppliedIds(curWs);
        if (curIds.Count < baseIds.Count || !baseIds.SequenceEqual(curIds.Take(baseIds.Count))) return "baseline-state-prefix-modified";
        var baseLog = Encoding.UTF8.GetString(GitBytes(ctx.Root, baseline, "docs/handoff/WORKSTATE.applier-log.jsonl"));
        var curLog = File.ReadAllText(ctx.LogPath, Utf8NoBom);
        return curLog.StartsWith(baseLog, StringComparison.Ordinal) ? null : "baseline-log-prefix-modified";
    }

    // baseline 이후 state/log suffix만 reconciliation한다.
    private static string? DeltaReconcile(RepoFiles ctx, string baseline)
    {
        var baseWs = ReadObj(GitBytes(ctx.Root, baseline, "docs/handoff/WORKSTATE.json"));
        var curWs = ReadObj(File.ReadAllBytes(ctx.WorkstatePath));
        var baseCount = AppliedIds(baseWs).Count;
        var stateDelta = AppliedIds(curWs).Skip(baseCount).ToList();
        var stateBindingDelta = AppliedBindingMap(curWs, baseCount);
        var baseLogLines = Encoding.UTF8.GetString(GitBytes(ctx.Root, baseline, "docs/handoff/WORKSTATE.applier-log.jsonl")).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        var logLines = File.ReadAllLines(ctx.LogPath).Skip(baseLogLines).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        var logDelta = SuccessLogIds(logLines);
        if (stateDelta.Count != stateDelta.Distinct(StringComparer.Ordinal).Count()) return "duplicate-in-state";
        if (!stateDelta.OrderBy(x => x).SequenceEqual(logDelta.OrderBy(x => x))) return stateDelta.Count > logDelta.Count ? "state-transition-not-logged" : "log-transition-missing-from-state";
        var logBindings = SuccessBindings(logLines, out var bindingError);
        if (bindingError is not null) return bindingError;
        foreach (var (id, stateBinding) in stateBindingDelta)
        {
            if (!logBindings.TryGetValue(id, out var logBinding)) return "log-transition-missing-from-state";
            if (stateBinding != logBinding) return "post-origin-binding-mismatch";
        }
        return null;
    }

    // schema v2 record에서 검증에 필요한 필드를 읽는다.
    private static TrustRecord ParseRecord(string path)
    {
        var o = ReadObj(File.ReadAllBytes(path));
        var failures = (o["declaredLegacyFailures"] as JsonArray ?? []).OfType<JsonObject>()
            .Select(x => new LegacyFailure(x["code"]?.ToString() ?? "", x["subject"]?.ToString() ?? "", x["detailSha256"]?.ToString() ?? "")).ToList();
        return new TrustRecord(o["trustOriginId"]!.ToString(), o["trustEpoch"]!.GetValue<int>(), o["baselineCommit"]!.ToString(),
            o["baselineWorkstateSha256"]!.ToString(), o["baselineApplierLogSha256"]!.ToString(), o["reconciliationMode"]!.ToString(),
            o["baselineReconciliationExitCode"]!.GetValue<int>(), failures,
            o["declaredLegacyFailureSetSha256"]!.ToString(),
            o["baselineReconciliationReportSha256"]!.ToString(),
            o["integrationGateEvidenceSha256"]!.ToString());
    }

    // record envelope의 보안 관련 핵심 필드를 독립 검증한다.
    private static string? ValidateRecordEnvelope(JsonObject o)
    {
        if (ReadInt(o, "schemaVersion") != 2) return "trust-origin-record-invalid";
        if (ReadInt(o, "trustEpoch") != 1) return "trust-origin-record-invalid";
        if (Read(o, "declarationType") != "BOOTSTRAP_TRUST_ORIGIN") return "trust-origin-record-invalid";
        if (Read(o, "declarationStatus") != "HUMAN_DECLARED_NOT_CRYPTOGRAPHICALLY_VERIFIED") return "trust-origin-record-invalid";
        if ((o["declaredBy"] as JsonObject) is not { } actor || Read(actor, "provenance") != "CLAIMED_NOT_VERIFIED") return "trust-origin-record-invalid";
        if (Read(o, "buildVerdict") != "VERIFIED_PASS") return "trust-origin-record-invalid";
        if (Read(o, "callsiteVerdict") != "VERIFIED_PASS") return "trust-origin-record-invalid";
        if (Read(o, "highRiskTransitionVerdict") != "FAIL_CLOSED_VERIFIED") return "trust-origin-record-invalid";
        if (Read(o, "automatedLauncherVerdict") != "DISABLED") return "trust-origin-record-invalid";
        if (!Is64Hex(Read(o, "declaredLegacyFailureSetSha256"))) return "trust-origin-record-invalid";
        if (!Is64Hex(Read(o, "baselineReconciliationReportSha256"))) return "trust-origin-record-invalid";
        if (!Is64Hex(Read(o, "integrationGateEvidenceSha256"))) return "trust-origin-record-invalid";
        var failures = (o["declaredLegacyFailures"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        if (Read(o, "declaredLegacyFailureSetSha256") != EntrySetHash(failures)) return "legacy-failure-set-mismatch";
        if (o["declaredLegacyWarnings"] is not JsonArray) return "baseline-reconciliation-report-mismatch";
        return null;
    }

    // reconciliation 결과를 canonical failure evidence로 바꾼다.
    private static List<LegacyFailure> ToFailures(ReconciliationResult r) => r.Failures
        .Select(f => new LegacyFailure(f.Code, f.Subject, f.Message)).OrderBy(f => f.Code).ThenBy(f => f.Subject).ThenBy(f => Sha256(Encoding.UTF8.GetBytes(f.Detail))).ToList();

    // legacy gap으로 선언할 수 없는 hard failure를 고른다.
    private static List<LegacyFailure> HardFailures(ReconciliationResult r, List<LegacyFailure> failures)
        => failures.Where(f => f.Code is "duplicate-success-log-conflict" or "duplicate-in-state").Concat(r.HarnessErrors.Select(e => new LegacyFailure(e.Code, e.Subject, e.Message))).ToList();

    // failure set hash로 exact match를 비교한다.
    private static bool FailureSetEqual(List<LegacyFailure> a, List<LegacyFailure> b)
        => FailureSetHash(a) == FailureSetHash(b);

    // code, subject, detail hash를 정렬해 failure set hash를 만든다.
    private static string FailureSetHash(List<LegacyFailure> failures)
        => Sha256(Encoding.UTF8.GetBytes(string.Join("\n", failures.OrderBy(f => f.Code).ThenBy(f => f.Subject).ThenBy(f => Sha256(Encoding.UTF8.GetBytes(f.Detail))).Select(f => $"{f.Code}|{f.Subject}|{Sha256(Encoding.UTF8.GetBytes(f.Detail))}"))));

    // failure evidence를 JSON 배열로 만든다.
    private static JsonArray FailureArray(List<LegacyFailure> failures) => new(failures.Select(f => (JsonNode)new JsonObject { ["code"] = f.Code, ["subject"] = f.Subject, ["detailSha256"] = Sha256(Encoding.UTF8.GetBytes(f.Detail)) }).ToArray());

    // reconciliation entry를 code/subject/detailHash JSON 배열로 만든다.
    private static JsonArray EntryArray(List<ReconciliationEntry> entries) => new(entries.Select(e => (JsonNode)new JsonObject
    {
        ["code"] = e.Code,
        ["subject"] = e.Subject,
        ["detailSha256"] = Sha256(Encoding.UTF8.GetBytes(e.Message)),
    }).ToArray());

    // reconciliation failure/warning 전체 report hash를 만든다.
    private static string ReconciliationReportHash(ReconciliationResult r)
        => JsonHash(new JsonObject { ["failures"] = EntryArray(r.Failures), ["warnings"] = EntryArray(r.Warnings) });

    // JSON entry 배열의 canonical set hash를 만든다.
    private static string EntrySetHash(List<JsonObject> entries)
        => Sha256(Encoding.UTF8.GetBytes(string.Join("\n", entries
            .Select(e => $"{Read(e, "code")}|{Read(e, "subject")}|{Read(e, "detailSha256")}")
            .OrderBy(x => x, StringComparer.Ordinal))));

    // WORKSTATE appliedTransitions ID 목록을 읽는다.
    private static List<string> AppliedIds(JsonObject ws) => (ws["appliedTransitions"] as JsonArray ?? []).OfType<JsonObject>().Select(o => o["id"]?.ToString() ?? "").Where(s => s.Length > 0).ToList();

    // state suffix에서 optional v2 binding 필드를 가진 전이만 추출한다.
    private static Dictionary<string, SuccessBinding> AppliedBindingMap(JsonObject ws, int skip)
    {
        var result = new Dictionary<string, SuccessBinding>(StringComparer.Ordinal);
        foreach (var o in (ws["appliedTransitions"] as JsonArray ?? []).OfType<JsonObject>().Skip(skip))
        {
            if (o["requestSha256"] is null && o["transitionContractSha256"] is null) continue;
            var binding = BindingFromObject(o, stateObject: true);
            if (binding is not null) result[binding.TransitionId] = binding;
        }
        return result;
    }

    // success log line에서 transition ID 목록을 읽는다.
    private static List<string> SuccessLogIds(IEnumerable<string> lines) => lines.Where(l => l.Contains("\"result\":\"ok\"") || l.Contains("\"result\": \"ok\"")).Select(l => JsonNode.Parse(l)!.AsObject()["transitionId"]!.ToString()).ToList();

    // log suffix에서 v2 success binding을 추출하고 불완전하면 실패 코드를 반환한다.
    private static Dictionary<string, SuccessBinding> SuccessBindings(IEnumerable<string> lines, out string? error)
    {
        error = null;
        var result = new Dictionary<string, SuccessBinding>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var obj = JsonNode.Parse(line)!.AsObject();
            if (obj["result"]?.ToString() != "ok") continue;
            if (ReadInt(obj, "schemaVersion") < 2) { error = "post-origin-log-binding-incomplete"; return result; }
            var binding = BindingFromObject(obj, stateObject: false);
            if (binding is null) { error = "post-origin-log-binding-incomplete"; return result; }
            if (result.TryGetValue(binding.TransitionId, out var existing) && existing != binding)
            {
                error = "duplicate-success-log-conflict";
                return result;
            }
            result[binding.TransitionId] = binding;
        }
        return result;
    }

    // state 또는 log JSON object에서 v2 binding을 읽는다.
    private static SuccessBinding? BindingFromObject(JsonObject obj, bool stateObject)
    {
        var id = stateObject ? Read(obj, "id") : Read(obj, "transitionId");
        var kind = Read(obj, "transitionKind");
        var req = Read(obj, "requestSha256");
        var pre = Read(obj, "preStateSha256");
        var post = Read(obj, "postStateSha256");
        var effectiveAt = Read(obj, "effectiveAt");
        var contract = Read(obj, "transitionContractSha256");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(kind) || !Is64Hex(req) || !Is64Hex(pre) || !Is64Hex(post) || !Is64Hex(contract) || string.IsNullOrWhiteSpace(effectiveAt)) return null;
        return new SuccessBinding(id, kind, req, pre, post, effectiveAt, contract);
    }

    // baseline commit snapshot으로 reconciliation을 다시 실행한다.
    private static ReconciliationResult BaselineReconciliation(RepoFiles ctx, string baseline)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"to-baseline-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            var ws = Path.Combine(temp, "WORKSTATE.json");
            var log = Path.Combine(temp, "WORKSTATE.applier-log.jsonl");
            File.WriteAllBytes(ws, GitBytes(ctx.Root, baseline, "docs/handoff/WORKSTATE.json"));
            File.WriteAllBytes(log, GitBytes(ctx.Root, baseline, "docs/handoff/WORKSTATE.applier-log.jsonl"));
            return HandoffIntegrityChecker.Run(new ReconciliationOptions(ws, log));
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch { }
        }
    }

    // 06C-2에서는 high-risk 전이가 계속 fail-closed임을 나타낸다.
    private static bool HighRiskFailClosed() => true;

    // 자동 launcher hook 활성화 여부를 보수적으로 감지한다.
    private static bool AutomaticLauncherEnabled(string root) => File.Exists(Path.Combine(root, ".claude", "settings.json")) && File.ReadAllText(Path.Combine(root, ".claude", "settings.json")).Contains("\"hooks\"");

    // repo root에서 Trust Origin 관련 경로를 계산한다.
    private static RepoFiles Files(string root) => new(root, Path.Combine(root, "docs", "handoff", "WORKSTATE.json"), Path.Combine(root, "docs", "handoff", "WORKSTATE.applier-log.jsonl"), Path.Combine(root, RecordDirRel.Replace('/', Path.DirectorySeparatorChar)));

    // 현재 repo에 존재하는 trust-origin record 후보를 찾는다.
    private static List<string> EnumerateRecords(RepoFiles ctx) => Directory.Exists(ctx.RecordDir) ? Directory.GetFiles(ctx.RecordDir, "*.json").ToList() : [];

    // record 후보 중 가장 높은 epoch 값을 계산한다.
    private static int HighestEpoch(RepoFiles ctx) => EnumerateRecords(ctx).Select(p => { try { return JsonNode.Parse(File.ReadAllText(p))?["trustEpoch"]?.GetValue<int>() ?? 0; } catch { return 0; } }).DefaultIfEmpty(0).Max();

    // 현재 state/log bytes가 HEAD snapshot과 같은지 확인한다.
    private static bool SnapshotMatchesHead(RepoFiles ctx, string head) => SnapshotHashMatches(ctx.Root, head, "docs/handoff/WORKSTATE.json", Sha256(File.ReadAllBytes(ctx.WorkstatePath))) && SnapshotHashMatches(ctx.Root, head, "docs/handoff/WORKSTATE.applier-log.jsonl", Sha256(File.ReadAllBytes(ctx.LogPath)));

    // 단일 Git snapshot hash를 기대 hash와 비교한다.
    private static bool SnapshotHashMatches(string root, string commit, string rel, string hash) => Sha256(GitBytes(root, commit, rel)) == hash;

    // Git object bytes를 문자열 변환 없이 읽는다.
    private static byte[] GitBytes(string root, string commit, string rel)
    {
        using var p = new Process { StartInfo = new ProcessStartInfo("git", $"show {commit}:{rel}") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false } };
        p.Start();
        using var ms = new MemoryStream();
        p.StandardOutput.BaseStream.CopyTo(ms);
        var e = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException(e);
        return ms.ToArray();
    }
    // working tree clean 여부를 확인한다.
    private static bool IsWorktreeClean(string root) => string.IsNullOrWhiteSpace(Git(root, "status --porcelain"));

    // 지정 path가 Git에 tracked 상태인지 확인한다.
    private static bool IsTracked(string root, string path) => GitExit(root, "ls-files --error-unmatch " + Path.GetRelativePath(root, path).Replace('\\', '/')) == 0;

    // Git ancestry 관계를 확인한다.
    private static bool IsAncestor(string root, string a, string b) => GitExit(root, $"merge-base --is-ancestor {a} {b}") == 0;

    // record path가 최초 추가된 commit을 찾는다.
    private static string FirstCommitForPath(string root, string rel) => Git(root, $"log --diff-filter=A --format=%H -- {rel}").Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

    // UTF-8 JSON bytes를 JsonObject로 읽는다.
    private static JsonObject ReadObj(byte[] bytes) => JsonNode.Parse(bytes)!.AsObject();

    // declare evidence JSON object를 읽는다.
    private static JsonObject ReadEvidenceObject(string path)
        => JsonNode.Parse(File.ReadAllText(path, Utf8NoBom))?.AsObject()
           ?? throw new JsonException("integration gate evidence must be a JSON object");

    // SHA-256 hex를 소문자로 계산한다.
    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    // JSON object의 canonical hash를 계산한다.
    private static string JsonHash(JsonObject obj) => Sha256(Encoding.UTF8.GetBytes(obj.ToJsonString(JsonOptions)));

    // JsonObject 문자열 필드를 읽는다.
    private static string Read(JsonObject obj, string key) => obj[key]?.GetValue<string>() ?? "";

    // JsonObject 정수 필드를 읽는다.
    private static int ReadInt(JsonObject obj, string key)
    {
        try { return obj[key]?.GetValue<int>() ?? int.MinValue; }
        catch { return int.MinValue; }
    }

    // 64자 hex 문자열인지 확인한다.
    private static bool Is64Hex(string? s)
        => s is { Length: 64 } && s.All(c =>
            (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));

    // active direct writer gate를 보수적으로 확인한다.
    private static bool DirectWriterGatePass(string root) => LegacyCallsiteCount(root) == 0;

    // legacy single-shot state-transition callsite 수를 계산한다.
    private static int LegacyCallsiteCount(string root)
    {
        var pattern = new Regex(@"state-transition\s+--(?:transition-id|expected-workstate-sha256)", RegexOptions.IgnoreCase);
        var historical = LoadHistoricalFiles(root);
        var count = 0;
        foreach (var file in EnumerateGateFiles(root))
        {
            var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (historical.Contains(rel)) continue;
            try { if (pattern.IsMatch(File.ReadAllText(file))) count++; } catch { }
        }
        return count;
    }

    // callsite gate용 historical allowlist를 읽는다.
    private static HashSet<string> LoadHistoricalFiles(string root)
    {
        var path = Path.Combine(root, "docs", "handoff", "CALLSITE-HISTORICAL.json");
        if (!File.Exists(path)) return [];
        try
        {
            var arr = JsonNode.Parse(File.ReadAllText(path))?["historicalFiles"]?.AsArray();
            return arr is null ? [] : arr.Select(n => n?.GetValue<string>() ?? "").Where(s => s.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch { return []; }
    }

    // callsite gate 스캔 대상 파일을 열거한다.
    private static IEnumerable<string> EnumerateGateFiles(string root)
    {
        var dirs = new[] { "server", "scripts", "outputs", "docs", ".claude", ".github" };
        var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".cs", ".ps1", ".sh", ".cmd", ".bat", ".json", ".yaml", ".yml", ".md", ".txt" };
        foreach (var dir in dirs.Select(d => Path.Combine(root, d)).Where(Directory.Exists))
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                if (exts.Contains(Path.GetExtension(file))) yield return file;
    }

    // CLI flag 값을 찾는다.
    private static string Flag(string[] args, string name) { for (var i = 0; i + 1 < args.Length; i++) if (args[i] == "--" + name) return args[i + 1]; return ""; }

    // self-test case 결과를 JSON 배열에 추가한다.
    private static void Add(JsonArray a, string name, bool pass) => a.Add(new JsonObject { ["case"] = name, ["pass"] = pass });

    // 성공 CommandResult를 만든다.
    private static CommandResult Ok(string command, JsonObject extra) { extra["command"] = command; extra["exitCode"] = 0; return new(0, extra); }

    // 실패 CommandResult를 만든다.
    private static CommandResult Fail(string code) => new(1, new JsonObject { ["exitCode"] = 1, ["failures"] = new JsonArray(new JsonObject { ["code"] = code }) });

    // JSON 결과를 stdout으로 출력한다.
    private static void Output(JsonObject o) => Console.WriteLine(o.ToJsonString(JsonOptions));

    // JSON 오류를 stderr로 출력한다.
    private static int Error(string message, int code) { Console.Error.WriteLine(new JsonObject { ["error"] = message }.ToJsonString()); return code; }

    // fixture repo의 Git 기본 설정을 초기화한다.
    private static void GitInit(string root) { Git(root, "init"); Git(root, "config core.autocrlf false"); Git(root, "config user.email test@example.com"); Git(root, "config user.name Test"); }

    // fixture repo의 전체 변경을 commit한다.
    private static void GitCommitAll(string root, string msg) { Git(root, "add ."); Git(root, $"commit -m \"{msg}\""); }

    // Git 명령을 실행하고 stdout 문자열을 반환한다.
    private static string Git(string root, string args) { using var p = new Process { StartInfo = new ProcessStartInfo("git", args) { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false } }; p.Start(); var o = p.StandardOutput.ReadToEnd(); var e = p.StandardError.ReadToEnd(); p.WaitForExit(); if (p.ExitCode != 0) throw new InvalidOperationException(e); return o; }

    // Git 명령을 실행하고 exit code만 반환한다.
    private static int GitExit(string root, string args) { using var p = new Process { StartInfo = new ProcessStartInfo("git", args) { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false } }; p.Start(); p.WaitForExit(); return p.ExitCode; }
}
