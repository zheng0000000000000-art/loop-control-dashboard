// HARNESS-01 참조 스캐폴드 — gate-clean: 트리 clean을 '정규화된 내용 해시'로 판정한다.
// 주의: docs/handoff/queue/ 아래 *참조본*. 빌드 대상 아님.
// 실행자(sonnet)가 server/GateCleanCli.cs로 옮기고 CliRouter에 "gate-clean" 분기를 등록한 뒤 검증한다.
//
// 왜 존재하나 (FAIL-2026-010):
//   조율자 발사조건①은 `git status --porcelain -- server`가 비어야 통과였다.
//   어떤 도구가 .cs를 LF→CRLF로 되쓰자(내용 변경 0) git은 영구 "수정됨"으로 봤고,
//   게이트가 며칠간 거짓이 되어 큐가 정지했다. 표현(줄바꿈)이 게이트를 잠근 것이다.
//   → 게이트가 물어야 할 것은 "바이트가 다른가"가 아니라 "내용이 다른가"이다.
//
// 판정 규칙:
//   각 추적 파일에 대해 HEAD 블롭과 워킹트리 내용을 '정규화'한 뒤 SHA-256 비교.
//   정규화 = CRLF/CR → LF 통일, UTF-8 BOM 제거, 각 줄 후행공백 제거, 파일 끝 개행 통일.
//   - 정규화 후 같음 + 원본 바이트 다름  → representation-only (표현차. 게이트 통과, 위생 경고)
//   - 정규화 후 다름                      → content-dirty  (진짜 변경. 게이트 차단)
//   게이트는 contentDirty만 본다.

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

// 트리 clean 여부를 정규화 내용 해시로 판정하는 하네스 CLI.
internal static class GateCleanCli
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // 진입점. exit 0=게이트 통과(내용 변경 0), 1=내용 변경 존재(차단), 2=실행 오류.
    // 사용법: gate-clean [경로 ...]   (기본 경로: server)
    internal static int Run(string[] args)
    {
        try
        {
            var repoRoot = FindRepoRoot();
            var paths = args.Skip(1).Where(a => !a.StartsWith("-")).ToArray();
            if (paths.Length == 0) paths = new[] { "server" };

            var files = new JsonArray();
            var contentDirty = 0;
            var representationOnly = 0;

            foreach (var path in paths)
            {
                // raw git이 '수정됨'이라 보는 파일만 검사 대상(그 외는 애초에 clean).
                var porcelain = RunGit(repoRoot, $"status --porcelain -- \"{path}\"") ?? "";
                foreach (var line in porcelain.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var status = line.Length >= 2 ? line[..2] : "??";
                    var file = line.Length > 3 ? line[3..].Trim().Trim('"') : "";
                    if (file.Length == 0) continue;

                    // 미추적/삭제는 정규화 비교 대상이 아니다 — 그대로 내용 변경으로 본다.
                    if (status.Contains('?') || status.Contains('D'))
                    {
                        contentDirty++;
                        files.Add(new JsonObject
                        {
                            ["path"] = file,
                            ["gitStatus"] = status.Trim(),
                            ["verdict"] = "content-dirty",
                            ["reason"] = status.Contains('?') ? "미추적 파일" : "삭제됨",
                        });
                        continue;
                    }

                    var headBlob = RunGitBytes(repoRoot, $"show HEAD:\"{file}\"");
                    var workPath = Path.Combine(repoRoot, file);
                    if (headBlob is null || !File.Exists(workPath))
                    {
                        contentDirty++;
                        files.Add(new JsonObject
                        {
                            ["path"] = file, ["gitStatus"] = status.Trim(),
                            ["verdict"] = "content-dirty", ["reason"] = "HEAD 블롭 또는 워킹파일 없음",
                        });
                        continue;
                    }

                    var workBytes = File.ReadAllBytes(workPath);
                    var rawSame = headBlob.SequenceEqual(workBytes);
                    var hHead = NormalizedHash(headBlob);
                    var hWork = NormalizedHash(workBytes);

                    if (hHead == hWork)
                    {
                        // 내용은 같다. 바이트만 다르다 = 표현차. 게이트를 막지 않는다.
                        if (!rawSame) representationOnly++;
                        files.Add(new JsonObject
                        {
                            ["path"] = file,
                            ["gitStatus"] = status.Trim(),
                            ["verdict"] = rawSame ? "clean" : "representation-only",
                            ["reason"] = rawSame ? "동일" : DescribeRepresentationDiff(headBlob, workBytes),
                            ["normalizedHash"] = hWork[..16],
                        });
                    }
                    else
                    {
                        contentDirty++;
                        files.Add(new JsonObject
                        {
                            ["path"] = file,
                            ["gitStatus"] = status.Trim(),
                            ["verdict"] = "content-dirty",
                            ["reason"] = "정규화 후에도 내용이 다름 — 진짜 변경",
                            ["headHash"] = hHead[..16],
                            ["workHash"] = hWork[..16],
                        });
                    }
                }
            }

            var pass = contentDirty == 0;
            var report = new JsonObject
            {
                ["harness"] = "gate-clean",
                ["paths"] = new JsonArray(paths.Select(p => (JsonNode)p!).ToArray()),
                ["contentDirtyCount"] = contentDirty,
                ["representationOnlyCount"] = representationOnly,
                ["gate"] = pass ? "PASS" : "FAIL",
                ["files"] = files,
                // 표현차가 있으면 게이트는 통과시키되 위생 문제로 경고한다(도구가 파일을 되쓰고 있다는 신호).
                ["hygieneWarning"] = representationOnly > 0
                    ? $"표현만 다른 파일 {representationOnly}건 — 어떤 도구가 파일을 되쓰고 있다. .gitattributes 정규화 확인 필요(FAIL-2026-010)."
                    : null,
            };

            Console.WriteLine(report.ToJsonString(JsonOptions));
            return pass ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{{\"error\":\"gate-clean 실패: {ex.Message}\"}}");
            return 2;
        }
    }

    // 표현 차이를 정규화한다: BOM 제거 → CRLF/CR을 LF로 → 줄 후행공백 제거 → 끝 개행 통일.
    private static string NormalizedHash(byte[] raw)
    {
        var text = new UTF8Encoding(false).GetString(StripBom(raw));
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = text.Split('\n').Select(l => l.TrimEnd(' ', '\t'));
        var normalized = string.Join("\n", lines).TrimEnd('\n') + "\n";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }

    private static byte[] StripBom(byte[] b)
        => b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF ? b[3..] : b;

    // 표현차의 종류를 사람이 읽게 설명한다(왜 dirty로 보였는지).
    private static string DescribeRepresentationDiff(byte[] head, byte[] work)
    {
        var reasons = new List<string>();
        var headCrlf = CountCrlf(head); var workCrlf = CountCrlf(work);
        if (headCrlf != workCrlf) reasons.Add(workCrlf > headCrlf ? "LF→CRLF 재작성" : "CRLF→LF 재작성");
        if (HasBom(head) != HasBom(work)) reasons.Add(HasBom(work) ? "BOM 추가됨" : "BOM 제거됨");
        if (reasons.Count == 0) reasons.Add("후행공백/끝개행 차이");
        return string.Join(", ", reasons) + " — 내용 동일";
    }

    private static int CountCrlf(byte[] b)
    {
        var n = 0;
        for (var i = 1; i < b.Length; i++) if (b[i] == 0x0A && b[i - 1] == 0x0D) n++;
        return n;
    }

    private static bool HasBom(byte[] b)
        => b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF;

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }

    private static string? RunGit(string repoRoot, string arguments)
    {
        var bytes = RunGitBytes(repoRoot, arguments);
        return bytes is null ? null : new UTF8Encoding(false).GetString(bytes);
    }

    // git 읽기 전용 호출(status/show). 변경 명령에는 쓰지 않는다.
    private static byte[]? RunGitBytes(string repoRoot, string arguments)
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
            p.WaitForExit(10000);
            return p.ExitCode == 0 ? ms.ToArray() : null;
        }
        catch { return null; }
    }
}
