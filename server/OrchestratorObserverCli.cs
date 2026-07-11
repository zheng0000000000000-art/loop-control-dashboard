// 오케스트레이터 관측 전용 CLI. 큐·깃·실행상태를 읽어 발사 결정을 계산만 하고 기록한다.
// 부작용 0: Process.Start(실행자) 금지, git commit/push 금지, approve 금지.
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

// 오케스트레이터 관측 전용 CLI. 큐/깃/실행상태를 읽어 "발사 결정"을 계산만 하고 기록한다.
internal static class OrchestratorObserverCli
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // 진입점. exit 0=관측 성공(발사조건 무관), 2=관측 자체 오류.
    internal static int Run(string[] args)
    {
        try
        {
            var repoRoot = FindRepoRoot();

            var queue = ParseSonnetQueue(Path.Combine(repoRoot, "docs", "handoff", "SONNET-QUEUE.md"));
            var clean = EvaluateTreeClean(repoRoot, "server");   // 정규화 내용 해시 판정(FAIL-010)
            var serverClean = clean.ContentDirty == 0;
            var executorRunning = IsExecutorRunning(repoRoot);
            var pendingImports = CountImportPending(Path.Combine(repoRoot, "outbox"));
            var currentDiId = ReadWorkstateDiId(Path.Combine(repoRoot, "docs", "handoff", "WORKSTATE.json"));

            // 발사 결정 계산(실행하지 않음). SONNET-QUEUE 자동발사 규칙을 그대로 코드화.
            var inProgress = queue.FirstOrDefault(q => q.Status == "진행");
            var nextWaiting = queue.FirstOrDefault(q => q.Status == "대기");

            var blockers = new JsonArray();
            if (!serverClean) blockers.Add($"server/ 실내용 변경 {clean.ContentDirty}건");
            if (executorRunning) blockers.Add("실행 중 executor(PID 파일 존재)");
            if (inProgress is not null) blockers.Add($"진행 항목 미완결: {inProgress.Di}");
            if (nextWaiting is null) blockers.Add("대기 항목 없음(큐 소진 또는 없음)");

            // 진행 항목이 있으면 그 커밋이 로그에 존재하는지(완료 결속) 확인.
            string? completionCheck = null;
            if (inProgress is not null)
                completionCheck = CommitExistsForDi(repoRoot, inProgress.Di)
                    ? "진행 항목 커밋 로그에 존재 → 완료로 승격 가능"
                    : "진행 항목 커밋 미발견 → 미완결(발사 금지)";

            var wouldLaunch = blockers.Count == 0;

            var report = new JsonObject
            {
                ["observedAt"] = DateTimeOffset.Now.ToString("o"),
                ["mode"] = "observe-only",   // 이 빌드는 절대 발사/커밋/결재하지 않음
                ["serverTreeClean"] = serverClean,
                ["representationOnlyCount"] = clean.RepresentationOnly,   // 표현차(CRLF/BOM) — 게이트를 막지 않음
                ["hygieneWarning"] = clean.RepresentationOnly > 0
                    ? "표현만 다른 파일 존재 — 어떤 도구가 파일을 되쓰고 있다(FAIL-2026-010)."
                    : null,
                ["executorRunning"] = executorRunning,
                ["importPendingCount"] = pendingImports,
                ["workstateDiId"] = currentDiId,
                ["queue"] = new JsonArray(queue.Select(q => (JsonNode)new JsonObject
                {
                    ["order"] = q.Order,
                    ["di"] = q.Di,
                    ["status"] = q.Status,
                }).ToArray()),
                ["inProgress"] = inProgress?.Di,
                ["nextWaiting"] = nextWaiting?.Di,
                ["completionCheck"] = completionCheck,
                ["wouldLaunch"] = wouldLaunch,
                ["wouldLaunchTarget"] = wouldLaunch ? nextWaiting?.Di : null,
                ["blockers"] = blockers,
                ["note"] = "관측 전용. wouldLaunch=true여도 이 프로그램은 발사하지 않는다.",
            };

            Console.WriteLine(report.ToJsonString(JsonOptions));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{{\"error\":\"orch-observe 실패: {ex.Message}\"}}");
            return 2;
        }
    }

    // 큐 항목 표(| 순번 | DI | 경로 | 영역 | 상태 |)를 파싱한다.
    private static List<QueueItem> ParseSonnetQueue(string path)
    {
        var items = new List<QueueItem>();
        if (!File.Exists(path)) return items;
        foreach (var line in File.ReadAllLines(path))
        {
            var t = line.TrimStart();
            if (!t.StartsWith("|")) continue;
            var cols = t.Split('|', StringSplitOptions.TrimEntries);
            // cols[0]="" , [1]=순번, [2]=DI, [3]=경로, [4]=영역, [5]=상태
            if (cols.Length < 6) continue;
            if (!int.TryParse(cols[1], out var order)) continue; // 헤더/구분선 skip
            var status = cols[5].StartsWith("완료") ? "완료"
                       : cols[5].StartsWith("진행") ? "진행"
                       : cols[5].StartsWith("대기") ? "대기" : cols[5];
            items.Add(new QueueItem(order, cols[2], status));
        }
        return items;
    }

    // 트리 clean 판정. raw git status는 줄바꿈 같은 '표현 차이'만으로도 dirty가 되어
    // 게이트를 영구 잠근다(FAIL-2026-010). 정규화 내용 해시로 실제 변경만 걸러낸다.
    private static CleanVerdict EvaluateTreeClean(string repoRoot, string path)
    {
        var porcelain = RunGit(repoRoot, $"status --porcelain -- {path}") ?? "";
        int contentDirty = 0, representationOnly = 0;

        foreach (var line in porcelain.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var status = line.Length >= 2 ? line[..2] : "??";
            var file = line.Length > 3 ? line[3..].Trim().Trim('"') : "";
            if (file.Length == 0) continue;

            // 미추적·삭제는 표현차가 아니라 실변경으로 본다.
            if (status.Contains('?') || status.Contains('D')) { contentDirty++; continue; }

            var head = RunGitBytes(repoRoot, $"show HEAD:{file}");
            var full = Path.Combine(repoRoot, file);
            if (head is null || !File.Exists(full)) { contentDirty++; continue; }

            if (NormalizedHash(head) == NormalizedHash(File.ReadAllBytes(full)))
                representationOnly++;   // 내용 동일, 바이트만 다름 → 게이트 통과
            else
                contentDirty++;
        }
        return new CleanVerdict(contentDirty, representationOnly);
    }

    // 표현 정규화: BOM 제거 → CRLF/CR을 LF로 → 줄 후행공백 제거 → 끝 개행 통일.
    private static string NormalizedHash(byte[] raw)
    {
        var b = raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF ? raw[3..] : raw;
        var text = new UTF8Encoding(false).GetString(b).Replace("\r\n", "\n").Replace('\r', '\n');
        var norm = string.Join("\n", text.Split('\n').Select(l => l.TrimEnd(' ', '\t'))).TrimEnd('\n') + "\n";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(norm)));
    }

    // git 바이너리 출력을 byte[]로 받는다. HEAD 파일 내용 비교 시 사용.
    private static byte[]? RunGitBytes(string repoRoot, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = repoRoot, RedirectStandardOutput = true,
                RedirectStandardError = true, UseShellExecute = false,
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            using var ms = new MemoryStream();
            p.StandardOutput.BaseStream.CopyTo(ms);
            p.WaitForExit(10000);
            return p.ExitCode == 0 ? ms.ToArray() : null;
        }
        catch { return null; }
    }

    private sealed record CleanVerdict(int ContentDirty, int RepresentationOnly);

    // 실행 감지: PID 파일 존재로 판정(잠정). StartTime 기반 감지는 금지(I-1 교훈).
    private static bool IsExecutorRunning(string repoRoot)
        => File.Exists(Path.Combine(repoRoot, "sonnet-active.pid"));

    // outbox 디렉터리를 순회해 import_pending 상태 항목 수를 반환한다.
    private static int CountImportPending(string outboxRoot)
    {
        if (!Directory.Exists(outboxRoot)) return 0;
        var count = 0;
        foreach (var dir in Directory.EnumerateDirectories(outboxRoot))
        {
            var meta = Path.Combine(dir, "meta.json");
            if (!File.Exists(meta)) continue;
            try
            {
                var node = JsonNode.Parse(File.ReadAllText(meta));
                if ((string?)node?["status"] == "import_pending") count++;
            }
            catch { /* malformed meta는 무시 */ }
        }
        return count;
    }

    // WORKSTATE.json에서 현재 diId를 읽어 반환한다.
    private static string? ReadWorkstateDiId(string path)
    {
        if (!File.Exists(path)) return null;
        try { return (string?)JsonNode.Parse(File.ReadAllText(path))?["diId"]; }
        catch { return null; }
    }

    // git 로그 최근 50건에서 DI 식별자가 포함된 커밋 존재 여부를 반환한다.
    private static bool CommitExistsForDi(string repoRoot, string di)
    {
        var log = RunGit(repoRoot, "log --oneline -50");
        return log is not null && log.Contains(di, StringComparison.OrdinalIgnoreCase);
    }

    // .git 디렉터리를 상위 탐색해 저장소 루트 경로를 반환한다.
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }

    // git 읽기 전용 호출. commit/push 등 변경 명령에는 쓰지 않는다.
    private static string? RunGit(string repoRoot, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            var stdout = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            return stdout;
        }
        catch { return null; }
    }

    private sealed record QueueItem(int Order, string Di, string Status);
}
