// 핵심 상태·큐 문서가 비원자적 쓰기로 조용히 잘렸는지 검사하는 하네스 CLI.
// STATUS.md·SONNET-QUEUE.md·CliRouter.cs가 각각 끝이 잘린 채 발견됐다(FAIL-2026-011).
// .cs는 빌드가 잡지만 .md/.json은 잡아주는 것이 없어 WORKSTATE.json이 잘리면 상태 원본이 조용히 소실된다.
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class DocIntegrityCli
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // 잘리면 피해가 큰 핵심 파일들.
    private static readonly string[] CriticalJson =
    {
        "docs/handoff/WORKSTATE.json",
    };

    private static readonly string[] CriticalDocs =
    {
        "docs/STATUS.md",
        "docs/handoff/SONNET-QUEUE.md",
        "docs/handoff/HUMAN-INBOX.md",
        "docs/handoff/HS-CANDIDATES.md",
        "docs/wiki/failures/index.md",
    };

    // doc-integrity 진입점. exit 0=무결, 1=손상 검출, 2=오류.
    internal static int Run(string[] args)
    {
        try
        {
            var root = GitTools.FindRepoRoot();
            var results = new JsonArray();
            var broken = 0;

            foreach (var rel in CriticalJson)
            {
                var (ok, reason) = CheckJson(Path.Combine(root, rel));
                if (!ok) broken++;
                results.Add(Entry(rel, "json", ok, reason));
            }

            foreach (var rel in CriticalDocs)
            {
                var (ok, reason) = CheckMarkdown(Path.Combine(root, rel));
                if (!ok) broken++;
                results.Add(Entry(rel, "markdown", ok, reason));
            }

            var report = new JsonObject
            {
                ["harness"] = "doc-integrity",
                ["checked"] = results.Count,
                ["brokenCount"] = broken,
                ["verdict"] = broken == 0 ? "INTACT" : "TRUNCATED",
                ["files"] = results,
                ["note"] = "비원자적 쓰기로 조용히 잘린 파일을 검출한다. 복구는 사람·조율자의 몫.",
            };
            Console.WriteLine(report.ToJsonString(JsonOptions));
            return broken == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{{\"error\":\"doc-integrity 실패: {ex.Message}\"}}");
            return 2;
        }
    }

    // 검사 결과 1건을 JSON 항목으로 만든다.
    private static JsonObject Entry(string path, string kind, bool ok, string reason)
        => new()
        {
            ["path"] = path,
            ["kind"] = kind,
            ["verdict"] = ok ? "intact" : "TRUNCATED",
            ["reason"] = reason,
        };

    // JSON 파일이 끝까지 파싱되는지 확인한다. 잘리면 파싱이 실패한다.
    private static (bool ok, string reason) CheckJson(string full)
    {
        if (!File.Exists(full)) return (false, "파일 없음");
        try
        {
            JsonNode.Parse(File.ReadAllText(full));
            return (true, "파싱 성공");
        }
        catch (Exception ex)
        {
            return (false, $"파싱 실패(잘림 의심): {ex.Message}");
        }
    }

    // 마크다운이 문장 중간에서 끊겼는지 확인한다. 끝 개행이 없고 마지막 줄이 미완이면 잘림으로 본다.
    private static (bool ok, string reason) CheckMarkdown(string full)
    {
        if (!File.Exists(full)) return (false, "파일 없음");
        var text = File.ReadAllText(full);
        if (text.Length == 0) return (false, "빈 파일");

        // 코드펜스가 열린 채 끝났으면 확실한 잘림.
        var fences = text.Split("```").Length - 1;
        if (fences % 2 != 0)
            return (false, "코드펜스가 닫히지 않음 — 잘림");

        // 끝 개행 없음만으로는 잘림이 아니다(정상 파일도 그럴 수 있다 — 오탐 방지).
        // 잘림의 진짜 표식은 '마지막 줄이 토큰 중간에서 끊긴 것'이다:
        // CliRouter.cs는 "retur", SONNET-QUEUE는 "QUOTA_"에서 끊겼다.
        var lastLine = text.TrimEnd('\n').Split('\n').LastOrDefault()?.TrimEnd() ?? "";
        if (lastLine.Length > 0 && !text.EndsWith('\n') && !EndsCleanly(lastLine))
            return (false, $"마지막 줄이 문장·토큰 중간에서 끊김 — 잘림. 마지막: \"{Tail(text, 30)}\"");

        return (true, text.EndsWith('\n') ? "무결" : "무결(끝 개행 없음 — 경미)");
    }

    // 마지막 줄이 정상적으로 끝났는지 본다. 문장부호·마크다운 구조로 끝나면 정상으로 간주한다.
    private static bool EndsCleanly(string line)
    {
        var last = line[^1];
        return ".!?)]`|\"'>:-*_".Contains(last) || char.IsPunctuation(last);
    }

    // 파일 끝 일부를 사람이 읽게 잘라낸다.
    private static string Tail(string s, int n)
        => s.Length <= n ? s.Trim() : s[^n..].Replace("\n", "\\n").Trim();
}
