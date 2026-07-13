// WORKSTATE handoff integrity harness.
// Checks that the current handoff record is backed by real files, hashes, and completion artifacts.
// --workstate + --applier-log 둘 다 지정 시 fixture 격리 모드: reconciliation+malformed+blockers만 실행.
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class HandoffIntegrityCli
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly HashSet<string> DoneStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "done", "completed", "complete", "pass", "passed",
    };

    // handoff-integrity entry. exit 0=integrity verified, 1=contract gap, 2=harness error.
    internal static int Run(string[] args)
    {
        try
        {
            if (args.Any(a => string.Equals(a, "--pending-transition", StringComparison.OrdinalIgnoreCase)))
            {
                Console.Error.WriteLine("{\"error\":\"pending-not-allowed-on-cli\",\"code\":\"pending-not-allowed-on-cli\"}");
                return 1;
            }
            var root = GitTools.FindRepoRoot();
            if (args.Any(a => string.Equals(a, "--self-test", StringComparison.OrdinalIgnoreCase)))
                return RunSelfTest(root);
            var (workstatePath, applierLogPath, fixtureMode) = ParsePaths(args, root);
            if (!File.Exists(workstatePath))
                { Console.Error.WriteLine("{\"error\":\"WORKSTATE.json not found\"}"); return 2; }
            var parsed = JsonNode.Parse(File.ReadAllText(workstatePath)) as JsonObject;
            if (parsed is null)
                { Console.Error.WriteLine("{\"error\":\"WORKSTATE.json is not a JSON object\"}"); return 2; }
            return RunChecks(root, parsed, workstatePath, applierLogPath, fixtureMode);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{{\"error\":\"handoff-integrity failed: {ex.Message}\"}}");
            return 2;
        }
    }

    // --workstate / --applier-log 인자를 파싱해 절대 경로와 모드를 반환한다.
    private static (string workstatePath, string applierLogPath, bool fixtureMode) ParsePaths(string[] args, string root)
    {
        string? workstateArg = null;
        string? applierLogArg = null;
        for (int i = 1; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--workstate", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                workstateArg = args[++i];
            else if (string.Equals(args[i], "--applier-log", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                applierLogArg = args[++i];
            else if (workstateArg is null && !args[i].StartsWith("--"))
                workstateArg = args[i];
        }
        var wsPath = workstateArg is not null
            ? Path.GetFullPath(Path.IsPathRooted(workstateArg) ? workstateArg : Path.Combine(root, workstateArg))
            : Path.Combine(root, "docs", "handoff", "WORKSTATE.json");
        var logPath = applierLogArg is not null
            ? Path.GetFullPath(Path.IsPathRooted(applierLogArg) ? applierLogArg : Path.Combine(root, applierLogArg))
            : Path.Combine(root, "docs", "handoff", "WORKSTATE.applier-log.jsonl");
        return (wsPath, logPath, applierLogArg is not null);
    }

    // 모든 검사를 실행하고 보고서를 출력한다.
    private static int RunChecks(
        string root, JsonObject parsed, string workstatePath, string applierLogPath, bool fixtureMode)
    {
        var failures = new JsonArray();
        var warnings = new JsonArray();
        var changedFiles = new JsonArray();
        var diId = ReadString(parsed, "diId");
        var status = ReadString(parsed, "status");

        var reconc = RunReconciliation(workstatePath, applierLogPath, failures, warnings);
        if (reconc.harnessErrors > 0)
        {
            Console.WriteLine(new JsonObject
            {
                ["harness"] = "handoff-integrity",
                ["workstate"] = Path.GetRelativePath(root, workstatePath).Replace('\\', '/'),
                ["fixtureMode"] = fixtureMode,
                ["verdict"] = "HARNESS_ERROR",
                ["failures"] = failures, ["warnings"] = warnings,
            }.ToJsonString(JsonOptions));
            return 2;
        }

        if (!fixtureMode)
            RunFullModeChecks(root, parsed, diId, status, failures, warnings, changedFiles);
        CheckBlockerConsistency(parsed, status, failures);

        Console.WriteLine(new JsonObject
        {
            ["harness"] = "handoff-integrity",
            ["workstate"] = Path.GetRelativePath(root, workstatePath).Replace('\\', '/'),
            ["applierLog"] = Path.GetRelativePath(root, applierLogPath).Replace('\\', '/'),
            ["fixtureMode"] = fixtureMode,
            ["diId"] = diId, ["status"] = status,
            ["changedFileCount"] = changedFiles.Count,
            ["failureCount"] = failures.Count, ["warningCount"] = warnings.Count,
            ["verdict"] = failures.Count == 0 ? "PASS" : "FAIL",
            ["reconciliation"] = reconc.reconciliationResult,
            ["changedFiles"] = changedFiles, ["failures"] = failures, ["warnings"] = warnings,
            ["note"] = fixtureMode
                ? "Fixture isolation mode: reconciliation+blockers only."
                : "Full mode: changedFiles hash, completion, queue, reconciliation.",
        }.ToJsonString(JsonOptions));
        return failures.Count == 0 ? 0 : 1;
    }

    // full 모드에서 changedFiles·completion·queue를 검사한다.
    private static void RunFullModeChecks(
        string root, JsonObject parsed, string diId, string status,
        JsonArray failures, JsonArray warnings, JsonArray changedFiles)
    {
        CheckRequired(parsed, "schemaVersion", failures);
        CheckRequired(parsed, "diId", failures);
        CheckRequired(parsed, "status", failures);
        var changed = parsed["changedFiles"] as JsonArray;
        if (changed is null || changed.Count == 0)
            failures.Add(Failure("changedFiles", "missing", "changedFiles must be a non-empty array"));
        else
            foreach (var item in changed.OfType<JsonObject>())
                changedFiles.Add(CheckChangedFile(root, item, failures));
        CheckCompletionArtifacts(root, parsed, diId, status, failures, warnings);
        CheckQueueStatus(root, diId, status, failures, warnings);
    }

    // HandoffIntegrityChecker를 실행하고 결과를 failures/warnings에 병합한다.
    private static (int harnessErrors, JsonNode? reconciliationResult) RunReconciliation(
        string workstatePath, string applierLogPath,
        JsonArray failures, JsonArray warnings)
    {
        var opts = new ReconciliationOptions(workstatePath, applierLogPath, null);
        var result = HandoffIntegrityChecker.Run(opts);

        foreach (var e in result.HarnessErrors)
            Console.Error.WriteLine($"{{\"harness-error\":\"{e.Code}\",\"subject\":\"{e.Subject}\",\"message\":\"{e.Message}\"}}");

        if (result.HarnessErrors.Count > 0)
            return (result.HarnessErrors.Count, null);

        foreach (var f in result.Failures)
            failures.Add(Failure(f.Subject, f.Code, f.Message));
        foreach (var w in result.Warnings)
            warnings.Add(Warning(w.Subject, w.Code, w.Message));

        JsonNode? metricsNode = null;
        if (result.Metrics is { } m)
        {
            metricsNode = new JsonObject
            {
                ["appliedTransitionCount"] = m.AppliedTransitionCount,
                ["successfulLogEntryCount"] = m.SuccessfulLogEntryCount,
                ["successfulLogIdCount"] = m.SuccessfulLogIdCount,
                ["duplicateSuccessLogCount"] = m.DuplicateSuccessLogCount,
                ["pendingExemptionApplied"] = m.PendingExemptionApplied,
                ["reconciliation"] = m.Reconciliation,
            };
        }

        return (0, metricsNode);
    }

    // 필수 최상위 WORKSTATE 필드를 확인한다.
    private static void CheckRequired(JsonObject root, string property, JsonArray failures)
    {
        if (root[property] is null || string.IsNullOrWhiteSpace(root[property]?.ToString()))
            failures.Add(Failure($"workstate.{property}", "missing", "required field is absent or empty"));
    }

    // changedFiles 항목 하나를 파일시스템·해시와 대조한다.
    private static JsonObject CheckChangedFile(string root, JsonObject item, JsonArray failures)
    {
        var path = ReadString(item, "path");
        var entry = new JsonObject
        {
            ["path"] = path,
            ["exists"] = false,
            ["hashField"] = "",
            ["hashMatches"] = null,
        };

        if (string.IsNullOrWhiteSpace(path))
        {
            failures.Add(Failure("changedFiles.path", "missing", "changedFiles item has no path"));
            return entry;
        }

        var full = Path.GetFullPath(Path.Combine(root, path));
        var insideRoot = full.Equals(root, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        if (!insideRoot)
        {
            failures.Add(Failure(path, "outside-root", "changedFiles path escapes repository root"));
            return entry;
        }

        var exists = File.Exists(full) || Directory.Exists(full);
        entry["exists"] = exists;
        if (!exists)
        {
            failures.Add(Failure(path, "missing-file", "changedFiles path does not exist"));
            return entry;
        }

        var expectedHash = ReadString(item, "sha256");
        var hashField = "sha256";
        if (string.IsNullOrWhiteSpace(expectedHash))
        {
            expectedHash = ReadString(item, "hash");
            hashField = "hash";
        }

        entry["hashField"] = string.IsNullOrWhiteSpace(expectedHash) ? "" : hashField;
        if (string.IsNullOrWhiteSpace(expectedHash))
        {
            failures.Add(Failure(path, "missing-hash", "changedFiles item lacks sha256/hash"));
            return entry;
        }

        if (Directory.Exists(full))
        {
            failures.Add(Failure(path, "directory-hash-unsupported", "changedFiles hash can only be verified for files"));
            return entry;
        }

        var actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(full))).ToLowerInvariant();
        var normalizedExpected = expectedHash.Trim().ToLowerInvariant().Replace("sha256:", "");
        var matches = actualHash.Equals(normalizedExpected, StringComparison.OrdinalIgnoreCase);
        entry["actualSha256"] = actualHash;
        entry["hashMatches"] = matches;
        if (!matches)
            failures.Add(Failure(path, "hash-mismatch", "changedFiles hash does not match current file content"));

        return entry;
    }

    // 완료 상태의 handoff가 실제 verification 아티팩트를 가리키는지 확인한다.
    private static void CheckCompletionArtifacts(string root, JsonObject workstate, string diId, string status, JsonArray failures, JsonArray warnings)
    {
        if (!DoneStatuses.Contains(status)) return;

        var changed = workstate["changedFiles"] as JsonArray;
        var verificationInChangedFiles = changed?.OfType<JsonObject>()
            .Select(o => ReadString(o, "path"))
            .Any(p => p.StartsWith("docs/verification/", StringComparison.OrdinalIgnoreCase)
                && File.Exists(Path.Combine(root, p))) == true;

        var verificationById = false;
        var verificationDir = Path.Combine(root, "docs", "verification");
        if (!verificationInChangedFiles && Directory.Exists(verificationDir) && !string.IsNullOrWhiteSpace(diId))
        {
            var normalized = NormalizeId(diId);
            verificationById = Directory.EnumerateFiles(verificationDir, "*.md")
                .Any(path => NormalizeId(Path.GetFileNameWithoutExtension(path)).Contains(normalized));
        }

        if (!verificationInChangedFiles && !verificationById)
            failures.Add(Failure(diId, "missing-verification", "done status requires an existing docs/verification artifact"));
        else if (!verificationInChangedFiles)
            warnings.Add(Warning(diId, "verification-not-in-changedFiles", "verification exists by id but is not listed in current changedFiles"));
    }

    // WORKSTATE diId가 큐 파일에서 열린 상태인지 대조한다.
    private static void CheckQueueStatus(string root, string diId, string status, JsonArray failures, JsonArray warnings)
    {
        if (string.IsNullOrWhiteSpace(diId)) return;

        var queueFiles = new[]
        {
            "docs/handoff/CODEX-QUEUE.md",
            "docs/handoff/SONNET-QUEUE.md",
        };
        var mentions = new JsonArray();
        foreach (var rel in queueFiles)
        {
            var full = Path.Combine(root, rel);
            if (!File.Exists(full)) continue;
            foreach (var line in File.ReadLines(full).Where(l => l.Contains(diId, StringComparison.OrdinalIgnoreCase)))
            {
                mentions.Add(new JsonObject
                {
                    ["queue"] = rel,
                    ["line"] = line.Trim(),
                });
                if (DoneStatuses.Contains(status) && IsOpenQueueLine(line))
                    failures.Add(Failure(rel, "queue-status-mismatch", $"{diId} is done in WORKSTATE but open in queue line"));
            }
        }

        if (mentions.Count == 0)
            warnings.Add(Warning(diId, "queue-mention-missing", "diId is not mentioned in CODEX or SONNET queue"));
    }

    // blocked 상태에 blockers[]가 없거나 비어 있으면 blockers-missing, done 상태에 blockers[]가 남으면 blockers-stale.
    private static void CheckBlockerConsistency(JsonObject workstate, string status, JsonArray failures)
    {
        var blockers = workstate["blockers"] as JsonArray;
        var hasBlockers = blockers is not null && blockers.Count > 0;
        var blocked = status.Contains("block", StringComparison.OrdinalIgnoreCase);
        if (blocked && !hasBlockers)
            failures.Add(Failure("blockers", "blockers-missing", "blocked status requires a non-empty blockers array"));
        if (!blocked && hasBlockers && DoneStatuses.Contains(status))
            failures.Add(Failure("blockers", "blockers-stale", "done status must not carry a non-empty blockers array"));
    }

    // 큐 행이 아직 열린 상태를 뜻하는지 판정한다.
    private static bool IsOpenQueueLine(string line)
        => line.Contains("대기", StringComparison.Ordinal)
            || line.Contains("진행", StringComparison.Ordinal)
            || line.Contains("pending", StringComparison.OrdinalIgnoreCase)
            || line.Contains("in progress", StringComparison.OrdinalIgnoreCase);

    // JSON 객체에서 문자열 속성을 안전하게 읽는다.
    private static string ReadString(JsonObject obj, string property)
        => obj[property]?.ToString() ?? "";

    // 지시서 ID와 파일명을 비교하기 쉬운 영숫자 소문자로 정규화한다.
    private static string NormalizeId(string value)
        => new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    // 실패 항목을 공통 JSON 형식으로 만든다.
    private static JsonObject Failure(string subject, string code, string message)
        => new() { ["subject"] = subject, ["code"] = code, ["message"] = message };

    // 경고 항목을 공통 JSON 형식으로 만든다.
    private static JsonObject Warning(string subject, string code, string message)
        => new() { ["subject"] = subject, ["code"] = code, ["message"] = message };

    // pending fixture 6종을 in-process로 실행해 기대 결과(면제·실패코드·하네스오류)와 대조한다. 단언 실행기 — 인자 없음.
    private static int RunSelfTest(string root)
    {
        var cases = new[]
        {
            (name: "pending-ok",          pendingId: "PENDING-OK",      expectExemption: true,
             expectFailureCodes: Array.Empty<string>(),          expectHarnessErrors: false),
            (name: "pending-failed-log",  pendingId: "PENDING-FAILED",  expectExemption: false,
             expectFailureCodes: new[] { "state-transition-not-logged" }, expectHarnessErrors: false),
            (name: "pending-success-log", pendingId: "PENDING-SUCCESS", expectExemption: false,
             expectFailureCodes: Array.Empty<string>(),          expectHarnessErrors: false),
            (name: "pending-duplicate",   pendingId: "PENDING-DUP",     expectExemption: false,
             expectFailureCodes: new[] { "duplicate-in-state", "state-transition-not-logged" }, expectHarnessErrors: false),
            (name: "pending-mismatch",    pendingId: "WRONG-ID",        expectExemption: false,
             expectFailureCodes: new[] { "state-transition-not-logged" }, expectHarnessErrors: false),
            (name: "pending-nonok-zero",  pendingId: "PENDING-NONOK",   expectExemption: false,
             expectFailureCodes: new[] { "state-transition-not-logged" }, expectHarnessErrors: false),
        };

        var mismatches = new JsonArray();
        foreach (var (name, pendingId, expectExemption, expectFailureCodes, expectHarnessErrors) in cases)
        {
            var dir = Path.Combine(root, "docs", "qa", "fixtures", "reconciliation", "pending", name);
            var r = HandoffIntegrityChecker.Run(new ReconciliationOptions(
                Path.Combine(dir, "workstate.json"), Path.Combine(dir, "applier-log.jsonl"), pendingId));
            var entry = BuildSelfTestMismatch(name, r, expectExemption, expectFailureCodes, expectHarnessErrors);
            if (entry is not null) mismatches.Add(entry);
        }

        if (mismatches.Count == 0)
        {
            Console.WriteLine(new JsonObject
            {
                ["selfTest"] = "handoff-integrity-pending",
                ["verdict"] = "PASS",
                ["casesRun"] = cases.Length,
            }.ToJsonString(JsonOptions));
            return 0;
        }

        Console.Error.WriteLine(new JsonObject
        {
            ["selfTest"] = "handoff-integrity-pending",
            ["verdict"] = "FAIL",
            ["mismatchCount"] = mismatches.Count,
            ["mismatches"] = mismatches,
        }.ToJsonString(JsonOptions));
        return 1;
    }

    // case 1건의 기대 vs 실제를 대조해 불일치 엔트리를 반환한다. 일치하면 null.
    private static JsonObject? BuildSelfTestMismatch(
        string name, ReconciliationResult r,
        bool expectExemption, string[] expectFailureCodes, bool expectHarnessErrors)
    {
        var actualHasHarnessErrors = r.HarnessErrors.Count > 0;
        var actualExemption = r.Metrics?.PendingExemptionApplied ?? false;
        var actualCodes = new HashSet<string>(r.Failures.Select(f => f.Code), StringComparer.Ordinal);
        var expectedCodes = new HashSet<string>(expectFailureCodes, StringComparer.Ordinal);

        // HarnessErrors가 예상 밖이면 unexpected-harness-error — 입력 오염과 검사 실패를 구분한다.
        var harnessErrorMismatch = actualHasHarnessErrors != expectHarnessErrors;
        var codesMismatch = !actualCodes.SetEquals(expectedCodes);
        var exemptionMismatch = actualExemption != expectExemption;
        if (!harnessErrorMismatch && !codesMismatch && !exemptionMismatch) return null;

        var entry = new JsonObject
        {
            ["case"] = name,
            ["expectedPendingExemptionApplied"] = expectExemption,
            ["actualPendingExemptionApplied"] = actualExemption,
            ["expectedFailureCodes"] = new JsonArray(expectFailureCodes.Select(c => (JsonNode)JsonValue.Create(c)!).ToArray()),
            ["actualFailureCodes"] = new JsonArray(actualCodes.OrderBy(c => c).Select(c => (JsonNode)JsonValue.Create(c)!).ToArray()),
            ["expectedHarnessErrors"] = expectHarnessErrors,
            ["actualHarnessErrors"] = actualHasHarnessErrors,
            ["harnessErrors"] = new JsonArray(r.HarnessErrors.Select(e =>
                (JsonNode)new JsonObject { ["code"] = e.Code, ["subject"] = e.Subject }).ToArray()),
            ["mismatchReason"] = harnessErrorMismatch ? "unexpected-harness-error"
                               : codesMismatch        ? "failure-code-mismatch"
                                                      : "exemption-mismatch",
        };
        return entry;
    }
}
