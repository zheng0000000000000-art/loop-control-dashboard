// 격리 실행 outbox와 반입 대기 상태를 관리한다.
// 저장소 사본 실행, 변경 추출, 반입 적용을 처리한다.
using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

public sealed class OutboxManager
{
    private static readonly SemaphoreSlim DispatchLock = new(1, 1);
    private static readonly HashSet<string> SupportedExecutors = new(StringComparer.OrdinalIgnoreCase)
    {
        "claude-code",
        "codex",
        "ollama",
    };
    private readonly string workspaceRoot;
    private readonly string outboxRoot;
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // outbox 루트 경로를 준비한다.
    public OutboxManager(string workspaceRoot)
    {
        this.workspaceRoot = Path.GetFullPath(workspaceRoot);
        outboxRoot = Path.Combine(this.workspaceRoot, "outbox");
        Directory.CreateDirectory(outboxRoot);
    }

    // 실행 지시를 격리 사본에서 실행하고 outbox 항목을 만든다.
    public async Task<JsonObject> DispatchAsync(string projectId, JsonObject body, string configuredToken, string providedToken)
    {
        RequireDispatchToken(configuredToken, providedToken);
        var instruction = body["instruction"]?.GetValue<string>() ?? "";
        var executor = body["executor"]?.GetValue<string>() ?? "";

        if (!SupportedExecutors.Contains(executor))
        {
            throw new DispatchHttpException(400, "dispatch.invalid_executor", "executor must be claude-code, codex, or ollama");
        }

        var taskId = $"task-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
        var taskDirectory = Path.Combine(outboxRoot, taskId);
        Directory.CreateDirectory(taskDirectory);

        var questions = CheckInstructionGate(instruction);
        if (questions.Count > 0)
        {
            var meta = BaseMeta(taskId, projectId, executor, instruction, "needs_questions", 0, 0);
            meta["questions"] = new JsonArray(questions.Select(question => JsonValue.Create(question)).ToArray<JsonNode?>());
            WriteJson(Path.Combine(taskDirectory, "meta.json"), meta);
            WriteJson(Path.Combine(taskDirectory, "questions.json"), meta["questions"]!.DeepClone());
            return meta;
        }

        if (!await DispatchLock.WaitAsync(0))
        {
            throw new DispatchHttpException(409, "dispatch.busy", "another dispatch is already running");
        }

        try
        {
            var stopwatch = Stopwatch.StartNew();
            var copyRoot = Path.Combine(Path.GetTempPath(), $"loop-dispatch-{taskId}");
            CopyWorkspace(copyRoot);
            var execution = await RunExecutorAsync(copyRoot, executor, instruction);
            var changes = CollectChanges(copyRoot, taskDirectory);
            var measure = await RunMeasureAsync(copyRoot);
            var behavior = await RunVerifyBehaviorAsync(copyRoot);
            stopwatch.Stop();

            var strictGate = RequiresStrictGate(instruction);
            var passedStrictGate = !strictGate || (measure.ExitCode == 0 && behavior.ExitCode == 0);
            var hasChanges = changes.ChangedFiles.Count > 0 || changes.DeletedFiles.Count > 0;
            var status = execution.TimedOut || execution.ExitCode != 0 || !hasChanges || !passedStrictGate ? "failed" : "import_pending";
            var meta = BaseMeta(taskId, projectId, executor, instruction, status, stopwatch.ElapsedMilliseconds, SubscriptionCalls(executor));
            meta["executorExitCode"] = execution.ExitCode;
            meta["timedOut"] = execution.TimedOut;
            meta["changedFiles"] = new JsonArray(changes.ChangedFiles.Select(file => JsonValue.Create(file)).ToArray<JsonNode?>());
            meta["deletedFiles"] = new JsonArray(changes.DeletedFiles.Select(file => JsonValue.Create(file)).ToArray<JsonNode?>());
            meta["measureExitCode"] = measure.ExitCode;
            meta["measureSummary"] = measure.Stdout;
            meta["behaviorExitCode"] = behavior.ExitCode;
            meta["behaviorSummary"] = behavior.Stdout;
            meta["strictGate"] = strictGate;
            meta["completedAt"] = DateTimeOffset.Now.ToString("O");

            WriteText(Path.Combine(taskDirectory, "executor-report.md"), BuildExecutorReport(execution));
            WriteText(Path.Combine(taskDirectory, "diff.patch"), changes.Patch);
            WriteJson(Path.Combine(taskDirectory, "measure-result.json"), new JsonObject
            {
                ["exitCode"] = measure.ExitCode,
                ["stdout"] = measure.Stdout,
                ["stderr"] = measure.Stderr,
            });
            WriteJson(Path.Combine(taskDirectory, "behavior-result.json"), new JsonObject
            {
                ["exitCode"] = behavior.ExitCode,
                ["stdout"] = behavior.Stdout,
                ["stderr"] = behavior.Stderr,
            });
            WriteJson(Path.Combine(taskDirectory, "meta.json"), meta);
            TryDeleteDirectory(copyRoot);
            return meta;
        }
        finally
        {
            DispatchLock.Release();
        }
    }

    // 반입 대기 항목과 질문 대기 항목을 inbox 배열에 추가한다.
    public void AddInboxItems(JsonArray items)
    {
        foreach (var metaPath in Directory.EnumerateFiles(outboxRoot, "meta.json", SearchOption.AllDirectories))
        {
            var meta = ReadJson(metaPath);
            var status = meta["status"]?.GetValue<string>() ?? "";

            if (status != "import_pending" && status != "needs_questions")
            {
                continue;
            }

            items.Add(new JsonObject
            {
                ["projectId"] = meta["projectId"]?.GetValue<string>() ?? "",
                ["projectName"] = meta["projectId"]?.GetValue<string>() ?? "",
                ["kind"] = status == "import_pending" ? "import_pending" : "dispatch_questions",
                ["taskId"] = meta["taskId"]?.GetValue<string>() ?? Path.GetFileName(Path.GetDirectoryName(metaPath)),
                ["title"] = status == "import_pending" ? "반입 대기" : "지시 보완 필요",
                ["waitingSince"] = meta["createdAt"]?.GetValue<string>() ?? DateTimeOffset.Now.ToString("O"),
                ["summary"] = meta["instruction"]?.GetValue<string>() ?? "",
                ["assignableTo"] = status == "import_pending" ? "human" : "agent_or_human",
            });
        }
    }

    // outbox 항목 상세를 반환한다.
    public JsonObject ReadTask(string taskId)
    {
        var taskDirectory = ResolveTaskDirectory(taskId);
        var meta = ReadJson(Path.Combine(taskDirectory, "meta.json"));
        meta["diff"] = File.Exists(Path.Combine(taskDirectory, "diff.patch"))
            ? File.ReadAllText(Path.Combine(taskDirectory, "diff.patch"), Encoding.UTF8)
            : "";
        return meta;
    }

    // 반입 대기 diff를 본 저장소에 적용한다.
    public JsonObject ApproveImport(string taskId, string configuredToken, string providedToken)
    {
        RequireDispatchToken(configuredToken, providedToken);
        var taskDirectory = ResolveTaskDirectory(taskId);
        var metaPath = Path.Combine(taskDirectory, "meta.json");
        var meta = ReadJson(metaPath);

        if (meta["status"]?.GetValue<string>() != "import_pending")
        {
            throw new DispatchHttpException(409, "dispatch.not_import_pending", "task is not waiting for import");
        }

        CreateImportRestore(taskDirectory, meta);
        foreach (var file in meta["changedFiles"]?.AsArray().Select(node => node?.GetValue<string>()).Where(value => !string.IsNullOrWhiteSpace(value)) ?? [])
        {
            var destination = SafeWorkspacePath(file!);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(Path.Combine(taskDirectory, "files", file!.Replace('/', Path.DirectorySeparatorChar)), destination, overwrite: true);
        }

        foreach (var file in meta["deletedFiles"]?.AsArray().Select(node => node?.GetValue<string>()).Where(value => !string.IsNullOrWhiteSpace(value)) ?? [])
        {
            var destination = SafeWorkspacePath(file!);
            if (File.Exists(destination))
            {
                File.Delete(destination);
            }
        }

        meta["status"] = "imported";
        meta["importedAt"] = DateTimeOffset.Now.ToString("O");
        WriteJson(metaPath, meta);
        return meta;
    }

    // 반입 대기 diff를 거절 상태로 표시한다.
    public JsonObject RejectImport(string taskId, JsonObject body, string configuredToken, string providedToken)
    {
        RequireDispatchToken(configuredToken, providedToken);
        var taskDirectory = ResolveTaskDirectory(taskId);
        var metaPath = Path.Combine(taskDirectory, "meta.json");
        var meta = ReadJson(metaPath);
        meta["status"] = "rejected";
        meta["rejectedAt"] = DateTimeOffset.Now.ToString("O");
        meta["rejectReason"] = body["reason"]?.GetValue<string>() ?? "";
        WriteJson(metaPath, meta);
        return meta;
    }

    // 토큰 설정과 헤더 값을 검사한다.
    private static void RequireDispatchToken(string configuredToken, string providedToken)
    {
        if (string.IsNullOrWhiteSpace(configuredToken) ||
            !string.Equals(configuredToken, providedToken, StringComparison.Ordinal))
        {
            throw new DispatchHttpException(401, "auth.invalid_token", "valid X-Action-Token is required for dispatch");
        }
    }

    // 지시의 범위와 완료 기준을 간단히 검사한다.
    private static List<string> CheckInstructionGate(string instruction)
    {
        var questions = new List<string>();

        if (string.IsNullOrWhiteSpace(instruction))
        {
            questions.Add("수행할 지시 본문을 알려 달라.");
            return questions;
        }

        if (!ContainsAny(instruction, ["README", ".md", ".cs", ".js", "server", "dashboard", "docs", "data", "/"]))
        {
            questions.Add("대상 파일이나 범위를 지정해 달라.");
        }

        if (!ContainsAny(instruction, ["검증", "확인", "완료", "기준", "테스트", "verify", "check", "done"]))
        {
            questions.Add("완료 기준이나 검증 방법을 지정해 달라.");
        }

        return questions;
    }

    // 문자열에 후보 토큰이 포함되는지 확인한다.
    private static bool ContainsAny(string text, IEnumerable<string> tokens)
    {
        return tokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    // 기본 메타 JSON을 만든다.
    private static JsonObject BaseMeta(string taskId, string projectId, string executor, string instruction, string status, long durationMs, int subscriptionCalls)
    {
        return new JsonObject
        {
            ["schemaVersion"] = 2,
            ["taskId"] = taskId,
            ["projectId"] = projectId,
            ["executor"] = executor,
            ["instruction"] = instruction,
            ["status"] = status,
            ["createdAt"] = DateTimeOffset.Now.ToString("O"),
            ["durationMs"] = durationMs,
            ["cost"] = new JsonObject
            {
                ["estimatedUSD"] = 0,
                ["subscriptionCalls"] = subscriptionCalls,
                ["role"] = "runtime",
            },
        };
    }

    // 실행자별 구독 호출 수를 계산한다.
    private static int SubscriptionCalls(string executor)
    {
        return executor.Equals("claude-code", StringComparison.OrdinalIgnoreCase) ||
            executor.Equals("codex", StringComparison.OrdinalIgnoreCase)
            ? 1
            : 0;
    }

    // 작업 공간을 임시 사본으로 복사한다.
    private void CopyWorkspace(string copyRoot)
    {
        if (Directory.Exists(copyRoot))
        {
            Directory.Delete(copyRoot, recursive: true);
        }

        Directory.CreateDirectory(copyRoot);
        CopyDirectory(workspaceRoot, copyRoot);
    }

    // 디렉터리를 제외 규칙에 맞춰 복사한다.
    private void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            var name = Path.GetFileName(directory);
            if (name is ".git" or "bin" or "obj" or "outbox" || directory.Contains($"{Path.DirectorySeparatorChar}history", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            CopyDirectory(directory, Path.Combine(destination, name));
        }

        foreach (var file in Directory.EnumerateFiles(source))
        {
            if (ShouldIgnoreCopyFile(file))
            {
                continue;
            }

            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }
    }

    // 실행 사본에 필요 없는 작업 부산물을 제외한다.
    private static bool ShouldIgnoreCopyFile(string file)
    {
        var name = Path.GetFileName(file);
        return name.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".pid", StringComparison.OrdinalIgnoreCase);
    }

    // 격리 사본에서 실행자 CLI를 실행한다.
    private static async Task<ProcessResult> RunExecutorAsync(string copyRoot, string executor, string instruction)
    {
        return await RunProcessAsync(copyRoot, "dotnet", ["run", "--project", "server", "--", "dispatch-executor", executor, instruction], 5);
    }

    // 격리 사본에서 dev-pack 측정을 실행한다.
    private static async Task<ProcessResult> RunMeasureAsync(string copyRoot)
    {
        return await RunProcessAsync(copyRoot, "dotnet", ["run", "--project", "server", "--", "measure", "dev-pack"], 60);
    }

    // 격리 사본에서 동작 동일성을 검증한다.
    private static async Task<ProcessResult> RunVerifyBehaviorAsync(string copyRoot)
    {
        return await RunProcessAsync(copyRoot, "dotnet", ["run", "--project", "server", "--", "verify-behavior"], 60);
    }

    // 엄격한 반입 판정이 필요한 지시인지 확인한다.
    private static bool RequiresStrictGate(string instruction)
    {
        return instruction.Contains("verify-behavior", StringComparison.OrdinalIgnoreCase) ||
            instruction.Contains("Program.cs", StringComparison.OrdinalIgnoreCase) ||
            instruction.Contains("구조 지표", StringComparison.OrdinalIgnoreCase);
    }

    // 외부 프로세스를 셸 없이 실행하고 결과를 수집한다.
    private static async Task<ProcessResult> RunProcessAsync(string workingDirectory, string fileName, IEnumerable<string> arguments, int timeoutSeconds)
    {
        var output = new StringBuilder();
        var error = new StringBuilder();
        using var process = new Process();
        process.StartInfo.FileName = fileName;
        process.StartInfo.WorkingDirectory = workingDirectory;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.OutputDataReceived += (_, line) => { if (line.Data is not null) output.AppendLine(line.Data); };
        process.ErrorDataReceived += (_, line) => { if (line.Data is not null) error.AppendLine(line.Data); };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var completed = await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(timeoutSeconds)).ContinueWith(task => !task.IsFaulted);
        if (!completed)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // 종료된 프로세스는 무시한다.
            }

            return new ProcessResult(-1, output.ToString(), error.ToString(), true);
        }

        return new ProcessResult(process.ExitCode, output.ToString(), error.ToString(), false);
    }

    // 원본과 사본의 변경 목록과 diff 텍스트를 만든다.
    private ChangeSet CollectChanges(string copyRoot, string taskDirectory)
    {
        var changed = new List<string>();
        var deleted = new List<string>();
        var patch = new StringBuilder();
        var filesDirectory = Path.Combine(taskDirectory, "files");
        Directory.CreateDirectory(filesDirectory);
        var paths = RelativeFiles(workspaceRoot).Union(RelativeFiles(copyRoot), StringComparer.OrdinalIgnoreCase)
            .Where(path => !ShouldIgnoreRelative(path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var relative in paths)
        {
            var original = Path.Combine(workspaceRoot, relative);
            var modified = Path.Combine(copyRoot, relative);
            var originalExists = File.Exists(original);
            var modifiedExists = File.Exists(modified);

            if (originalExists && !modifiedExists)
            {
                deleted.Add(ToSlash(relative));
                patch.AppendLine($"deleted {ToSlash(relative)}");
                continue;
            }

            if (!modifiedExists || AreSame(original, modified))
            {
                continue;
            }

            var slash = ToSlash(relative);
            changed.Add(slash);
            var storedPath = Path.Combine(filesDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(storedPath)!);
            File.Copy(modified, storedPath, overwrite: true);
            AppendFilePatch(patch, slash, originalExists ? File.ReadAllText(original, Encoding.UTF8) : "", File.ReadAllText(modified, Encoding.UTF8));
        }

        return new ChangeSet(changed, deleted, patch.ToString());
    }

    // 파일별 단순 diff 텍스트를 추가한다.
    private static void AppendFilePatch(StringBuilder patch, string relative, string before, string after)
    {
        patch.AppendLine($"--- a/{relative}");
        patch.AppendLine($"+++ b/{relative}");
        patch.AppendLine("@@");
        foreach (var line in before.Split('\n'))
        {
            patch.AppendLine("-" + line.TrimEnd('\r'));
        }

        foreach (var line in after.Split('\n'))
        {
            patch.AppendLine("+" + line.TrimEnd('\r'));
        }
    }

    // 상대 파일 목록을 만든다.
    private static IEnumerable<string> RelativeFiles(string root)
    {
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path));
    }

    // 상대 경로 제외 여부를 판단한다.
    private static bool ShouldIgnoreRelative(string relative)
    {
        var normalized = ToSlash(relative);
        var name = Path.GetFileName(normalized);
        return normalized.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("outbox/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/history/", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".pid", StringComparison.OrdinalIgnoreCase);
    }

    // 두 파일 내용이 같은지 확인한다.
    private static bool AreSame(string first, string second)
    {
        if (!File.Exists(first) || !File.Exists(second))
        {
            return false;
        }

        return File.ReadAllBytes(first).SequenceEqual(File.ReadAllBytes(second));
    }

    // 실행 결과 보고서를 만든다.
    private static string BuildExecutorReport(ProcessResult execution)
    {
        return $"""
        # Executor Report

        exitCode: {execution.ExitCode}
        timedOut: {execution.TimedOut}

        ## stdout
        {execution.Stdout}

        ## stderr
        {execution.Stderr}
        """;
    }

    // 반입 전 현재 파일을 outbox restore 폴더에 복사한다.
    private void CreateImportRestore(string taskDirectory, JsonObject meta)
    {
        var restoreDirectory = Path.Combine(taskDirectory, "restore-before-import");
        Directory.CreateDirectory(restoreDirectory);
        foreach (var file in meta["changedFiles"]?.AsArray().Select(node => node?.GetValue<string>()).Where(value => !string.IsNullOrWhiteSpace(value)) ?? [])
        {
            var source = SafeWorkspacePath(file!);
            if (!File.Exists(source))
            {
                continue;
            }

            var destination = Path.Combine(restoreDirectory, file!.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
        }
    }

    // 작업 항목 디렉터리를 안전하게 계산한다.
    private string ResolveTaskDirectory(string taskId)
    {
        var fullPath = Path.GetFullPath(Path.Combine(outboxRoot, taskId));

        if (!fullPath.StartsWith(outboxRoot, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(fullPath))
        {
            throw new DispatchHttpException(404, "dispatch.not_found", "outbox task not found");
        }

        return fullPath;
    }

    // 작업 공간 내부 경로를 안전하게 계산한다.
    private string SafeWorkspacePath(string relative)
    {
        var fullPath = Path.GetFullPath(Path.Combine(workspaceRoot, relative.Replace('/', Path.DirectorySeparatorChar)));

        if (!fullPath.StartsWith(workspaceRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new DispatchHttpException(400, "path.invalid", "path is outside workspace");
        }

        return fullPath;
    }

    // JSON 파일을 기록한다.
    private void WriteJson(string path, JsonNode node)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, node.ToJsonString(jsonOptions), Encoding.UTF8);
    }

    // JSON 파일을 읽는다.
    private static JsonObject ReadJson(string path)
    {
        return JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8))!.AsObject();
    }

    // 텍스트 파일을 기록한다.
    private static void WriteText(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, Encoding.UTF8);
    }

    // 임시 디렉터리를 정리한다.
    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // 임시 파일 정리 실패는 실행 결과를 막지 않는다.
        }
    }

    // 경로 구분자를 슬래시로 통일한다.
    private static string ToSlash(string path)
    {
        return path.Replace('\\', '/');
    }
}

public sealed class DispatchHttpException : Exception
{
    public int StatusCode { get; }
    public string ReasonCode { get; }

    // HTTP 응답에 쓸 dispatch 오류를 만든다.
    public DispatchHttpException(int statusCode, string reasonCode, string message) : base(message)
    {
        StatusCode = statusCode;
        ReasonCode = reasonCode;
    }
}

public sealed record ProcessResult(int ExitCode, string Stdout, string Stderr, bool TimedOut);

public sealed record ChangeSet(List<string> ChangedFiles, List<string> DeletedFiles, string Patch);
