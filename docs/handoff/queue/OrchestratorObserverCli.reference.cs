// ORCH-01 참조 스캐폴드 — 관측 전용(read-only). 발사·커밋·결재 없음.
// 주의: 이 파일은 docs/handoff/queue/ 아래의 *참조본*이다. 빌드 대상 아님.
// 실행자(sonnet)가 server/OrchestratorObserverCli.cs로 옮기고 CliRouter에
// "orch-observe" 분기를 등록한 뒤 dotnet build/verify로 검증한다.
//
// 설계 고정점(ORCHESTRATOR-PROGRAM-VISION 반영):
//  - 이 단계는 "무엇을 할지 계산하되 하지 않는다". wouldLaunch는 판단만 기록.
//  - 실행 감지는 프로세스 StartTime 오판(I-1/데드락) 대신 PID 파일 존재로 한다(잠정).
//    최종형은 오케스트레이터가 자식 프로세스 핸들을 직접 소유한다(별도 단계).
//  - 발사↔완료를 task ID로 결속: 진행 항목의 커밋이 로그에 존재하는지 검증.
//  - 상태 정합: 큐 상태 ↔ git ↔ WORKSTATE diId ↔ outbox pending 교차 확인.
//  - 부작용 0: Process.Start(실행자) 금지, git commit 금지, approve 금지.

using System.Diagnostics;
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
            var serverClean = IsServerTreeClean(repoRoot);
            var executorRunning = IsExecutorRunning(repoRoot);
            var pendingImports = CountImportPending(Path.Combine(repoRoot, "outbox"));
            var currentDiId = ReadWorkstateDiId(Path.Combine(repoRoot, "docs", "handoff", "WORKSTATE.json"));

            // 발사 결정 계산(실행하지 않음). SONNET-QUEUE 자동발사 규칙을 그대로 코드화.
            var inProgress = queue.FirstOrDefault(q => q.Status == "진행");
            var nextWaiting = queue.FirstOrDefault(q => q.Status == "대기");

            var blockers = new JsonArray();
            if (!serverClean) blockers.Add("server/ 작업트리 dirty");
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

    private static bool IsServerTreeClean(string repoRoot)
        => RunGit(repoRoot, "status --porcelain -- server")?.Trim().Length == 0;

    // 실행 감지: PID 파일 존재로 판정(잠정). StartTime 기반 감지는 금지(I-1 교훈).
    private static bool IsExecutorRunning(string repoRoot)
        => File.Exists(Path.Combine(repoRoot, "sonnet-active.pid"));

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

    private static string? ReadWorkstateDiId(string path)
    {
        if (!File.Exists(path)) return null;
        try { return (string?)JsonNode.Parse(File.ReadAllText(path))?["diId"]; }
        catch { return null; }
    }

    private static bool CommitExistsForDi(string repoRoot, string di)
    {
        var log = RunGit(repoRoot, "log --oneline -50");
        return log is not null && log.Contains(di, StringComparison.OrdinalIgnoreCase);
    }

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
