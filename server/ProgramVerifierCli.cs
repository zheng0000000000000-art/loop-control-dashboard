// Program Verifier — 실행자가 낸 증거를 믿지 않고 게이트 검사를 직접 다시 돌려 판정한다.
// CODEX-HARNESS-LAUNCHER-minimal-contract §2-6의 결속 상대다. Launcher는 transport와 기록만 하고,
// 판정은 여기서 한다. 통과해도 canonical state는 건드리지 않는다 — transition request 후보만 낸다.
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class ProgramVerifierCli
{
    private const string ManifestRel = "docs/handoff/GATE-MANIFEST.json";
    private const string RequestDirRel = "outputs/transition-requests";
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // 검사 하나의 선언값이다. 목록은 GATE-MANIFEST.json에서만 온다 — 코드가 지어내지 않는다.
    private record CheckSpec(int Order, string Command, string[] Args, int ExpectedExit, bool MutatesState);

    // 검사 하나를 실제로 돌린 결과다. 판정 근거는 exitCode 하나뿐이다.
    private record CheckRun(CheckSpec Spec, int ActualExit, long DurationMs, string StdoutSha256, string StderrTail);

    // 명령을 분기한다. verify는 판정만, request는 통과한 판정에서 transition request 후보를 만든다.
    internal static int Run(string[] args)
    {
        var sub = args.Length > 1 ? args[1] : "";
        if (string.Equals(sub, "verify", StringComparison.OrdinalIgnoreCase)) return Verify(args, emitRequest: false);
        if (string.Equals(sub, "request", StringComparison.OrdinalIgnoreCase)) return Verify(args, emitRequest: true);
        Console.Error.WriteLine("{\"error\":\"사용법: program-verify verify|request --gate <gateId> [--launch <launchId>]\"}");
        return 2;
    }

    // 게이트의 검사를 순서대로 직접 재실행하고 exit code로만 판정한다. 하나라도 어긋나면 FAIL이다.
    private static int Verify(string[] args, bool emitRequest)
    {
        var gateId = ReadOption(args, "--gate");
        var launchId = ReadOption(args, "--launch");
        if (string.IsNullOrWhiteSpace(gateId))
        {
            Console.Error.WriteLine("{\"error\":\"--gate <gateId>가 필요하다.\"}");
            return 2;
        }

        var root = RepoRoot();
        List<CheckSpec> specs;
        try
        {
            specs = ReadGate(root, gateId);
        }
        catch (Exception ex)
        {
            // 게이트를 못 읽으면 통과가 아니라 실패다. 모르는 것을 PASS로 적지 않는다.
            Console.Error.WriteLine($"{{\"error\":\"게이트를 읽지 못했다: {Escape(ex.Message)}\"}}");
            return 2;
        }

        // 측정을 시작하는 시점의 트리 상태를 먼저 잡는다. 검사 중 measure가 산출물을 바꾸므로
        // 끝난 뒤에 재면 언제나 더럽다. 소비하는 쪽은 "무엇을 쟀는가"를 알아야 한다 —
        // 더러운 트리에서 잰 판정에 깨끗한 커밋 해시만 붙이면 그 커밋을 쟀다는 거짓이 된다.
        var cleanAtStart = WorktreeClean(root);
        var runs = new List<CheckRun>();
        foreach (var spec in specs)
        {
            runs.Add(RunCheck(root, spec));
        }

        var failed = runs.Where(r => r.ActualExit != r.Spec.ExpectedExit).ToList();
        var passed = failed.Count == 0 && runs.Count > 0;
        var report = BuildReport(gateId, launchId, runs, passed, cleanAtStart);

        string? requestPath = null;
        if (passed && emitRequest) requestPath = WriteRequest(root, gateId, launchId, report);
        report["transitionRequestPath"] = requestPath;

        Console.WriteLine(report.ToJsonString(JsonOptions));
        return passed ? 0 : 1;
    }

    // 매니페스트에서 게이트 하나의 검사 목록을 읽는다. 없는 게이트는 예외로 올려 fail-closed 시킨다.
    private static List<CheckSpec> ReadGate(string root, string gateId)
    {
        var path = Path.Combine(root, ManifestRel);
        if (!File.Exists(path)) throw new FileNotFoundException($"{ManifestRel}가 없다.");
        var manifest = JsonNode.Parse(File.ReadAllText(path, Utf8NoBom))?.AsObject()
            ?? throw new InvalidOperationException("GATE-MANIFEST.json을 파싱하지 못했다.");
        var gates = manifest["gates"]?.AsArray() ?? throw new InvalidOperationException("gates 배열이 없다.");
        var gate = gates.FirstOrDefault(g =>
            string.Equals(g?["gateId"]?.GetValue<string>(), gateId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"gateId '{gateId}'가 매니페스트에 없다.");
        var checks = gate["checks"]?.AsArray() ?? throw new InvalidOperationException("checks 배열이 없다.");

        var specs = new List<CheckSpec>();
        foreach (var check in checks)
        {
            var command = check?["command"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(command)) throw new InvalidOperationException("command가 빈 검사가 있다.");
            var argsNode = check?["args"]?.AsArray();
            var checkArgs = argsNode?.Select(a => a?.GetValue<string>() ?? "").ToArray() ?? [];
            // expectedExit이 선언되지 않았으면 0으로 가정하지 않는다 — 기대값 없는 검사는 판정할 수 없다.
            var expected = check?["expectedExit"]?.GetValue<int>()
                ?? throw new InvalidOperationException($"'{command}'에 expectedExit이 없다.");
            specs.Add(new CheckSpec(
                check?["order"]?.GetValue<int>() ?? specs.Count + 1,
                command,
                checkArgs,
                expected,
                check?["mutatesState"]?.GetValue<bool>() ?? false));
        }
        if (specs.Count == 0) throw new InvalidOperationException($"gateId '{gateId}'에 검사가 하나도 없다.");
        return specs.OrderBy(s => s.Order).ToList();
    }

    // 검사 하나를 자식 프로세스로 돌린다. 출력 문자열로 성패를 세지 않고 exit code만 판정에 쓴다.
    private static CheckRun RunCheck(string root, CheckSpec spec)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in new[] { "run", "--project", "server", "--", spec.Command }) psi.ArgumentList.Add(a);
        foreach (var a in spec.Args) psi.ArgumentList.Add(a);

        var watch = Stopwatch.StartNew();
        try
        {
            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("프로세스를 시작하지 못했다.");
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            watch.Stop();
            return new CheckRun(spec, process.ExitCode, watch.ElapsedMilliseconds, Sha256(stdout), Tail(stderr));
        }
        catch (Exception ex)
        {
            watch.Stop();
            // 실행 자체가 안 된 것은 "종료 코드 없음"이지 통과가 아니다. 기대값과 다른 값을 넣어 FAIL로 만든다.
            var missing = spec.ExpectedExit == int.MinValue ? int.MaxValue : int.MinValue;
            return new CheckRun(spec, missing, watch.ElapsedMilliseconds, Sha256(""), Tail(ex.Message));
        }
    }

    // 판정 결과를 기계가 읽는 모양으로 만든다. 검사별 기대값과 실측값을 나란히 남긴다.
    private static JsonObject BuildReport(string gateId, string? launchId, List<CheckRun> runs, bool passed, bool cleanAtStart)
    {
        var checks = new JsonArray();
        foreach (var run in runs)
        {
            checks.Add(new JsonObject
            {
                ["order"] = run.Spec.Order,
                ["command"] = run.Spec.Command,
                ["args"] = new JsonArray(run.Spec.Args.Select(a => (JsonNode)a!).ToArray()),
                ["expectedExit"] = run.Spec.ExpectedExit,
                ["actualExit"] = run.ActualExit,
                ["passed"] = run.ActualExit == run.Spec.ExpectedExit,
                ["mutatesState"] = run.Spec.MutatesState,
                ["durationMs"] = run.DurationMs,
                ["stdoutSha256"] = run.StdoutSha256,
                ["stderrTail"] = run.StderrTail,
            });
        }
        return new JsonObject
        {
            ["verifier"] = "program-verify",
            ["schemaVersion"] = 1,
            ["gateId"] = gateId,
            ["launchId"] = launchId,
            // 어느 커밋에서 잰 판정인지 박는다. 이것이 없으면 예전에 통과한 보고서를 나중에
            // 다시 들이밀 수 있다 — 소비하는 쪽이 HEAD와 대조해 거절할 수 있어야 한다.
            ["baselineCommit"] = HeadCommit(),
            ["worktreeCleanAtStart"] = cleanAtStart,
            ["verdict"] = passed ? "PASS" : "FAIL",
            ["checkCount"] = runs.Count,
            ["failedCount"] = runs.Count(r => r.ActualExit != r.Spec.ExpectedExit),
            ["verifiedAt"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["checks"] = checks,
        };
    }

    // 통과한 판정에서 transition request 후보를 파일로 낸다. WORKSTATE는 건드리지 않는다 —
    // canonical 변경은 사람 결재와 StateApplier 전이를 거쳐야 한다(계약 §4).
    private static string WriteRequest(string root, string gateId, string? launchId, JsonObject report)
    {
        var dir = Path.Combine(root, RequestDirRel);
        Directory.CreateDirectory(dir);
        var stamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");
        var name = $"TR-{gateId}-{stamp}.json";
        var request = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["kind"] = "TRANSITION_REQUEST",
            ["status"] = "AWAITING_HUMAN_APPROVAL",
            ["gateId"] = gateId,
            ["launchId"] = launchId,
            ["createdAt"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["createdBy"] = "program-verify",
            // 요청이 어떤 판정에서 나왔는지 묶는다. 판정 없이 만들어진 요청과 구분된다.
            ["evidence"] = report.DeepClone(),
            ["note"] = "이 파일은 요청이지 전이가 아니다. canonical state는 사람 결재 + state-transition으로만 바뀐다.",
        };
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, request.ToJsonString(JsonOptions), Utf8NoBom);
        return Path.Combine(RequestDirRel, name).Replace('\\', '/');
    }

    // 측정 시작 시점에 워크트리가 커밋과 일치했는지 본다. 판정할 수 없으면 false다 —
    // 모르는 것을 "깨끗했다"로 적지 않는다.
    private static bool WorktreeClean(string root)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git", WorkingDirectory = root,
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            };
            psi.ArgumentList.Add("status");
            psi.ArgumentList.Add("--porcelain");
            using var process = Process.Start(psi);
            if (process is null) return false;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 && string.IsNullOrWhiteSpace(output);
        }
        catch { return false; }
    }

    // 판정 시점의 HEAD 커밋을 읽는다. 못 읽으면 빈 문자열이지 "알 수 없음"을 감추지 않는다.
    private static string HeadCommit()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = RepoRoot(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("rev-parse");
            psi.ArgumentList.Add("HEAD");
            using var process = Process.Start(psi);
            if (process is null) return "";
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            return process.ExitCode == 0 ? output : "";
        }
        catch { return ""; }
    }

    // --이름 다음 값을 읽는다. 없으면 null이다.
    private static string? ReadOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        }
        return null;
    }

    // 저장소 루트를 찾는다. 실행 위치가 어디든 같은 기준으로 검사를 돌리기 위함이다.
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git"))) return dir.FullName;
            dir = dir.Parent;
        }
        return Directory.GetCurrentDirectory();
    }

    // 출력 본문을 해시로 남긴다. 본문을 판정에 쓰지 않되 나중에 대조할 수는 있게 한다.
    private static string Sha256(string text)
    {
        var bytes = SHA256.HashData(Utf8NoBom.GetBytes(text ?? ""));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    // stderr는 원인 파악용으로 꼬리만 남긴다. 판정에는 쓰지 않는다.
    private static string Tail(string text)
    {
        var t = (text ?? "").Trim();
        return t.Length <= 500 ? t : t[^500..];
    }

    // JSON 문자열에 그대로 넣을 수 있게 최소 이스케이프만 한다.
    private static string Escape(string text) => (text ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
}
