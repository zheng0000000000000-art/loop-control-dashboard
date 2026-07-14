// 발사 프롬프트 전송 증거의 해시와 replay 이벤트 수를 검사하는 하네스 CLI.
// 모델 출력이 아니라 CLI가 되돌린 user 메시지 바이트의 무결성만 판정한다.
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

internal static class LaunchCheckCli
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly Regex Sha256Pattern = new("^[0-9a-fA-F]{64}$", RegexOptions.Compiled);

    // launch-check 진입점이다. exit 0=전송 증거 유효, 1=증거 누락 또는 불일치.
    internal static int Run(string[] args)
    {
        var root = GitTools.FindRepoRoot();
        var failures = new JsonArray();
        JsonObject evidence = new();
        var evidencePath = args.Length >= 3 ? args[2].Trim() : "";
        var fullPath = string.IsNullOrWhiteSpace(evidencePath)
            ? ""
            : Path.GetFullPath(Path.IsPathRooted(evidencePath) ? evidencePath : Path.Combine(root, evidencePath));

        try
        {
            if (args.Length < 3)
            {
                failures.Add(Failure("usage", "usage: launch-check <taskId> <transportEvidencePath>"));
                return Finish(root, args.ElementAtOrDefault(1) ?? "", fullPath, false, evidence, failures, "skipped");
            }

            var taskId = args[1].Trim();
            if (string.IsNullOrWhiteSpace(taskId) || string.IsNullOrWhiteSpace(evidencePath))
            {
                failures.Add(Failure("missing-argument", "taskId and transportEvidencePath are required"));
                return Finish(root, taskId, fullPath, false, evidence, failures, "skipped");
            }

            evidence = ReadEvidence(fullPath, failures);
            if (failures.Count == 0)
            {
                CheckEvidence(taskId, evidence, failures);
                CheckBudget(root, evidence, failures, out var budgetStatus);
                return Finish(root, taskId, fullPath, true, evidence, failures, budgetStatus);
            }

            return Finish(root, taskId, fullPath, File.Exists(fullPath), evidence, failures, "skipped");
        }
        catch (Exception ex)
        {
            failures.Add(Failure("harness-error", ex.Message));
            return Finish(root, args.ElementAtOrDefault(1) ?? "", fullPath, File.Exists(fullPath), evidence, failures, "skipped");
        }
    }

    // evidence 파일을 JSON 객체로 읽고 파싱 실패를 기록한다.
    private static JsonObject ReadEvidence(string fullPath, JsonArray failures)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
        {
            failures.Add(Failure("evidence-missing", "transport evidence file does not exist"));
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(File.ReadAllText(fullPath)) as JsonObject
                ?? throw new JsonException("transport evidence root must be a JSON object");
        }
        catch (JsonException ex)
        {
            failures.Add(Failure("json-parse-failed", ex.Message));
            return new JsonObject();
        }
    }

    // 필수 필드와 전송 해시 판정을 검사한다.
    private static void CheckEvidence(string expectedTaskId, JsonObject evidence, JsonArray failures)
    {
        RequireInt(evidence, "schemaVersion", 1, failures);
        RequireString(evidence, "taskId", expectedTaskId, failures);
        RequireStringPresent(evidence, "cliVersion", failures);
        RequireHash(evidence, "sourceSha256", failures);
        var payloadSha256 = RequireHash(evidence, "payloadSha256", failures);
        var replaySha256 = RequireHash(evidence, "replaySha256", failures);
        RequireNonNegativeLong(evidence, "sourceByteLength", failures);
        RequireNonNegativeLong(evidence, "payloadByteLength", failures);
        RequireNonNegativeLong(evidence, "replayByteLength", failures);
        RequireLong(evidence, "replayEventCount", 1, failures);
        CheckPayloadFrameEvidence(evidence, failures);
        CheckExecutorPidEvidence(evidence, failures);
        RequireNonNegativeLong(evidence, "pid", failures);
        RequireStringPresent(evidence, "startedAt", failures);
        RequireStringPresent(evidence, "exitedAt", failures);
        RequireAllowedVerdict(evidence, failures);
        RequireVerdictConsistency(evidence, payloadSha256, replaySha256, failures);
        RejectCommandLineFallback(evidence, failures);

        if (!string.IsNullOrWhiteSpace(payloadSha256)
            && !string.IsNullOrWhiteSpace(replaySha256)
            && !payloadSha256.Equals(replaySha256, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add(Failure("TRANSPORT_INVALID", "payloadSha256 and replaySha256 differ"));
        }
    }

    // cmd stdin redirection transport에서는 wrapperPid와 실제 executorPid가 분리되어야 한다.
    private static void CheckExecutorPidEvidence(JsonObject evidence, JsonArray failures)
    {
        var stdinTransport = ReadString(evidence, "stdinTransport");
        if (!stdinTransport.Equals("cmd-stdin-file-redirection", StringComparison.Ordinal)) return;

        var wrapperPid = ReadLong(evidence, "wrapperPid");
        var executorPid = ReadLong(evidence, "executorPid");
        var pid = ReadLong(evidence, "pid");
        if (wrapperPid <= 0)
            failures.Add(Failure("wrapper-pid-missing", "wrapperPid must be positive for cmd stdin redirection"));
        if (executorPid <= 0)
            failures.Add(Failure("executor-pid-missing", "executorPid must be positive for cmd stdin redirection"));
        if (wrapperPid > 0 && executorPid > 0 && wrapperPid == executorPid)
            failures.Add(Failure("executor-pid-not-discovered", "executorPid must differ from wrapperPid for cmd stdin redirection"));
        if (executorPid > 0 && pid != executorPid)
            failures.Add(Failure("pid-mismatch", "pid must equal executorPid for cmd stdin redirection"));
        if (ReadBool(evidence, "executorPidDiscovered") != true)
            failures.Add(Failure("executor-pid-not-discovered", "executorPidDiscovered must be true for cmd stdin redirection"));
    }

    // 새 런처 evidence가 stdin stream-json 프레임의 첫 바이트와 BOM 여부를 기록하면 엄격히 검사한다.
    private static void CheckPayloadFrameEvidence(JsonObject evidence, JsonArray failures)
    {
        var prefix = ReadString(evidence, "payloadFramePrefixHex").Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(prefix))
        {
            if (!prefix.StartsWith("7b", StringComparison.Ordinal))
                failures.Add(Failure("stdin-frame-prefix", "payloadFramePrefixHex must start with 7b, the JSON object opening byte"));
            if (prefix.StartsWith("efbbbf", StringComparison.Ordinal))
                failures.Add(Failure("stdin-frame-bom", "stdin stream-json frame starts with UTF-8 BOM"));
        }

        if (ReadBool(evidence, "payloadFrameBomPresent") == true)
            failures.Add(Failure("stdin-frame-bom", "payloadFrameBomPresent must be false"));

        var frameLength = ReadLong(evidence, "payloadFrameByteLength");
        if (evidence.ContainsKey("payloadFrameByteLength") && frameLength <= 0)
            failures.Add(Failure("invalid-field", "payloadFrameByteLength must be positive when present"));

        var frameSha = ReadString(evidence, "payloadFrameSha256").Trim();
        if (!string.IsNullOrWhiteSpace(frameSha) && !Sha256Pattern.IsMatch(frameSha))
            failures.Add(Failure("invalid-field", "payloadFrameSha256 must be a full 64-character sha256 when present"));
    }

    // 예산 정책 파일이 있으면 초과 예외 조건만 실패로 기록한다.
    private static void CheckBudget(string root, JsonObject evidence, JsonArray failures, out string budgetStatus)
    {
        budgetStatus = "skipped";
        var policyPath = Path.Combine(root, "docs", "handoff", "LAUNCH-BUDGET.json");
        if (!File.Exists(policyPath)) return;

        var policy = JsonNode.Parse(File.ReadAllText(policyPath)) as JsonObject;
        var maxPayloadBytes = policy?["policy"]?["maxPayloadBytes"]?.GetValue<long?>()
            ?? policy?["maxPayloadBytes"]?.GetValue<long?>();
        if (maxPayloadBytes is null)
        {
            budgetStatus = "skipped";
            return;
        }

        var payloadByteLength = ReadLong(evidence, "payloadByteLength");
        var splitDecision = ReadString(evidence, "splitDecision");
        var exceptionReason = ReadString(evidence, "budgetExceptionReason");
        budgetStatus = "pass";
        if (payloadByteLength > maxPayloadBytes
            && string.IsNullOrWhiteSpace(splitDecision)
            && string.IsNullOrWhiteSpace(exceptionReason))
        {
            budgetStatus = "fail";
            failures.Add(Failure("budget-policy", "payloadByteLength exceeds policy.maxPayloadBytes without splitDecision or budgetExceptionReason"));
        }
    }

    // evidence verdict 필드가 계약 문자열인지 확인한다.
    private static void RequireAllowedVerdict(JsonObject evidence, JsonArray failures)
    {
        var verdict = ReadString(evidence, "verdict");
        if (verdict is "TRANSPORT_VALID" or "TRANSPORT_INVALID") return;
        failures.Add(Failure("invalid-field", "verdict must be TRANSPORT_VALID or TRANSPORT_INVALID"));
    }

    // evidence verdict가 해시 일치 여부와 같은지 확인한다.
    private static void RequireVerdictConsistency(
        JsonObject evidence,
        string payloadSha256,
        string replaySha256,
        JsonArray failures)
    {
        var verdict = ReadString(evidence, "verdict");
        if (string.IsNullOrWhiteSpace(payloadSha256) || string.IsNullOrWhiteSpace(replaySha256)) return;
        var expected = payloadSha256.Equals(replaySha256, StringComparison.OrdinalIgnoreCase)
            ? "TRANSPORT_VALID"
            : "TRANSPORT_INVALID";
        if (verdict.Equals(expected, StringComparison.Ordinal)) return;
        failures.Add(Failure("invalid-field", $"verdict must be {expected} for the recorded hashes"));
    }

    // 조용한 명령행 프롬프트 폴백 흔적이 있으면 실패한다.
    private static void RejectCommandLineFallback(JsonObject evidence, JsonArray failures)
    {
        foreach (var name in new[] { "inputMode", "inputTransport", "promptTransport", "transport" })
        {
            var value = ReadString(evidence, name);
            if (value.Contains("command", StringComparison.OrdinalIgnoreCase)
                || value.Contains("argument", StringComparison.OrdinalIgnoreCase)
                || value.Contains("argv", StringComparison.OrdinalIgnoreCase))
            {
                failures.Add(Failure("command-line-fallback", $"{name} indicates command-line prompt fallback"));
            }
        }
    }

    // 문자열 필드가 기대값과 같은지 확인한다.
    private static void RequireString(JsonObject evidence, string name, string expected, JsonArray failures)
    {
        var actual = ReadString(evidence, name);
        if (actual.Equals(expected, StringComparison.Ordinal)) return;
        failures.Add(Failure("invalid-field", $"{name} must equal {expected}"));
    }

    // 문자열 필드가 비어 있지 않은지 확인한다.
    private static void RequireStringPresent(JsonObject evidence, string name, JsonArray failures)
    {
        if (!string.IsNullOrWhiteSpace(ReadString(evidence, name))) return;
        failures.Add(Failure("missing-field", $"{name} is required"));
    }

    // sha256 필드가 64자리 전체 해시인지 확인하고 소문자로 반환한다.
    private static string RequireHash(JsonObject evidence, string name, JsonArray failures)
    {
        var value = ReadString(evidence, name).Trim();
        if (Sha256Pattern.IsMatch(value)) return value.ToLowerInvariant();
        failures.Add(Failure("invalid-field", $"{name} must be a full 64-character sha256"));
        return "";
    }

    // 정수 필드가 기대값과 같은지 확인한다.
    private static void RequireInt(JsonObject evidence, string name, int expected, JsonArray failures)
    {
        var actual = ReadLong(evidence, name);
        if (actual == expected) return;
        failures.Add(Failure("invalid-field", $"{name} must equal {expected}"));
    }

    // 정수 필드가 기대값과 같은지 확인한다.
    private static void RequireLong(JsonObject evidence, string name, long expected, JsonArray failures)
    {
        var actual = ReadLong(evidence, name);
        if (actual == expected) return;
        failures.Add(Failure("invalid-field", $"{name} must equal {expected}"));
    }

    // 정수 필드가 0 이상인지 확인한다.
    private static void RequireNonNegativeLong(JsonObject evidence, string name, JsonArray failures)
    {
        var actual = ReadLong(evidence, name);
        if (actual >= 0) return;
        failures.Add(Failure("invalid-field", $"{name} must be a non-negative integer"));
    }

    // JSON 숫자 필드를 long으로 읽고 실패 시 -1을 반환한다.
    private static long ReadLong(JsonObject evidence, string name)
    {
        try
        {
            return evidence[name]?.GetValue<long>() ?? -1;
        }
        catch
        {
            return -1;
        }
    }

    // JSON 문자열 필드를 읽고 없으면 빈 문자열을 반환한다.
    private static string ReadString(JsonObject evidence, string name)
    {
        return evidence[name]?.GetValue<string>() ?? "";
    }

    // JSON bool 필드를 읽고 실패 시 null을 반환한다.
    private static bool? ReadBool(JsonObject evidence, string name)
    {
        try
        {
            return evidence[name]?.GetValue<bool>();
        }
        catch
        {
            return null;
        }
    }

    // 실패 항목을 JSON 객체로 만든다.
    private static JsonObject Failure(string code, string message)
    {
        return new JsonObject
        {
            ["code"] = code,
            ["message"] = message,
        };
    }

    // 최종 보고서를 출력하고 exit code를 반환한다.
    private static int Finish(
        string root,
        string taskId,
        string fullPath,
        bool evidenceExists,
        JsonObject evidence,
        JsonArray failures,
        string budgetStatus)
    {
        var payloadSha256 = ReadString(evidence, "payloadSha256");
        var replaySha256 = ReadString(evidence, "replaySha256");
        var payloadFrameBomPresent = ReadBool(evidence, "payloadFrameBomPresent");
        var executorPidDiscovered = ReadBool(evidence, "executorPidDiscovered");
        var valid = failures.Count == 0
            && payloadSha256.Equals(replaySha256, StringComparison.OrdinalIgnoreCase);
        var report = new JsonObject
        {
            ["harness"] = "launch-check",
            ["taskId"] = taskId,
            ["transportEvidencePath"] = DisplayPath(root, fullPath),
            ["evidenceExists"] = evidenceExists,
            ["schemaVersion"] = ReadLong(evidence, "schemaVersion"),
            ["payloadSha256"] = payloadSha256,
            ["replaySha256"] = replaySha256,
            ["payloadByteLength"] = ReadLong(evidence, "payloadByteLength"),
            ["payloadFramePrefixHex"] = ReadString(evidence, "payloadFramePrefixHex"),
            ["payloadFrameBomPresent"] = payloadFrameBomPresent.HasValue ? JsonValue.Create(payloadFrameBomPresent.Value) : null,
            ["wrapperPid"] = ReadLong(evidence, "wrapperPid"),
            ["executorPid"] = ReadLong(evidence, "executorPid"),
            ["executorPidDiscovered"] = executorPidDiscovered.HasValue ? JsonValue.Create(executorPidDiscovered.Value) : null,
            ["replayByteLength"] = ReadLong(evidence, "replayByteLength"),
            ["replayEventCount"] = ReadLong(evidence, "replayEventCount"),
            ["budgetCheck"] = budgetStatus,
            ["failureCount"] = failures.Count,
            ["failures"] = failures,
            ["verdict"] = valid ? "TRANSPORT_VALID" : "TRANSPORT_INVALID",
            ["note"] = "This verdict proves only byte-level transport integrity between payload and CLI replay.",
        };

        Console.WriteLine(report.ToJsonString(JsonOptions));
        return valid ? 0 : 1;
    }

    // 출력용 경로를 저장소 상대 경로로 만든다.
    private static string DisplayPath(string root, string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return "";
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            ? Path.GetRelativePath(root, fullPath).Replace('\\', '/')
            : fullPath.Replace('\\', '/');
    }
}
