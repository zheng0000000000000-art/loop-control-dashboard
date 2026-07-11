// dispatch 템플릿이 현행 코드에 적용된 뒤 빌드 가능한지 확인하는 하네스 CLI.
// 실제 저장소는 건드리지 않고 temp copy에서 dispatch-executor를 실행한다.
using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class TemplateSyncCheckCli
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // template-sync-check 진입점. exit 0=템플릿 적용 후 빌드 성공, 1=동기화 실패, 2=하네스 오류.
    internal static int Run(string[] args)
    {
        try
        {
            var root = GitTools.FindRepoRoot();
            var injectMissingTemplate = args.Any(arg => arg.Equals("--inject-missing-template", StringComparison.OrdinalIgnoreCase));
            var tempRoot = Path.Combine(Path.GetTempPath(), "lfwd-template-sync", Guid.NewGuid().ToString("N"));
            CopyWorkspaceSubset(root, tempRoot);

            if (injectMissingTemplate)
            {
                File.Delete(Path.Combine(tempRoot, "server", "dispatch-templates", "ApplyMeasurementResult.txt"));
            }

            var dispatch = RunDispatchInTemp(tempRoot);
            var build = dispatch.ExitCode == 0
                ? RunProcess("dotnet",
                    $"build \"{Path.Combine(tempRoot, "server", "LocalFirstWorkflowDashboard.Server.csproj")}\" -c Release " +
                    $"-o \"{Path.Combine(tempRoot, "template-sync-bin")}\" /p:UseRazorBuildServer=false /p:UseSharedCompilation=false",
                    tempRoot)
                : (ExitCode: 1, Stdout: "", Stderr: "dispatch failed; build skipped");

            var ok = dispatch.ExitCode == 0 && build.ExitCode == 0;
            var report = new JsonObject
            {
                ["harness"] = "template-sync-check",
                ["mode"] = injectMissingTemplate ? "injected-missing-template" : "default",
                ["tempRoot"] = tempRoot,
                ["dispatchExitCode"] = dispatch.ExitCode,
                ["buildExitCode"] = build.ExitCode,
                ["verdict"] = ok ? "PASS" : "FAIL",
                ["dispatchStdoutTail"] = Tail(dispatch.Stdout, 1200),
                ["dispatchStderrTail"] = Tail(dispatch.Stderr, 1200),
                ["buildStdoutTail"] = Tail(build.Stdout, 1200),
                ["buildStderrTail"] = Tail(build.Stderr, 1200),
                ["note"] = "Applies dispatch templates in a temp copy and uses build exit code as the source of truth.",
            };
            Console.WriteLine(report.ToJsonString(JsonOptions));
            return ok ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{{\"error\":\"template-sync-check failed: {ex.Message}\"}}");
            return 2;
        }
    }

    // server와 dispatch-executor가 필요한 dashboard 파일만 temp copy로 복사한다.
    private static void CopyWorkspaceSubset(string root, string tempRoot)
    {
        CopyDirectory(Path.Combine(root, "server"), Path.Combine(tempRoot, "server"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "dashboard"));
        File.Copy(Path.Combine(root, "dashboard", "app.js"), Path.Combine(tempRoot, "dashboard", "app.js"), overwrite: true);
    }

    // temp workspace를 현재 디렉터리로 두고 dispatch-executor를 직접 실행한다.
    private static (int ExitCode, string Stdout, string Stderr) RunDispatchInTemp(string tempRoot)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        var originalDirectory = Directory.GetCurrentDirectory();
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            Directory.SetCurrentDirectory(tempRoot);
            var exitCode = DispatchExecutorCli.Run(new[] { "dispatch-executor", "claude-code", "Program.cs Orchestrator.cs ProposalFlow.cs" });
            return (exitCode, stdout.ToString(), stderr.ToString());
        }
        catch (Exception ex)
        {
            return (1, stdout.ToString(), stderr + ex.Message);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    // 빌드 등 외부 프로세스의 exit code와 출력을 캡처한다.
    private static (int ExitCode, string Stdout, string Stderr) RunProcess(string fileName, string arguments, string workingDirectory)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("process start failed");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout, stderr);
    }

    // 디렉터리를 복사하되 bin/obj 산출물은 제외한다.
    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var dir in Directory.EnumerateDirectories(sourceDir))
        {
            var name = Path.GetFileName(dir);
            if (name.Equals("bin", StringComparison.OrdinalIgnoreCase)
                || name.Equals("obj", StringComparison.OrdinalIgnoreCase))
                continue;
            CopyDirectory(dir, Path.Combine(targetDir, name));
        }
    }

    // 긴 출력의 끝부분만 보존한다.
    private static string Tail(string text, int maxChars)
    {
        var normalized = text.Replace("\r\n", "\n").Trim();
        return normalized.Length <= maxChars ? normalized : normalized[^maxChars..];
    }
}
