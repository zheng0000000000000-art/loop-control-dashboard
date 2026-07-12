// WORKSTATE handoff integrity harness.
// Checks that the current handoff record is backed by real files, hashes, and completion artifacts.
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

    // handoff-integrity entry. exit 0=handoff is backed by concrete artifacts, 1=contract gap, 2=harness error.
    internal static int Run(string[] args)
    {
        try
        {
            var root = GitTools.FindRepoRoot();
            var workstatePath = args.Length > 1
                ? Path.GetFullPath(Path.IsPathRooted(args[1]) ? args[1] : Path.Combine(root, args[1]))
                : Path.Combine(root, "docs", "handoff", "WORKSTATE.json");

            if (!File.Exists(workstatePath))
            {
                Console.Error.WriteLine("{\"error\":\"WORKSTATE.json not found\"}");
                return 2;
            }

            var parsed = JsonNode.Parse(File.ReadAllText(workstatePath)) as JsonObject;
            if (parsed is null)
            {
                Console.Error.WriteLine("{\"error\":\"WORKSTATE.json is not a JSON object\"}");
                return 2;
            }

            var failures = new JsonArray();
            var warnings = new JsonArray();
            var changedFiles = new JsonArray();

            var diId = ReadString(parsed, "diId");
            var status = ReadString(parsed, "status");
            CheckRequired(parsed, "schemaVersion", failures);
            CheckRequired(parsed, "diId", failures);
            CheckRequired(parsed, "status", failures);

            var changed = parsed["changedFiles"] as JsonArray;
            if (changed is null || changed.Count == 0)
            {
                failures.Add(Failure("changedFiles", "missing", "changedFiles must be a non-empty array"));
            }
            else
            {
                foreach (var item in changed.OfType<JsonObject>())
                {
                    changedFiles.Add(CheckChangedFile(root, item, failures));
                }
            }

            CheckCompletionArtifacts(root, parsed, diId, status, failures, warnings);
            CheckQueueStatus(root, diId, status, failures, warnings);
            CheckBlockerConsistency(parsed, status, failures);

            var report = new JsonObject
            {
                ["harness"] = "handoff-integrity",
                ["workstate"] = Path.GetRelativePath(root, workstatePath).Replace('\\', '/'),
                ["diId"] = diId,
                ["status"] = status,
                ["changedFileCount"] = changed?.Count ?? 0,
                ["failureCount"] = failures.Count,
                ["warningCount"] = warnings.Count,
                ["verdict"] = failures.Count == 0 ? "PASS" : "FAIL",
                ["changedFiles"] = changedFiles,
                ["failures"] = failures,
                ["warnings"] = warnings,
                ["note"] = "Read-only check. Hash fields are required for current changedFiles because handoff must be machine-verifiable.",
            };

            Console.WriteLine(report.ToJsonString(JsonOptions));
            return failures.Count == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{{\"error\":\"handoff-integrity failed: {ex.Message}\"}}");
            return 2;
        }
    }

    // Checks a required top-level WORKSTATE field.
    private static void CheckRequired(JsonObject root, string property, JsonArray failures)
    {
        if (root[property] is null || string.IsNullOrWhiteSpace(root[property]?.ToString()))
            failures.Add(Failure($"workstate.{property}", "missing", "required field is absent or empty"));
    }

    // Verifies one changedFiles item against the filesystem and optional hash value.
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

    // Checks that a done handoff points to a real verification artifact.
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

    // Compares WORKSTATE status against queue rows that mention the same diId.
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

    // blocked 상태에서 blockers[] 배열이 비어 있거나 없으면 failure, completed 상태에서 blockers[]가 남아 있으면 stale failure.
    private static void CheckBlockerConsistency(JsonObject workstate, string status, JsonArray failures)
    {
        var blockers = workstate["blockers"] as JsonArray;
        var hasBlockers = blockers is not null && blockers.Count > 0;
        var blocked = status.Contains("block", StringComparison.OrdinalIgnoreCase);
        if (blocked && !hasBlockers)
            failures.Add(Failure("blockers", "missing", "blocked status requires a non-empty blockers array"));
        if (!blocked && hasBlockers && DoneStatuses.Contains(status))
            failures.Add(Failure("blockers", "stale", "done status must not carry a non-empty blockers array"));
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
        => new()
        {
            ["subject"] = subject,
            ["code"] = code,
            ["message"] = message,
        };

    // 경고 항목을 공통 JSON 형식으로 만든다.
    private static JsonObject Warning(string subject, string code, string message)
        => new()
        {
            ["subject"] = subject,
            ["code"] = code,
            ["message"] = message,
        };
}
