// 실행자의 자기보고(문서의 "완료" 주장)를 실체(코드·커밋)와 대조하는 하네스 CLI.
// FIX-01은 문서가 완료를 주장하는데 코드에 심볼이 없는 상태로 3회 반복됐다. 그 수작업 대조를 코드로 고정한다.
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

internal static class ClaimCheckCli
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // claim-check 진입점. exit 0=주장과 실체 일치, 1=불일치 존재, 2=오류.
    internal static int Run(string[] args)
    {
        try
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("{\"error\":\"사용법: claim-check <diId>\"}");
                return 2;
            }

            var root = GitTools.FindRepoRoot();
            var diId = args[1];
            var doc = FindVerificationDoc(root, diId);
            if (doc is null)
            {
                Console.Error.WriteLine($"{{\"error\":\"검증 문서를 찾지 못함: {diId}\"}}");
                return 2;
            }

            var text = File.ReadAllText(doc);
            var claims = new JsonArray();
            var mismatch = 0;

            // 주장 1 — 신설/수정된 파일이 실제로 존재하는가.
            foreach (var file in ExtractClaimedFiles(text))
            {
                var exists = File.Exists(Path.Combine(root, file));
                if (!exists) mismatch++;
                claims.Add(MakeClaim("file", $"{file} 존재", "존재함", exists ? "존재함" : "없음", exists));
            }

            // 주장 2 — 언급된 심볼이 실제 코드에 있는가(FIX-01 허위 완료주장이 여기 걸렸어야 했다).
            foreach (var sym in ExtractClaimedSymbols(text))
            {
                var found = GitTools.RunGitText(root, $"grep -l {sym} -- server");
                var ok = !string.IsNullOrWhiteSpace(found);
                if (!ok) mismatch++;
                claims.Add(MakeClaim("symbol", $"{sym} 코드에 존재", "존재함",
                    ok ? found!.Trim().Replace("\n", ", ") : "코드에 없음", ok));
            }

            // 주장 3 — 언급된 커밋 해시가 로그에 실재하는가.
            foreach (var hash in ExtractClaimedCommits(text))
            {
                var log = GitTools.RunGitText(root, $"cat-file -t {hash}");
                var ok = log?.Trim() == "commit";
                if (!ok) mismatch++;
                claims.Add(MakeClaim("commit", $"커밋 {hash} 실재", "실재함", ok ? "실재함" : "로그에 없음", ok));
            }

            var report = new JsonObject
            {
                ["harness"] = "claim-check",
                ["diId"] = diId,
                ["document"] = Path.GetRelativePath(root, doc).Replace('\\', '/'),
                ["claimCount"] = claims.Count,
                ["mismatchCount"] = mismatch,
                ["verdict"] = mismatch == 0 ? "MATCH" : "MISMATCH",
                ["claims"] = claims,
                ["note"] = "주장과 실체의 대조만 한다. 문서 정정은 실행자·조율자·사람의 몫이다.",
            };
            Console.WriteLine(report.ToJsonString(JsonOptions));
            return mismatch == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{{\"error\":\"claim-check 실패: {ex.Message}\"}}");
            return 2;
        }
    }

    // 주장 1건을 JSON 항목으로 만든다.
    private static JsonObject MakeClaim(string kind, string claim, string asserted, string actual, bool ok)
        => new()
        {
            ["kind"] = kind,
            ["claim"] = claim,
            ["asserted"] = asserted,
            ["actual"] = actual,
            ["verdict"] = ok ? "match" : "MISMATCH",
        };

    // diId에 해당하는 검증 문서를 찾는다.
    private static string? FindVerificationDoc(string root, string diId)
    {
        var dir = Path.Combine(root, "docs", "verification");
        if (!Directory.Exists(dir)) return null;
        var key = diId.Replace("-", "").ToLowerInvariant();
        return Directory.EnumerateFiles(dir, "*.md")
            .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f)
                .Replace("-", "").ToLowerInvariant().Contains(key));
    }

    // 문서가 신설·수정했다고 주장하는 server/ 파일 경로를 뽑는다.
    private static IEnumerable<string> ExtractClaimedFiles(string text)
        => Regex.Matches(text, @"`?(server/[A-Za-z0-9_/\.]+\.cs)`?")
            .Select(m => m.Groups[1].Value)
            .Distinct();

    // 문서가 적용했다고 주장하는 코드 심볼(PascalCase 식별자)을 뽑는다.
    private static IEnumerable<string> ExtractClaimedSymbols(string text)
        => Regex.Matches(text, @"`([A-Z][A-Za-z0-9]{3,})`")
            .Select(m => m.Groups[1].Value)
            .Where(s => !s.EndsWith("cs", StringComparison.Ordinal))
            .Distinct()
            .Take(10);

    // 문서가 언급하는 커밋 해시를 뽑는다.
    private static IEnumerable<string> ExtractClaimedCommits(string text)
        => Regex.Matches(text, @"\b([0-9a-f]{7,40})\b")
            .Select(m => m.Groups[1].Value)
            .Where(h => h.Any(char.IsDigit) && h.Any(char.IsLetter))
            .Distinct()
            .Take(5);
}
