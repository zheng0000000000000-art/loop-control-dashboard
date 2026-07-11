// Build verification harness.
// Uses dotnet build exit code as the source of truth and writes outputs to a temp directory to avoid locked apphost files.
using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class BuildVerifyCli
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // build-verify entry. exit 0=build passed, 1=build failed, 2=harness error.
    internal static int Run(string[] args)
    {
        try
        {
            var root = GitTools.FindRepoRoot();
            var project = args.Length >= 2 ? args[1] : "server";
            var fullProject = Path.GetFullPath(Path.IsPathRooted(project) ? project : Path.Combine(root, project));
            if (!Directory.Exists(fullProject) && !File.Exists(fullProject))
            {
                Console.Error.WriteLine($"{{\"error\":\"project path not found: {project}\"}}");
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

            Console.WriteLine(report.ToJsonString(JsonOptions));
            return result.ExitCode == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{{\"error\":\"build-verify failed: {ex.Message}\"}}");
            return 2;
        }
    }

    // Runs a child process and captures exit code plus stdout/stderr.
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

    // Copies project sources to temp while skipping build artifacts that can be locked or stale.
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

    // Classifies common file-lock build failures without using it as the pass/fail source.
    private static bool LooksLocked(string text)
        => text.Contains("being used by another process", StringComparison.OrdinalIgnoreCase)
            || text.Contains("cannot access the file", StringComparison.OrdinalIgnoreCase)
            || text.Contains("used by another process", StringComparison.OrdinalIgnoreCase)
            || text.Contains("MSB3021", StringComparison.OrdinalIgnoreCase)
            || text.Contains("MSB3027", StringComparison.OrdinalIgnoreCase);

    // Trims long command output while preserving the diagnostic tail.
    private static string Tail(string text, int maxChars)
    {
        var normalized = text.Replace("\r\n", "\n").Trim();
        return normalized.Length <= maxChars ? normalized : normalized[^maxChars..];
    }
}
