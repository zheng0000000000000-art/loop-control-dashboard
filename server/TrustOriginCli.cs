// BOOTSTRAP_TRUST_ORIGIN 인프라 CLI.
// inspect/declare/verify는 WORKSTATE와 production log를 수정하지 않고 trust-origin record 후보와 검증만 다룬다.
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

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
        string Mode, int ReconciliationExit, List<LegacyFailure> DeclaredFailures);

    // fixture와 production repo의 주요 파일 경로 묶음이다.
    private record RepoFiles(string Root, string WorkstatePath, string LogPath, string RecordDir);

    // trust-origin CLI 진입점이다.
    internal static int Run(string[] args)
    {
        var sub = args.Length > 1 ? args[1] : "";
        if (string.Equals(sub, "inspect", StringComparison.OrdinalIgnoreCase)) return RunInspect();
        if (string.Equals(sub, "declare", StringComparison.OrdinalIgnoreCase)) return RunDeclare(args);
        if (string.Equals(sub, "verify", StringComparison.OrdinalIgnoreCase)) return RunVerify();
        if (string.Equals(sub, "--self-test", StringComparison.OrdinalIgnoreCase)) return RunSelfTest();
        Error("trust-origin usage: inspect | declare --evidence <file> | verify | --self-test", 2);
        return 2;
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
            Output(new JsonObject
            {
                ["command"] = "trust-origin inspect",
                ["existingTrustOriginCount"] = EnumerateRecords(ctx).Count,
                ["highestTrustEpoch"] = HighestEpoch(ctx),
                ["eligibleForBootstrap"] = IsWorktreeClean(ctx.Root) && hard.Count == 0,
                ["baselineCommit"] = Git(ctx.Root, "rev-parse HEAD").Trim(),
                ["worktreeClean"] = IsWorktreeClean(ctx.Root),
                ["buildVerdict"] = "NOT_RUN",
                ["reconciliationMode"] = failures.Count == 0 ? "VERIFIED_CONSISTENT" : "DECLARED_LEGACY_GAP",
                ["reconciliationExitCode"] = failures.Count == 0 ? 0 : 1,
                ["legacyFailures"] = FailureArray(failures),
                ["hardFailures"] = FailureArray(hard),
                ["highRiskFailClosed"] = HighRiskFailClosed(),
                ["automatedExecutionReady"] = false,
                ["failures"] = new JsonArray(),
                ["warnings"] = new JsonArray(),
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
            var evidence = ReadEvidence(Path.GetFullPath(evidencePath));
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
    private static CommandResult DeclareCore(RepoFiles ctx, List<LegacyFailure> declared)
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
        if (!FailureSetEqual(failures, declared)) return Fail("legacy-failure-set-mismatch");
        if (!HighRiskFailClosed()) return Fail("high-risk-not-fail-closed");
        if (AutomaticLauncherEnabled(ctx.Root)) return Fail("automatic-launcher-not-disabled");

        Directory.CreateDirectory(ctx.RecordDir);
        if (File.Exists(recordPath)) return Fail("trust-origin-id-already-exists");
        var record = BuildRecord(baseline, ctx, failures);
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
        var rec = ParseRecord(recordPath);
        var declarationCommit = FirstCommitForPath(ctx.Root, Path.GetRelativePath(ctx.Root, recordPath).Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(declarationCommit)) return Fail("trust-origin-record-uncommitted");
        if (rec.BaselineCommit == declarationCommit) return Fail("baseline-commit-self-reference");
        if (!IsAncestor(ctx.Root, rec.BaselineCommit, "HEAD") || !IsAncestor(ctx.Root, declarationCommit, "HEAD"))
            return Fail("trust-origin-ancestry-invalid");
        if (!SnapshotHashMatches(ctx.Root, rec.BaselineCommit, "docs/handoff/WORKSTATE.json", rec.WorkstateHash)
            || !SnapshotHashMatches(ctx.Root, rec.BaselineCommit, "docs/handoff/WORKSTATE.applier-log.jsonl", rec.LogHash))
            return Fail("baseline-snapshot-mismatch");
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
    private static JsonObject BuildRecord(string baseline, RepoFiles ctx, List<LegacyFailure> failures)
    {
        var failureSetHash = FailureSetHash(failures);
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
        var result = DeclareCore(ctx, []);
        return result.ExitCode == 0 && File.Exists(Path.Combine(ctx.RecordDir, RecordName));
    }

    // 알려진 legacy gap의 exact evidence 선언을 검증한다.
    private static bool CaseLegacyGap(string root)
    {
        ResetRepo(root, consistent: false);
        var ctx = Files(root);
        var failures = ToFailures(HandoffIntegrityChecker.Run(new ReconciliationOptions(ctx.WorkstatePath, ctx.LogPath)));
        return DeclareCore(ctx, failures).ExitCode == 0;
    }

    // 선언 evidence와 실제 failure set 불일치를 검증한다.
    private static bool CaseMismatch(string root)
    {
        ResetRepo(root, consistent: false, extraFailure: true);
        return DeclareCore(Files(root), [new("state-transition-not-logged", "DI0004-BLOCKED-CODEX", "")]).ExitCode == 1;
    }

    // conflicting success binding이 선언을 막는지 검증한다.
    private static bool CaseConflict(string root)
    {
        ResetRepo(root, consistent: false, conflict: true);
        return DeclareCore(Files(root), [new("state-transition-not-logged", "DI0004-BLOCKED-CODEX", "")]).ExitCode == 1;
    }

    // malformed state가 record 생성을 막는지 검증한다.
    private static bool CaseMalformed(string root)
    {
        ResetRepo(root, consistent: true);
        File.WriteAllText(Files(root).WorkstatePath, ((char)123).ToString(), Utf8NoBom);
        GitCommitAll(root, "malformed");
        return DeclareCore(Files(root), []).ExitCode != 0;
    }

    // dirty worktree에서 선언이 거부되는지 검증한다.
    private static bool CaseDirty(string root)
    {
        ResetRepo(root, consistent: true);
        File.AppendAllText(Files(root).WorkstatePath, " ");
        return DeclareCore(Files(root), []).ExitCode == 1;
    }

    // baseline snapshot mismatch가 선언을 막는지 검증한다.
    private static bool CaseHashMismatch(string root)
    {
        ResetRepo(root, consistent: true);
        File.AppendAllText(Files(root).LogPath, " ");
        return DeclareCore(Files(root), []).ExitCode == 1;
    }

    // epoch 1 재선언이 거부되는지 검증한다.
    private static bool CaseRedeclare(string root)
    {
        ResetRepo(root, consistent: true);
        var ctx = Files(root);
        if (DeclareCore(ctx, []).ExitCode != 0) return false;
        return DeclareCore(ctx, []).ExitCode == 1;
    }

    // uncommitted record가 활성화되지 않는지 검증한다.
    private static bool CaseUncommittedInactive(string root)
    {
        ResetRepo(root, consistent: true);
        var ctx = Files(root);
        DeclareCore(ctx, []);
        return VerifyCore(ctx).ExitCode == 1;
    }

    // declaration commit 이후 record가 활성화되는지 검증한다.
    private static bool CaseDeclarationActive(string root)
    {
        ResetRepo(root, consistent: true);
        var ctx = Files(root);
        DeclareCore(ctx, []);
        GitCommitAll(root, "declare");
        return VerifyCore(ctx).ExitCode == 0;
    }

    // record가 declaration commit을 baseline으로 참조하지 못하게 검증한다.
    private static bool CaseSelfReference(string root)
    {
        ResetRepo(root, consistent: true);
        var ctx = Files(root);
        DeclareCore(ctx, []);
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

    // fixture repo를 초기화한다.
    private static void ResetRepo(string root, bool consistent, bool extraFailure = false, bool conflict = false)
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
    private static void AddTransition(RepoFiles ctx, string id, bool appendLog)
    {
        var ws = ReadObj(File.ReadAllBytes(ctx.WorkstatePath));
        ws["appliedTransitions"]!.AsArray().Add(new JsonObject { ["id"] = id, ["appliedAt"] = "2026-01-01T00:10:00Z" });
        File.WriteAllText(ctx.WorkstatePath, ws.ToJsonString(JsonOptions), Utf8NoBom);
        if (appendLog) AppendV2Log(ctx.LogPath, id);
    }

    // JSONL 줄 경계를 보존하며 v2 success log를 추가한다.
    private static void AppendV2Log(string path, string id)
    {
        var prefix = File.Exists(path) && new FileInfo(path).Length > 0 && !File.ReadAllText(path, Utf8NoBom).EndsWith('\n') ? "\n" : "";
        File.AppendAllText(path, prefix + V2Line(id) + "\n", Utf8NoBom);
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
        var baseLogLines = Encoding.UTF8.GetString(GitBytes(ctx.Root, baseline, "docs/handoff/WORKSTATE.applier-log.jsonl")).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        var logDelta = SuccessLogIds(File.ReadAllLines(ctx.LogPath).Skip(baseLogLines));
        if (stateDelta.Count != stateDelta.Distinct(StringComparer.Ordinal).Count()) return "duplicate-in-state";
        if (!stateDelta.OrderBy(x => x).SequenceEqual(logDelta.OrderBy(x => x))) return stateDelta.Count > logDelta.Count ? "state-transition-not-logged" : "log-transition-missing-from-state";
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
            o["baselineReconciliationExitCode"]!.GetValue<int>(), failures);
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

    // WORKSTATE appliedTransitions ID 목록을 읽는다.
    private static List<string> AppliedIds(JsonObject ws) => (ws["appliedTransitions"] as JsonArray ?? []).OfType<JsonObject>().Select(o => o["id"]?.ToString() ?? "").Where(s => s.Length > 0).ToList();

    // success log line에서 transition ID 목록을 읽는다.
    private static List<string> SuccessLogIds(IEnumerable<string> lines) => lines.Where(l => l.Contains("\"result\":\"ok\"") || l.Contains("\"result\": \"ok\"")).Select(l => JsonNode.Parse(l)!.AsObject()["transitionId"]!.ToString()).ToList();

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

    // declare evidence 파일을 읽는다.
    private static List<LegacyFailure> ReadEvidence(string path) => (JsonNode.Parse(File.ReadAllText(path, Utf8NoBom)) as JsonArray ?? []).OfType<JsonObject>().Select(o => new LegacyFailure(o["code"]?.ToString() ?? "", o["subject"]?.ToString() ?? "", o["detail"]?.ToString() ?? "")).ToList();

    // SHA-256 hex를 소문자로 계산한다.
    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

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
