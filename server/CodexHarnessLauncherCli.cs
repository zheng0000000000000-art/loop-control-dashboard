// CodexHarnessLauncher — Codex를 하네스·fixture 제작 통로로만 연결한다.
// CODEX-HARNESS-LAUNCHER-minimal-contract 구현. Launcher는 transport와 기록만 한다:
// 역할 선택·성공 판정·상태 전이·fallback은 여기 없다. 판정은 ProgramVerifierCli가 독립 실행한다.
// canonical state는 건드리지 않으며 산출물은 outbox candidate로만 남는다(계약 §4).
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class CodexHarnessLauncherCli
{
    private const string TrustOriginDirRel = "docs/handoff/trust-origins";
    private const string OutboxDirRel = "outbox";
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // 쓰기 허용 범위의 정본은 CodexTerritory다(사본을 두지 마라). 그 선을 넘히려면 사람 결재다.
    // skills/는 2026-07-26 사람 결재로 들어왔다(BASELINE-CHANGES 참조). 계약 §2-2는 런처를
    // 하네스·fixture 제작으로 좁혀 썼는데 ADR-002는 skills/도 코덱스 배타로 준다 — 절차 문서를
    // 코덱스에게 맡기려면 그 간극을 메워야 했고, ADR-002의 선까지만 맞췄다.

    // 계약 §3: 요청이 스스로 금지해야 하는 행위. 하나라도 빠지면 요청이 불완전한 것이다.
    private static readonly string[] RequiredForbiddenActions =
        ["commit", "push", "state-transition", "pass", "product-code-edit"];

    // 명령을 분기한다. validate는 검사만, launch는 검사 후 실행하고 증거를 남긴다.
    internal static int Run(string[] args)
    {
        var sub = args.Length > 1 ? args[1] : "";
        if (string.Equals(sub, "validate", StringComparison.OrdinalIgnoreCase)) return Execute(args, launch: false);
        if (string.Equals(sub, "launch", StringComparison.OrdinalIgnoreCase)) return Execute(args, launch: true);
        return Error("usage: codex-launch validate|launch --request <file> [--manual]", 2);
    }

    // 요청을 검사하고, launch면 격리 사본에서 실행한 뒤 증거를 기록한다.
    private static int Execute(string[] args, bool launch)
    {
        // --manual은 안전장치다. 오타가 조용히 무시되면 그 가드가 없는 것과 같아진다.
        var optionFailure = CliOptions.Validate(args, 2, ["request"], ["manual"]);
        if (optionFailure is not null) return Error(optionFailure, 2);

        var requestPath = Flag(args, "request");
        if (string.IsNullOrWhiteSpace(requestPath)) return Error("launch-request-required", 2);
        var root = RepoRoot();

        JsonObject? request;
        try { request = JsonNode.Parse(File.ReadAllText(Path.GetFullPath(requestPath), Utf8NoBom))?.AsObject(); }
        catch { return Error("launch-request-unparsable", 2); }
        if (request is null) return Error("launch-request-unparsable", 2);

        var rejection = RequestRejection(root, request);
        if (rejection is not null) return Error(rejection, 2);

        // 계약 §83: 실제 자동 발사는 AUTOMATED_EXECUTION_READY에서만 허용된다. 그 전에는
        // 수동 dispatch만이다. 사람이 --manual로 명시하지 않으면 쏘지 않는다.
        var guardRejection = LaunchGuardRejection(args, root);
        if (launch && guardRejection is not null) return Error(guardRejection, 2);

        if (!launch)
        {
            Output(new JsonObject
            {
                ["command"] = "codex-launch validate",
                ["launchId"] = Str(request, "launchId"),
                ["verdict"] = "ACCEPTED",
                // 같은 인자로 launch하면 가드가 막는지를 여기서 답한다. 이것이 없으면 가드를 재려고
                // 실제 발사를 걸어야 했다(2026-07-26: PATH에서 codex를 빼는 우회로 겨우 쟀다).
                // 재려면 위험을 감수해야 하는 안전장치는 안전장치가 아니다.
                ["wouldLaunch"] = guardRejection is null,
                ["launchBlockedBy"] = guardRejection,
                ["manualFlag"] = HasFlag(args, "manual"),
                ["automatedExecutionReady"] = AutomatedExecutionReady(root),
                ["note"] = "요청이 계약을 만족한다. 실행은 launch가 한다 — 이 명령은 아무것도 실행하지 않았다. "
                    + "wouldLaunch는 같은 인자로 launch했을 때 가드가 통과시키는지를 뜻하며, 요청의 유효성과는 별개다.",
            });
            return 0;
        }
        return Launch(root, request);
    }

    // 지금 이 인자로 launch하면 가드가 막는지 본다. 막으면 사유, 아니면 null.
    // launch와 validate가 같은 함수를 쓰게 해서 "보고한 것과 실제가 다른" 경우를 없앤다.
    private static string? LaunchGuardRejection(string[] args, string root)
        => !HasFlag(args, "manual") && !AutomatedExecutionReady(root)
            ? "automated-execution-not-ready"
            : null;

    // 요청이 계약을 만족하는지 본다. 만족하면 null, 아니면 거절 사유다 — 모르는 것은 통과가 아니다.
    private static string? RequestRejection(string root, JsonObject request)
    {
        if (Str(request, "schemaVersion") != "1") return "launch-request-schema-unsupported";
        // 계약 §2-1: 역할이 다르면 즉시 거부한다. Launcher는 역할을 고르지 않는다(§3).
        if (Str(request, "actorRole") != "HARNESS_EXECUTOR") return "actor-role-not-harness-executor";
        if (Str(request, "transport") != "codex-cli") return "transport-unsupported";
        if (string.IsNullOrWhiteSpace(Str(request, "launchId"))) return "launch-id-required";
        if (string.IsNullOrWhiteSpace(Str(request, "diId"))) return "di-id-required";

        // 낡은 요청을 나중에 다시 쏘는 것을 막는다.
        if (Str(request, "baselineCommit") != Git(root, "rev-parse", "HEAD").Trim()) return "baseline-commit-mismatch";

        // 계약 §7: 지시서와 컨텍스트 팩은 해시로 묶인다(ADR-010 계열 transport 무결성).
        var directiveRejection = PinnedFileRejection(root, request, "directivePath", "directiveSha256", "directive");
        if (directiveRejection is not null) return directiveRejection;
        var packRejection = PinnedFileRejection(root, request, "contextPackPath", "contextPackSha256", "context-pack");
        if (packRejection is not null) return packRejection;

        // 계약 §2-2: 쓰기 범위가 코덱스 영역 밖이면 요청 자체를 거절한다. 실행 후 잡는 것이 아니라
        // 쏘기 전에 막는다 — 잘못 쏜 뒤의 scope 위반은 이미 사본을 더럽힌 뒤다.
        var allowed = Array(request, "allowedPaths");
        if (allowed.Count == 0) return "allowed-paths-required";
        foreach (var path in allowed)
        {
            if (!CodexTerritory.Contains(path)) return "allowed-paths-outside-codex-territory";
        }

        var forbidden = Array(request, "forbiddenActions");
        foreach (var required in RequiredForbiddenActions)
        {
            if (!forbidden.Contains(required, StringComparer.OrdinalIgnoreCase)) return "forbidden-actions-incomplete";
        }

        // 계약 §7: credentialRef는 참조만 담는다. 값처럼 보이면 거절한다 — 자격 증명이 요청 파일에
        // 실려 저장소에 들어가는 것을 구조적으로 막는다.
        var credential = Str(request, "credentialRef");
        if (credential.Length > 64 || credential.Contains(' ')) return "credential-ref-looks-like-a-secret";

        var timeout = Int(request, "timeoutSeconds");
        if (timeout <= 0 || timeout > 3600) return "timeout-out-of-range";
        var attempt = Int(request, "attempt");
        var maxAttempts = Int(request, "maxAttempts");
        if (attempt < 1 || maxAttempts < 1 || attempt > maxAttempts) return "attempt-out-of-range";
        if (Str(request, "workingCopyMode") != "isolated-clean") return "working-copy-mode-unsupported";
        return null;
    }

    // 격리 사본에 복원 산출물(server/obj)을 넣어준다. 없으면 실행자가 빌드를 증명할 수 없다.
    //
    // 2026-07-27 실측: `codex exec --sandbox workspace-write`는 **네트워크가 없다.** 그런데
    // worktree는 gitignore된 `server/obj`를 안 가져오므로 `--no-restore`는 NETSDK1004
    // (project.assets.json 부재), 복원은 NU1301(원본 로드 실패)로 죽는다. 두 발사(NET8-01·R1)가
    // 여기서 막혔고, 첫 번째는 **컴파일되지 않는 산출물**을 냈다.
    //
    // obj는 gitignore라 사본의 `git add -A`에 잡히지 않는다 — scope 판정에 영향이 없다.
    // 복사가 실패해도 발사를 막지 않는다. 빌드 실패는 실행자가 보고할 몫이지 런처가 판정할 것이 아니다.
    private static void SeedRestoreOutput(string root, string workdir)
    {
        try
        {
            var source = new DirectoryInfo(Path.Combine(root, "server", "obj"));
            if (!source.Exists) return;
            CopyDirectory(source, new DirectoryInfo(Path.Combine(workdir, "server", "obj")));
        }
        catch
        {
            // 무시한다. 실행자가 빌드 불가를 보고하면 그것이 증거다.
        }
    }

    // 디렉터리를 통째로 복사한다.
    private static void CopyDirectory(DirectoryInfo source, DirectoryInfo target)
    {
        Directory.CreateDirectory(target.FullName);
        foreach (var file in source.GetFiles())
            file.CopyTo(Path.Combine(target.FullName, file.Name), overwrite: true);
        foreach (var dir in source.GetDirectories())
            CopyDirectory(dir, new DirectoryInfo(Path.Combine(target.FullName, dir.Name)));
    }

    // 경로가 있고 해시가 현재 내용과 맞는지 본다. 어긋나면 낡은 참조로 쏘는 것이다.
    private static string? PinnedFileRejection(string root, JsonObject request, string pathKey, string hashKey, string label)
    {
        var rel = Str(request, pathKey);
        if (string.IsNullOrWhiteSpace(rel)) return $"{label}-path-required";
        var full = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(full)) return $"{label}-missing";
        if (Sha256(File.ReadAllBytes(full)) != Str(request, hashKey)) return $"{label}-sha256-mismatch";
        return null;
    }

    // 격리된 clean 사본에서 codex를 돌리고, 그 결과를 증거로만 기록한다. 판정하지 않는다.
    private static int Launch(string root, JsonObject request)
    {
        var launchId = Str(request, "launchId");
        var baseline = Str(request, "baselineCommit");
        var workdir = Path.Combine(Path.GetTempPath(), "codex-launch-" + Guid.NewGuid().ToString("N"));
        var started = DateTime.UtcNow;
        try
        {
            // 계약 §2-3: 임시 사본에서 실행한다. worktree로 baseline 커밋을 그대로 떼어낸다.
            var add = RunProcess(root, "git", ["worktree", "add", "--detach", workdir, baseline], 120);
            if (add.ExitCode != 0) return Error("isolated-copy-failed", 2);

            SeedRestoreOutput(root, workdir);

            var prompt = BuildPrompt(root, request);
            var timeout = Int(request, "timeoutSeconds");
            var codex = ResolveExecutable("codex");
            if (codex is null) return Error("codex-executable-not-found", 2);
            var run = RunProcess(workdir, codex, ["exec", "--sandbox", "workspace-write", "-"], timeout, prompt);

            // 계약 §2-2: 사본에서 실제로 바뀐 경로가 allowedPaths 안인지 대조한다.
            var changed = ChangedPaths(workdir);
            var allowed = Array(request, "allowedPaths");
            var violations = changed.Where(p => !allowed.Any(a => Matches(p, a))).ToList();

            // `git diff`만 쓰면 untracked 새 파일이 패치에서 빠진다. changedPaths는 status로
            // 세므로 3개라고 보고하면서 패치에는 1개만 담기는 일이 실제로 났다(GWIT-01, 2026-07-26):
            // 새 클래스가 빠진 채 그것을 참조하는 수정만 실려 빌드가 깨졌다. 전부 stage한 뒤 낸다.
            RunProcess(workdir, "git", ["add", "-A"], 120);
            var patch = RunProcess(workdir, "git", ["--no-pager", "diff", "--cached"], 120).Stdout;
            var evidence = new JsonObject
            {
                ["schemaVersion"] = 1,
                ["kind"] = "CODEX_LAUNCH_EVIDENCE",
                ["launchId"] = launchId,
                ["diId"] = Str(request, "diId"),
                ["baselineCommit"] = baseline,
                ["startedAt"] = started.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                ["finishedAt"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                // 계약 §2-4: stdout/stderr/exit code/산출물 해시를 증거로 남긴다.
                ["exitCode"] = run.ExitCode,
                ["timedOut"] = run.TimedOut,
                ["stdoutSha256"] = Sha256(Utf8NoBom.GetBytes(run.Stdout)),
                ["stderrTail"] = Tail(run.Stderr),
                ["changedPaths"] = new JsonArray(changed.Select(p => (JsonNode)p!).ToArray()),
                ["scopeViolations"] = new JsonArray(violations.Select(p => (JsonNode)p!).ToArray()),
                ["candidatePatchSha256"] = Sha256(Utf8NoBom.GetBytes(patch)),
                // 계약 §2-5·§3: Launcher는 여기까지다. 판정은 Program Verifier가 독립 실행한다.
                ["verdict"] = "NOT_JUDGED_BY_LAUNCHER",
                ["note"] = "이것은 증거이지 판정이 아니다. 산출물은 outbox candidate이며 반입은 사람이 한다.",
            };

            // 증거와 산출물이 어긋나면 실패다. changedPaths에 있는데 패치에 없는 경로가 있으면
            // 반입하는 쪽이 조용히 반쪽짜리를 적용하게 된다 — 위 사고가 정확히 그것이었다.
            var missingFromPatch = changed
                .Where(p => !patch.Contains("/" + p, StringComparison.Ordinal)
                    && !patch.Contains(" " + p, StringComparison.Ordinal))
                .ToList();
            evidence["pathsMissingFromPatch"] = new JsonArray(missingFromPatch.Select(p => (JsonNode)p!).ToArray());

            var outDir = Path.Combine(root, OutboxDirRel, "codex-launch-" + launchId);
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "candidate.patch"), patch, Utf8NoBom);
            File.WriteAllText(Path.Combine(outDir, "execution-report.json"), evidence.ToJsonString(JsonOptions), Utf8NoBom);
            WritePendingDisposition(outDir, launchId, started);

            evidence["outboxDir"] = Path.GetRelativePath(root, outDir).Replace('\\', '/');
            Output(evidence);
            // 범위를 벗어난 변경이 있으면 실행 자체가 실패다. 증거는 남기되 성공으로 적지 않는다.
            return violations.Count > 0 || missingFromPatch.Count > 0 ? 1 : run.ExitCode == 0 ? 0 : 1;
        }
        finally
        {
            RunProcess(root, "git", ["worktree", "remove", "--force", workdir], 120);
        }
    }

    // 처분을 아직 안 정했다는 사실을 발사 시점에 남긴다. 기록이 없는 것과 안 정한 것은 다른 상태다.
    // 이미 파일이 있으면 덮지 않는다 — 사람이 정한 처분을 런처가 지우면 안 된다(DISPO-02 §6-1).
    private static void WritePendingDisposition(string outDir, string launchId, DateTime startedAt)
    {
        var path = Path.Combine(outDir, "disposition.json");
        if (File.Exists(path)) return;

        var record = new JsonObject
        {
            ["launchId"] = launchId,
            ["state"] = "pending",
            ["decidedAt"] = startedAt.ToString("yyyy-MM-dd"),
            ["actor"] = "codex-launch (자동) — 처분은 사람이 정한다",
            ["note"] = "발사만 됐고 반입·폐기는 정해지지 않았다. launch-disposition이 위반으로 센다.",
        };
        File.WriteAllText(path, record.ToJsonString(JsonOptions), Utf8NoBom);
    }

    // 실행자에게 줄 프롬프트를 만든다. 금지 행위를 본문에 실어 요청과 프롬프트가 어긋나지 않게 한다.
    private static string BuildPrompt(string root, JsonObject request)
    {
        var directive = File.ReadAllText(Path.Combine(root, Str(request, "directivePath").Replace('/', Path.DirectorySeparatorChar)), Utf8NoBom);
        var lines = new List<string>
        {
            "You are a harness executor working inside an isolated clean copy of this repository.",
            $"Write ONLY files matching: {string.Join(", ", Array(request, "allowedPaths"))}",
            $"You must NOT: {string.Join(", ", Array(request, "forbiddenActions"))}",
            "Do not run git commit, git push, or any state transition. Do not declare PASS or VERIFIED —",
            "a separate program verifier decides that, and your claim would not be read.",
            "",
            "# Directive",
            directive,
        };
        return string.Join("\n", lines);
    }

    // 신뢰 원점 기록이 자동 실행을 허용하는지 본다. 기록이 없으면 허용하지 않는다.
    private static bool AutomatedExecutionReady(string root)
    {
        var dir = Path.Combine(root, TrustOriginDirRel);
        if (!Directory.Exists(dir)) return false;
        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
        {
            try
            {
                var record = JsonNode.Parse(File.ReadAllText(file, Utf8NoBom))?.AsObject();
                if (record?["automatedExecutionReady"]?.GetValue<bool>() == true) return true;
            }
            catch { }
        }
        return false;
    }

    // 사본에서 실제로 바뀐 경로를 읽는다. untracked도 포함한다 — 새 파일이 안 보이면 범위 검사가 헛돈다.
    private static List<string> ChangedPaths(string workdir)
    {
        var status = RunProcess(workdir, "git", ["status", "--porcelain"], 120).Stdout;
        var paths = new List<string>();
        foreach (var line in status.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.Length < 4) continue;
            paths.Add(trimmed[3..].Trim().Trim('"').Replace('\\', '/'));
        }
        return paths;
    }

    // 경로가 allowlist 항목에 걸리는지 본다. ** 로 끝나면 접두사, 아니면 정확히 같아야 한다.
    private static bool Matches(string path, string pattern)
    {
        var p = pattern.Replace('\\', '/');
        if (p.EndsWith("**", StringComparison.Ordinal))
            return path.StartsWith(p[..^2], StringComparison.OrdinalIgnoreCase);
        return string.Equals(path, p, StringComparison.OrdinalIgnoreCase);
    }

    // 자식 프로세스를 돌리고 stdout/stderr/exit를 모은다. 시간 초과는 성공이 아니다.
    private static (int ExitCode, string Stdout, string Stderr, bool TimedOut) RunProcess(
        string cwd, string file, string[] args, int timeoutSeconds, string? stdin = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = file, WorkingDirectory = cwd,
            RedirectStandardOutput = true, RedirectStandardError = true,
            RedirectStandardInput = stdin is not null, UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        try
        {
            using var process = Process.Start(psi);
            if (process is null) return (int.MinValue, "", "process-start-failed", false);
            if (stdin is not null) { process.StandardInput.Write(stdin); process.StandardInput.Close(); }
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutSeconds * 1000))
            {
                try { process.Kill(true); } catch { }
                return (int.MinValue, stdout.Result, stderr.Result, true);
            }
            return (process.ExitCode, stdout.Result, stderr.Result, false);
        }
        catch (Exception ex) { return (int.MinValue, "", ex.Message, false); }
    }

    // 실행 파일의 전체 경로를 찾는다. Windows에서 npm 심(codex.cmd)은 이름만으로는
    // Process.Start가 못 찾는다(PATHEXT를 보지 않는다). 못 찾으면 null이지 이름 그대로
    // 넘겨 "실행했는데 실패했다"로 보이게 하지 않는다.
    private static string? ResolveExecutable(string name)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        var exts = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT")
            .Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            // PATHEXT 후보를 먼저 본다. npm은 확장자 없는 셸 스크립트도 같이 두는데,
            // Windows에서 그것은 실행 파일이 아니다 — 먼저 집으면 못 쏘고 실패로만 남는다.
            foreach (var ext in exts.Append(""))
            {
                var candidate = Path.Combine(dir.Trim(), name + ext);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    // 저장소 루트를 찾는다.
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

    // git 명령을 돌려 stdout만 돌려준다.
    private static string Git(string root, params string[] args) => RunProcess(root, "git", args, 60).Stdout;
    // JSON에서 문자열 값을 읽는다. 없으면 빈 문자열이다.
    private static string Str(JsonObject o, string key) => o[key]?.ToString() ?? "";
    // JSON에서 정수 값을 읽는다. 숫자가 아니면 0이며, 호출부가 범위로 다시 거른다.
    private static int Int(JsonObject o, string key) => int.TryParse(o[key]?.ToString(), out var v) ? v : 0;
    // --이름 형태의 플래그가 있는지 본다.
    private static bool HasFlag(string[] args, string name) => args.Contains("--" + name, StringComparer.OrdinalIgnoreCase);
    // --이름 다음 값을 읽는다. 없으면 빈 문자열이다.
    private static string Flag(string[] args, string name)
    {
        for (var i = 0; i + 1 < args.Length; i++) if (args[i] == "--" + name) return args[i + 1];
        return "";
    }

    // JSON 배열을 문자열 목록으로 읽는다. 없으면 빈 목록이다.
    private static List<string> Array(JsonObject o, string key) =>
        o[key]?.AsArray()?.Select(n => n?.ToString() ?? "").Where(s => s.Length > 0).ToList() ?? [];

    // 바이트열의 sha256을 소문자 16진수로 돌려준다. 증거 해시는 전부 이것으로 만든다.
    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    // stderr는 원인 파악용으로 꼬리만 남긴다. 판정에는 쓰지 않는다.
    private static string Tail(string text) { var t = (text ?? "").Trim(); return t.Length <= 500 ? t : t[^500..]; }

    // 결과 JSON을 stdout으로 낸다.
    private static void Output(JsonObject payload) => Console.WriteLine(payload.ToJsonString(JsonOptions));

    // 거절을 stderr JSON + exit code로 낸다. 판정은 문자열이 아니라 종료 코드로 전달된다.
    private static int Error(string code, int exit)
    {
        Console.Error.WriteLine($"{{\"error\":\"{code}\"}}");
        return exit;
    }
}
