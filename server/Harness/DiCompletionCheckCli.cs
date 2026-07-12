// DI 완료 게이트 manifest를 읽고 각 검사 명령의 기대 exit code와 실제 exit code를 대조한다.
// 결과 증거는 outputs/gates 아래 JSON으로 남겨 다음 검수자가 재확인할 수 있게 한다.
using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class DiCompletionCheckCli
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly HashSet<string> BuiltInCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "measure",
        "verify-behavior",
    };

    // di-completion-check 진입점이다. exit 0=모든 기대 exit 일치, 1=불일치 또는 fail-closed.
    internal static int Run(string[] args)
    {
        var root = GitTools.FindRepoRoot();
        var options = ParseArgs(args);
        var manifestPath = ResolvePath(root, options.ManifestPath);

        if (!string.IsNullOrWhiteSpace(options.EmitDocPath))
            return EmitDoc(root, manifestPath, ResolvePath(root, options.EmitDocPath));

        var taskId = string.IsNullOrWhiteSpace(options.TaskId)
            ? DateTime.Now.ToString("yyyyMMdd-HHmmss")
            : SanitizeFileName(options.TaskId);
        var report = BuildBaseReport(options.GateId, manifestPath, taskId);

        try
        {
            if (!File.Exists(manifestPath))
                return FailClosed(root, taskId, report, "manifest-missing", $"manifest not found: {DisplayPath(root, manifestPath)}");

            var manifest = JsonNode.Parse(File.ReadAllText(manifestPath)) as JsonObject;
            if (manifest is null)
                return FailClosed(root, taskId, report, "manifest-malformed", "manifest root is not a JSON object");

            var gate = FindGate(manifest, options.GateId);
            if (gate is null)
                return FailClosed(root, taskId, report, "gate-missing", $"gate not found: {options.GateId}");

            var checks = gate["checks"] as JsonArray;
            if (checks is null || checks.Count == 0)
                return FailClosed(root, taskId, report, "checks-missing", "gate checks must be a non-empty array");

            var entries = new JsonArray();
            var failures = new JsonArray();
            var warnings = new JsonArray();
            foreach (var warning in MutatingWarnings(checks)) warnings.Add(warning);
            foreach (var warning in UnlistedHarnessWarnings(checks)) warnings.Add(warning);

            foreach (var check in checks.OfType<JsonObject>().OrderBy(ReadOrder))
            {
                var result = RunCheck(root, check);
                entries.Add(result.Report);
                if (!result.Passed) failures.Add(result.Failure);
            }

            report["checkCount"] = entries.Count;
            report["failureCount"] = failures.Count;
            report["warningCount"] = warnings.Count;
            report["verdict"] = failures.Count == 0 ? "PASS" : "FAIL";
            report["gateVerdict"] = failures.Count == 0 ? "PASS" : "FAIL";
            report["checks"] = entries;
            report["failures"] = failures;
            report["warnings"] = warnings;
            report["note"] = "Pass/fail is decided by expectedExit versus actual exit code. stdout/stderr are diagnostic evidence.";

            WriteEvidence(root, taskId, report);
            Console.WriteLine(report.ToJsonString(JsonOptions));
            return failures.Count == 0 ? 0 : 1;
        }
        catch (JsonException ex)
        {
            return FailClosed(root, taskId, report, "manifest-parse-failed", ex.Message);
        }
        catch (Exception ex)
        {
            return FailClosed(root, taskId, report, "harness-error", ex.Message);
        }
    }

    // manifest 검사 하나를 별도 dotnet 프로세스로 실행해 실제 exit code를 캡처한다.
    private static CheckRunResult RunCheck(string root, JsonObject check)
    {
        var command = ReadString(check, "command");
        var expectedExit = ReadInt(check, "expectedExit", int.MinValue);
        var args = ReadArgs(check);
        var order = ReadOrder(check);
        var mutatesState = ReadBool(check, "mutatesState");

        var report = new JsonObject
        {
            ["order"] = order,
            ["command"] = command,
            ["args"] = new JsonArray(args.Select(arg => (JsonNode)arg).ToArray()),
            ["expectedExit"] = expectedExit == int.MinValue ? null : expectedExit,
            ["mutatesState"] = mutatesState,
        };

        if (string.IsNullOrWhiteSpace(command) || expectedExit == int.MinValue)
        {
            report["verdict"] = "FAIL";
            report["reason"] = "command or expectedExit is missing";
            return new CheckRunResult(report, false, Failure(command, "check-contract-invalid", "command and expectedExit are required"));
        }

        if (!KnownCommand(command))
        {
            report["actualExit"] = 1;
            report["verdict"] = "FAIL-CLOSED";
            report["reason"] = "unknown command";
            return new CheckRunResult(report, false, Failure(command, "unknown-command", "manifest command is not registered"));
        }

        var started = DateTimeOffset.Now;
        var result = RunDotnetCommand(root, command, args);
        var passed = result.ExitCode == expectedExit;
        report["actualExit"] = result.ExitCode;
        report["durationMs"] = result.DurationMs;
        report["startedAt"] = started.ToString("O");
        report["verdict"] = passed ? "PASS" : "FAIL";
        report["stdoutTail"] = Tail(result.Stdout, 3000);
        report["stderrTail"] = Tail(result.Stderr, 3000);

        return new CheckRunResult(
            report,
            passed,
            Failure(command, "exit-mismatch", $"expected {expectedExit}, actual {result.ExitCode}"));
    }

    // dotnet run --no-build --project server -- <command> 형태로 기존 CLI를 재사용한다.
    private static ProcessRunResult RunDotnetCommand(string root, string command, List<string> args)
    {
        var stopwatch = Stopwatch.StartNew();
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("--no-build");
        psi.ArgumentList.Add("--project");
        psi.ArgumentList.Add("server");
        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add(command);
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("process start failed");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(180000))
        {
            process.Kill(entireProcessTree: true);
            stopwatch.Stop();
            return new ProcessRunResult(124, stdoutTask.Result, stderrTask.Result + "\nprocess timeout", stopwatch.ElapsedMilliseconds);
        }

        stopwatch.Stop();
        return new ProcessRunResult(process.ExitCode, stdoutTask.Result, stderrTask.Result, stopwatch.ElapsedMilliseconds);
    }

    // HARNESSES 문서를 manifest와 registry 목록에서 재생성한다.
    private static int EmitDoc(string root, string manifestPath, string outputPath)
    {
        if (!File.Exists(manifestPath))
        {
            Console.Error.WriteLine($"{{\"error\":\"manifest not found: {DisplayPath(root, manifestPath)}\"}}");
            return 1;
        }

        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath)) as JsonObject;
        if (manifest is null)
        {
            Console.Error.WriteLine("{\"error\":\"manifest root is not a JSON object\"}");
            return 1;
        }

        var gates = (manifest["gates"] as JsonArray)?.OfType<JsonObject>().ToList() ?? [];
        var listed = gates.SelectMany(gate => (gate["checks"] as JsonArray)?.OfType<JsonObject>() ?? [])
            .Select(check => ReadString(check, "command"))
            .Where(command => !string.IsNullOrWhiteSpace(command))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unlisted = HarnessRegistry.RegisteredNames
            .Where(name => !listed.Contains(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var lines = new List<string>
        {
            "<!-- GENERATED by 'dotnet run --project server -- di-completion-check --emit-doc docs/handoff/HARNESSES.md'. 직접 편집하지 마라. -->",
            "",
            "# HARNESSES — 실행 가능한 검사 목록",
            "",
            "> 원칙: **문서·보고·표현은 프록시다. 판정은 실체로, 규칙은 코드로.**",
            "> 이 문서는 `docs/handoff/GATE-MANIFEST.json`와 `HarnessRegistry`에서 생성한 목록이다.",
            "",
            "## 게이트",
        };

        foreach (var gateObject in gates)
        {
            lines.Add("");
            lines.Add($"### {ReadString(gateObject, "gateId")}");
            lines.Add("");
            lines.Add($"- triggeredBy: {ReadString(gateObject, "triggeredBy")}");
            lines.Add($"- description: {ReadString(gateObject, "description")}");
            lines.Add("");
            lines.Add("| 순서 | 명령 | 인자 | 기대 exit | 상태 변경 | 비고 |");
            lines.Add("| --- | --- | --- | --- | --- | --- |");

            foreach (var check in (gateObject["checks"] as JsonArray)?.OfType<JsonObject>().OrderBy(ReadOrder) ?? [])
            {
                var args = string.Join(" ", ReadArgs(check));
                lines.Add($"| {ReadOrder(check)} | `{ReadString(check, "command")}` | `{args}` | {ReadInt(check, "expectedExit", -1)} | {ReadBool(check, "mutatesState")} | {ReadString(check, "note")} |");
            }
        }

        lines.AddRange(new[]
        {
            "",
            "## Manifest 밖 등록 하네스",
            "",
            "| 명령 | 상태 |",
            "| --- | --- |",
        });
        foreach (var name in unlisted) lines.Add($"| `{name}` | unlisted |");

        lines.AddRange(new[]
        {
            "",
            "## 실행",
            "",
            "- 실행자 종료 직후: `dotnet run --project server -- di-completion-check --gate POST-EXECUTOR --task <taskId>`",
            "- 조율자 커밋 직후: `dotnet run --project server -- di-completion-check --gate POST-COMMIT --task <taskId>`",
            "- 증거 JSON: `outputs/gates/<taskId|timestamp>.gate.json`",
            "- 문서 재생성: `dotnet run --project server -- di-completion-check --emit-doc docs/handoff/HARNESSES.md`",
        });

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, string.Join(Environment.NewLine, lines) + Environment.NewLine, new UTF8Encoding(false));
        Console.WriteLine(new JsonObject
        {
            ["generated"] = DisplayPath(root, outputPath),
            ["manifest"] = DisplayPath(root, manifestPath),
            ["unlistedCount"] = unlisted.Count,
        }.ToJsonString(JsonOptions));
        return 0;
    }

    // 실패 닫힘 결과를 증거 파일과 stderr 양쪽에 남긴다.
    private static int FailClosed(string root, string taskId, JsonObject report, string code, string message)
    {
        report["verdict"] = "FAIL-CLOSED";
        report["gateVerdict"] = "FAIL";
        report["failureCount"] = 1;
        report["failures"] = new JsonArray(Failure("", code, message));
        WriteEvidence(root, taskId, report);
        Console.Error.WriteLine(report.ToJsonString(JsonOptions));
        return 1;
    }

    // 증거 JSON을 outputs/gates 아래에 기록한다.
    private static void WriteEvidence(string root, string taskId, JsonObject report)
    {
        var dir = Path.Combine(root, "outputs", "gates");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{SanitizeFileName(taskId)}.gate.json");
        report["evidencePath"] = DisplayPath(root, path);
        File.WriteAllText(path, report.ToJsonString(JsonOptions), new UTF8Encoding(false));
    }

    // CLI 인자를 기본값과 함께 해석한다.
    private static DiCompletionOptions ParseArgs(string[] args)
    {
        var options = new DiCompletionOptions("POST-EXECUTOR", "", "docs/handoff/GATE-MANIFEST.json", "");
        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Equals("--gate", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                options = options with { GateId = args[++i] };
            else if (arg.Equals("--task", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                options = options with { TaskId = args[++i] };
            else if (arg.Equals("--manifest", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                options = options with { ManifestPath = args[++i] };
            else if (arg.Equals("--emit-doc", StringComparison.OrdinalIgnoreCase))
                options = options with
                {
                    EmitDocPath = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                        ? args[++i]
                        : "docs/handoff/HARNESSES.md",
                };
        }

        return options;
    }

    // 보고서 기본 필드를 만든다.
    private static JsonObject BuildBaseReport(string gateId, string manifestPath, string taskId)
        => new()
        {
            ["harness"] = "di-completion-check",
            ["gateId"] = gateId,
            ["taskId"] = taskId,
            ["manifest"] = manifestPath.Replace('\\', '/'),
            ["createdAt"] = DateTimeOffset.Now.ToString("O"),
            ["gateVerdict"] = "FAIL",
        };

    // gateId에 맞는 gate 객체를 찾는다.
    private static JsonObject? FindGate(JsonObject manifest, string gateId)
        => (manifest["gates"] as JsonArray)?.OfType<JsonObject>()
            .FirstOrDefault(gate => ReadString(gate, "gateId").Equals(gateId, StringComparison.OrdinalIgnoreCase));

    // 상태 변경 검사를 경고로 드러낸다.
    private static IEnumerable<JsonObject> MutatingWarnings(JsonArray checks)
        => checks.OfType<JsonObject>()
            .Where(check => ReadBool(check, "mutatesState"))
            .Select(check => new JsonObject
            {
                ["command"] = ReadString(check, "command"),
                ["code"] = "mutates-state",
                ["message"] = "이 검사는 run-log/proposal 등 저장소 상태를 바꿀 수 있다.",
            });

    // registry에는 있으나 manifest에 없는 하네스를 경고로 드러낸다.
    private static IEnumerable<JsonObject> UnlistedHarnessWarnings(JsonArray checks)
    {
        var listed = checks.OfType<JsonObject>()
            .Select(check => ReadString(check, "command"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return HarnessRegistry.RegisteredNames
            .Where(name => !listed.Contains(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name => new JsonObject
            {
                ["command"] = name,
                ["code"] = "unlisted",
                ["message"] = "등록된 하네스가 manifest 게이트에 없다.",
            });
    }

    // manifest 명령이 실행 가능한 CLI인지 확인한다.
    private static bool KnownCommand(string command)
        => BuiltInCommands.Contains(command) || HarnessRegistry.RegisteredNames.Contains(command, StringComparer.OrdinalIgnoreCase);

    // JSON 문자열 배열 인자를 읽는다.
    private static List<string> ReadArgs(JsonObject check)
        => (check["args"] as JsonArray)?.Select(node => node?.ToString() ?? "").ToList() ?? [];

    // JSON 문자열 필드를 읽는다.
    private static string ReadString(JsonObject obj, string property)
        => obj[property]?.ToString() ?? "";

    // JSON 정수 필드를 읽는다.
    private static int ReadInt(JsonObject obj, string property, int fallback)
        => obj[property] is not null && int.TryParse(obj[property]!.ToString(), out var value) ? value : fallback;

    // JSON bool 필드를 읽는다.
    private static bool ReadBool(JsonObject obj, string property)
        => obj[property] is not null && bool.TryParse(obj[property]!.ToString(), out var value) && value;

    // order가 없으면 뒤쪽으로 보낸다.
    private static int ReadOrder(JsonObject obj)
        => ReadInt(obj, "order", int.MaxValue);

    // 실패 항목 JSON을 만든다.
    private static JsonObject Failure(string subject, string code, string message)
        => new()
        {
            ["subject"] = subject,
            ["code"] = code,
            ["message"] = message,
        };

    // 저장소 상대 또는 절대 경로를 해석한다.
    private static string ResolvePath(string root, string path)
        => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path));

    // 출력용 경로를 저장소 상대 경로로 바꾼다.
    private static string DisplayPath(string root, string path)
    {
        var full = Path.GetFullPath(path);
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return full.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            ? Path.GetRelativePath(root, full).Replace('\\', '/')
            : full.Replace('\\', '/');
    }

    // 파일명에 안전하지 않은 문자를 치환한다.
    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        return new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray());
    }

    // 긴 출력의 끝부분만 보존한다.
    private static string Tail(string text, int maxChars)
    {
        var normalized = text.Replace("\r\n", "\n").Trim();
        return normalized.Length <= maxChars ? normalized : normalized[^maxChars..];
    }
}

internal sealed record DiCompletionOptions(string GateId, string TaskId, string ManifestPath, string EmitDocPath);
internal sealed record ProcessRunResult(int ExitCode, string Stdout, string Stderr, long DurationMs);
internal sealed record CheckRunResult(JsonObject Report, bool Passed, JsonObject Failure);
