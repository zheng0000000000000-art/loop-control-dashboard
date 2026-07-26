// HEAD 커밋의 코덱스 영토 변경이 outbox 반입 또는 사람 면제로 설명되는지 검사한다.
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class TerritoryCheckCli
{
    private const string DefaultLedger = "docs/handoff/TERRITORY-EXCEPTIONS.json";

    // territory-check 진입점. exit 0=위반 없음, 1=영토 위반 또는 낡은 면제, 2=입력 오류.
    internal static int Run(string[] args)
    {
        try
        {
            var optionFailure = CliOptions.Validate(args, 1, ["commit", "ledger"], []);
            if (optionFailure is not null) return Error(optionFailure);

            var root = GitTools.FindRepoRoot();
            var requestedCommit = Option(args, "commit") ?? "HEAD";
            var ledgerOption = Option(args, "ledger");
            var ledgerPath = ledgerOption ?? DefaultLedger;
            var commit = Git(root, "rev-parse", "--verify", requestedCommit + "^{commit}").Trim();
            var territoryPaths = ChangedPaths(root, commit)
                .Where(CodexTerritory.Contains)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var exceptions = LoadExceptions(root, ledgerPath, ledgerOption is null);
            var staleExceptions = exceptions
                .Where(item => !CommitExists(root, item.Sha))
                .Select(item => item.Sha)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var patches = LoadTrackedPatches(root);
            var coveredByOutbox = territoryPaths
                .Where(path => patches.Any(patch => PatchContainsPath(patch, path)))
                .ToArray();
            var isExempt = exceptions.Any(item =>
                string.Equals(item.Sha, commit, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(item.Reason));
            var exempted = isExempt
                ? territoryPaths.Except(coveredByOutbox, StringComparer.OrdinalIgnoreCase).ToArray()
                : [];
            var violationPaths = territoryPaths
                .Except(coveredByOutbox, StringComparer.OrdinalIgnoreCase)
                .Except(exempted, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            Console.WriteLine(new JsonObject
            {
                ["harness"] = "territory-check",
                ["commit"] = commit,
                ["territoryPaths"] = JsonArray(territoryPaths),
                ["coveredByOutbox"] = JsonArray(coveredByOutbox),
                ["exempted"] = JsonArray(exempted),
                ["staleExceptions"] = JsonArray(staleExceptions),
                ["violations"] = violationPaths.Length + staleExceptions.Length,
                ["violationPaths"] = JsonArray(violationPaths),
            }.ToJsonString());
            return violationPaths.Length == 0 && staleExceptions.Length == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    // 이름 있는 옵션의 값을 읽는다.
    private static string? Option(string[] args, string name)
    {
        var flag = "--" + name;
        for (var i = 1; i + 1 < args.Length; i++)
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }

    // 커밋이 바꾼 저장소 상대 경로를 읽는다.
    private static string[] ChangedPaths(string root, string commit) =>
        Git(root, "diff-tree", "--no-commit-id", "--name-only", "-r", commit)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(path => path.Replace('\\', '/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    // 추적 중인 candidate.patch 내용을 읽는다.
    private static string[] LoadTrackedPatches(string root) =>
        Git(root, "ls-files", "outbox/*/candidate.patch")
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(path => File.ReadAllText(Path.Combine(root, path)))
            .ToArray();

    // unified diff의 파일 헤더가 대상 경로를 가리키는지 확인한다.
    private static bool PatchContainsPath(string patch, string path)
    {
        var expectedA = "--- a/" + path;
        var expectedB = "+++ b/" + path;
        return patch.Split('\n').Any(line =>
            string.Equals(line.TrimEnd('\r'), expectedA, StringComparison.Ordinal)
            || string.Equals(line.TrimEnd('\r'), expectedB, StringComparison.Ordinal));
    }

    // ledger 면제를 읽으며 반입 전 기본 ledger 부재는 빈 목록으로 취급한다.
    private static TerritoryException[] LoadExceptions(string root, string relativePath, bool isDefault)
    {
        var path = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!File.Exists(path))
        {
            if (isDefault) return [];
            throw new InvalidOperationException("ledger-not-found");
        }

        var array = JsonNode.Parse(File.ReadAllText(path))?["exceptions"]?.AsArray()
            ?? throw new InvalidOperationException("ledger-exceptions-required");
        return array.Select(node => new TerritoryException(
                node?["sha"]?.GetValue<string>() ?? "",
                node?["reason"]?.GetValue<string>() ?? ""))
            .Where(item => item.Sha.Length > 0)
            .ToArray();
    }

    // sha가 저장소의 커밋인지 확인한다.
    private static bool CommitExists(string root, string sha)
    {
        try
        {
            Git(root, "cat-file", "-e", sha + "^{commit}");
            return true;
        }
        catch
        {
            return false;
        }
    }

    // git을 인자 배열로 실행하고 실패를 입력 오류로 올린다.
    private static string Git(string root, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("git-start-failed");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(stderr.Trim().Length > 0 ? stderr.Trim() : "git-command-failed");
        return stdout;
    }

    // 문자열 배열을 JSON 배열로 바꾼다.
    private static JsonArray JsonArray(IEnumerable<string> values) =>
        new(values.Select(value => (JsonNode?)value).ToArray());

    // 입력 오류를 JSON으로 출력한다.
    private static int Error(string message)
    {
        Console.Error.WriteLine(new JsonObject { ["error"] = message }.ToJsonString());
        return 2;
    }

    private sealed record TerritoryException(string Sha, string Reason);
}
