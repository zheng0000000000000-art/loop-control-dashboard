// 트리 clean 여부를 '정규화된 내용 해시'로 판정하는 하네스 CLI.
// raw git status는 줄바꿈 같은 표현 차이만으로 dirty가 되어 발사 게이트를 영구 잠근다(FAIL-2026-010).
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class GateCleanCli
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // gate-clean 진입점. exit 0=통과(실내용 변경 0), 1=실내용 변경 존재, 2=오류.
    internal static int Run(string[] args)
    {
        try
        {
            var repoRoot = GitTools.FindRepoRoot();
            var paths = args.Skip(1).Where(a => !a.StartsWith('-')).ToArray();
            if (paths.Length == 0) paths = new[] { "server" };

            var files = new JsonArray();
            var contentDirty = 0;
            var representationOnly = 0;

            foreach (var path in paths)
            {
                var porcelain = GitTools.RunGitText(repoRoot, $"status --porcelain -- {path}") ?? "";
                foreach (var line in porcelain.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var status = line.Length >= 2 ? line[..2] : "??";
                    var file = line.Length > 3 ? line[3..].Trim().Trim('"') : "";
                    if (file.Length == 0) continue;

                    if (status.Contains('?') || status.Contains('D'))
                    {
                        contentDirty++;
                        files.Add(MakeEntry(file, status, "content-dirty",
                            status.Contains('?') ? "미추적 파일" : "삭제됨"));
                        continue;
                    }

                    var head = GitTools.RunGitBytes(repoRoot, $"show HEAD:{file}");
                    var full = Path.Combine(repoRoot, file);
                    if (head is null || !File.Exists(full))
                    {
                        contentDirty++;
                        files.Add(MakeEntry(file, status, "content-dirty", "HEAD 블롭 또는 워킹파일 없음"));
                        continue;
                    }

                    var work = File.ReadAllBytes(full);
                    var rawSame = head.AsSpan().SequenceEqual(work);
                    if (GitTools.NormalizedHash(head) == GitTools.NormalizedHash(work))
                    {
                        if (!rawSame) representationOnly++;
                        files.Add(MakeEntry(file, status,
                            rawSame ? "clean" : "representation-only",
                            rawSame ? "동일" : DescribeRepresentation(head, work)));
                    }
                    else
                    {
                        contentDirty++;
                        files.Add(MakeEntry(file, status, "content-dirty", "정규화 후에도 내용이 다름 — 진짜 변경"));
                    }
                }
            }

            var report = new JsonObject
            {
                ["harness"] = "gate-clean",
                ["paths"] = new JsonArray(paths.Select(p => (JsonNode)p).ToArray()),
                ["contentDirtyCount"] = contentDirty,
                ["representationOnlyCount"] = representationOnly,
                ["gate"] = contentDirty == 0 ? "PASS" : "FAIL",
                ["files"] = files,
                ["hygieneWarning"] = representationOnly > 0
                    ? $"표현만 다른 파일 {representationOnly}건 — 어떤 도구가 파일을 되쓰고 있다(FAIL-2026-010)."
                    : null,
            };
            Console.WriteLine(report.ToJsonString(JsonOptions));
            return contentDirty == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{{\"error\":\"gate-clean 실패: {ex.Message}\"}}");
            return 2;
        }
    }

    // 파일 판정 1건을 JSON 항목으로 만든다.
    private static JsonObject MakeEntry(string file, string status, string verdict, string reason)
        => new()
        {
            ["path"] = file,
            ["gitStatus"] = status.Trim(),
            ["verdict"] = verdict,
            ["reason"] = reason,
        };

    // 표현 차이의 종류(줄바꿈·BOM·공백)를 사람이 읽게 설명한다.
    private static string DescribeRepresentation(byte[] head, byte[] work)
    {
        var reasons = new List<string>();
        if (CountCrlf(head) != CountCrlf(work))
            reasons.Add(CountCrlf(work) > CountCrlf(head) ? "LF→CRLF 재작성" : "CRLF→LF 재작성");
        if (GitTools.HasBom(head) != GitTools.HasBom(work))
            reasons.Add(GitTools.HasBom(work) ? "BOM 추가됨" : "BOM 제거됨");
        if (reasons.Count == 0) reasons.Add("후행공백/끝개행 차이");
        return string.Join(", ", reasons) + " — 내용 동일";
    }

    // CRLF 개수를 센다.
    private static int CountCrlf(byte[] b)
    {
        var n = 0;
        for (var i = 1; i < b.Length; i++)
            if (b[i] == 0x0A && b[i - 1] == 0x0D) n++;
        return n;
    }
}

// 하네스들이 공유하는 git 읽기 전용 헬퍼와 표현 정규화.
internal static class GitTools
{
    // .git 디렉터리를 찾아 저장소 루트를 반환한다.
    internal static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }

    // git 명령을 실행해 표준출력을 바이트로 받는다. 읽기 전용 명령에만 쓴다.
    internal static byte[]? RunGitBytes(string repoRoot, string arguments)
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
            using var ms = new MemoryStream();
            p.StandardOutput.BaseStream.CopyTo(ms);
            p.WaitForExit(15000);
            return p.ExitCode == 0 ? ms.ToArray() : null;
        }
        catch
        {
            return null;
        }
    }

    // git 명령 결과를 문자열로 받는다.
    internal static string? RunGitText(string repoRoot, string arguments)
    {
        var bytes = RunGitBytes(repoRoot, arguments);
        return bytes is null ? null : new UTF8Encoding(false).GetString(bytes);
    }

    // UTF-8 BOM이 있는지 확인한다.
    internal static bool HasBom(byte[] b)
        => b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF;

    // 표현 차이를 제거한 내용 해시를 만든다: BOM 제거 → CRLF/CR을 LF로 → 줄 후행공백 제거 → 끝 개행 통일.
    internal static string NormalizedHash(byte[] raw)
    {
        var b = HasBom(raw) ? raw[3..] : raw;
        var text = new UTF8Encoding(false).GetString(b).Replace("\r\n", "\n").Replace('\r', '\n');
        var norm = string.Join("\n", text.Split('\n').Select(l => l.TrimEnd(' ', '\t'))).TrimEnd('\n') + "\n";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(norm)));
    }
}
