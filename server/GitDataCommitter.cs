// 운영 데이터 파일 변경을 git 커밋으로 남긴다.
// dashboard/data 아래 변경만 스테이징하고 실패는 콘솔 로그로 남긴다.
using System.Diagnostics;

public sealed record GitDataCommitOptions(string WorkspaceRoot, bool AutoCommitData, bool AutoPush);

public static class GitDataCommitter
{
    // 사람 액션으로 변경된 운영 데이터를 커밋하고 설정에 따라 push한다.
    // [loop] 접두사는 루프 이터레이션을 나타내며 주체(actor)를 뜻하지 않는다 — 주체는 actor 필드로 구분한다.
    public static void CommitHumanAction(GitDataCommitOptions options, string projectId, int loopIteration, string action, string? proposalId, string actor = "unknown")
    {
        if (!options.AutoCommitData)
        {
            return;
        }

        try
        {
            var status = RunGit(options.WorkspaceRoot, "status", "--porcelain", "--", "dashboard/data");

            if (string.IsNullOrWhiteSpace(status.Output))
            {
                return;
            }

            RunGit(options.WorkspaceRoot, "add", "-A", "--", "dashboard/data");

            var message = $"[loop] {projectId} 회차{loopIteration}: {action} {proposalId ?? "none"} (actor: {actor})";
            var commit = RunGit(options.WorkspaceRoot, "commit", "-m", message);

            if (commit.ExitCode != 0)
            {
                Console.Error.WriteLine($"[git-data] commit failed: {commit.Error}{commit.Output}");
                return;
            }

            if (!options.AutoPush)
            {
                return;
            }

            var push = RunGit(options.WorkspaceRoot, "push");

            if (push.ExitCode != 0)
            {
                Console.Error.WriteLine($"[git-data] push failed: {push.Error}{push.Output}");
            }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"[git-data] skipped: {error.Message}");
        }
    }

    // git 명령을 실행하고 표준 출력·오류를 수집한다.
    private static GitCommandResult RunGit(string workingDirectory, params string[] arguments)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new GitCommandResult(process.ExitCode, output, error);
    }
}

public sealed record GitCommandResult(int ExitCode, string Output, string Error);
