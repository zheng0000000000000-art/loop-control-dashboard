// dotnet build 종료 코드를 정본으로 삼고 임시 경로에서 실행하는 빌드 검증 하네스.
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

internal static class BuildVerifyCli
{

    // build-verify 진입점. exit 0=빌드 성공, 1=빌드 실패, 2=하네스 오류.
    internal static int Run(string[] args)
    {
        try
        {
            // 오타 난 픽스처 옵션이 기본 server 빌드로 빠지는 것을 막는다.
            var optionFailure = CliOptions.Validate(args, 1, ["fixture"], []);
            if (optionFailure is not null)
            {
                Console.Error.WriteLine(new JsonObject { ["error"] = optionFailure }.ToJsonString());
                return 2;
            }

            var root = GitTools.FindRepoRoot();
            var fixtureMode = args.Length >= 2
                && string.Equals(args[1], "--fixture", StringComparison.OrdinalIgnoreCase);
            if (fixtureMode && args.Length < 3)
                throw new ArgumentException("--fixture 뒤에 디렉터리 경로가 필요합니다.");

            var project = fixtureMode ? args[2] : args.Length >= 2 ? args[1] : "server";
            var fullProject = Path.GetFullPath(Path.IsPathRooted(project) ? project : Path.Combine(root, project));
            if (!Directory.Exists(fullProject) && !File.Exists(fullProject))
            {
                Console.Error.WriteLine($"{{\"error\":\"project path not found: {project}\"}}");
                return 2;
            }
            if (fixtureMode && !Directory.Exists(fullProject))
            {
                Console.Error.WriteLine($"{{\"error\":\"fixture directory not found: {project}\"}}");
                return 2;
            }

            var tempRoot = Path.Combine(Path.GetTempPath(), "lfwd-build-verify", Guid.NewGuid().ToString("N"));
            var projectDir = File.Exists(fullProject)
                ? Path.GetDirectoryName(fullProject) ?? root
                : fullProject;
            var projectFileName = File.Exists(fullProject)
                ? Path.GetFileName(fullProject)
                : Directory.EnumerateFiles(projectDir, "*.csproj").Select(Path.GetFileName).FirstOrDefault()
                    ?? throw new InvalidOperationException("csproj not found");
            var tempProjectDir = Path.Combine(tempRoot, "source");
            var outputDir = Path.Combine(tempRoot, "bin");
            CopyDirectory(projectDir, tempProjectDir);
            Directory.CreateDirectory(outputDir);

            var result = RunProcess("dotnet",
                $"build \"{Path.Combine(tempProjectDir, projectFileName)}\" -c Release -o \"{outputDir}\" " +
                "/p:UseRazorBuildServer=false /p:UseSharedCompilation=false",
                root);
            var combined = result.Stdout + "\n" + result.Stderr;
            var locked = result.ExitCode != 0 && LooksLocked(combined);
            var verdict = result.ExitCode == 0 ? "PASS" : locked ? "LOCKED" : "CODE-ERROR";

            var report = new JsonObject
            {
                ["harness"] = "build-verify",
                ["fixtureMode"] = fixtureMode,
                ["project"] = Path.GetRelativePath(root, fullProject).Replace('\\', '/'),
                ["buildProject"] = Path.Combine(tempProjectDir, projectFileName),
                ["configuration"] = "Release",
                ["outputDir"] = outputDir,
                ["sourceCopied"] = true,
                ["exitCode"] = result.ExitCode,
                ["verdict"] = verdict,
                ["locked"] = locked,
                ["stdoutTail"] = Tail(result.Stdout, 1200),
                ["stderrTail"] = Tail(result.Stderr, 1200),
                ["note"] = "PASS/FAIL is decided only by dotnet build exit code. Text is diagnostic context.",
            };

            Console.WriteLine(report.ToJsonString(HarnessJson.Options));
            return result.ExitCode == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{{\"error\":\"build-verify failed: {ex.Message}\"}}");
            return 2;
        }
    }

    // 자식 프로세스를 실행하고 종료 코드와 표준출력·표준오류를 수집한다.
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

    // 잠기거나 낡을 수 있는 빌드 산출물은 제외하고 프로젝트 소스를 임시 경로에 복사한다.
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

    // 성공·실패 판정에는 쓰지 않고 흔한 파일 잠금 오류만 분류한다.
    private static bool LooksLocked(string text)
        => text.Contains("being used by another process", StringComparison.OrdinalIgnoreCase)
            || text.Contains("cannot access the file", StringComparison.OrdinalIgnoreCase)
            || text.Contains("used by another process", StringComparison.OrdinalIgnoreCase)
            || text.Contains("MSB3021", StringComparison.OrdinalIgnoreCase)
            || text.Contains("MSB3027", StringComparison.OrdinalIgnoreCase);

    // 긴 명령 출력을 진단용 끝부분만 남겨 줄인다.
    private static string Tail(string text, int maxChars)
    {
        var normalized = text.Replace("\r\n", "\n").Trim();
        return normalized.Length <= maxChars ? normalized : normalized[^maxChars..];
    }
}
