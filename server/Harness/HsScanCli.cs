// 지식 승격 심사(HS-GATE)의 트리거를 기계가 탐지하는 하네스 CLI.
// 점수화(판단)는 하지 않는다 — 그건 skills/common/hs-gate.md(LLM)의 몫이다.
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

internal static class HsScanCli
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // 실패 '메커니즘'이 아니라 분류 꼬리표라 반복해도 승격 신호가 아니다(노이즈 차단).
    private static readonly HashSet<string> MetaClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "design_learning", "known_failure",
    };

    // hs-scan 진입점. exit 0=트리거 없음, 1=트리거 있음(HS-GATE 의무), 2=오류.
    internal static int Run(string[] args)
    {
        try
        {
            var root = GitTools.FindRepoRoot();
            var cases = ParseFailureIndex(Path.Combine(root, "docs", "wiki", "failures", "index.md"));
            var (lastGate, judged) = ParseCandidates(Path.Combine(root, "docs", "handoff", "HS-CANDIDATES.md"));

            var signals = new JsonArray();
            var candidates = new JsonArray();

            // S1 — 같은 failureClass가 2회 이상이고 아직 심사되지 않은 것.
            foreach (var g in cases.SelectMany(c => c.Classes.Select(cl => (cl, c)))
                                   .Where(x => !MetaClasses.Contains(x.cl))
                                   .GroupBy(x => x.cl)
                                   .Where(g => g.Count() >= 2))
            {
                if (judged.Contains(g.Key)) continue;
                signals.Add($"S1 반복 실패계열: {g.Key} {g.Count()}회");
                candidates.Add(new JsonObject
                {
                    ["signal"] = "S1",
                    ["failureClass"] = g.Key,
                    ["occurrences"] = g.Count(),
                    ["cases"] = new JsonArray(g.Select(x => (JsonNode)x.c.Id).ToArray()),
                    ["suggestedType"] = "하네스",
                    ["why"] = $"같은 실패계열 {g.Count()}회 반복 — 승급 조건(2회+) 충족",
                });
            }

            // S4 — 같은 구성요소가 3회 이상 실패한 것.
            foreach (var g in cases.SelectMany(c => c.Components.Select(cp => (cp, c)))
                                   .GroupBy(x => x.cp)
                                   .Where(g => g.Count() >= 3))
            {
                signals.Add($"S4 반복 구성요소: {g.Key} {g.Count()}회");
                candidates.Add(new JsonObject
                {
                    ["signal"] = "S4",
                    ["component"] = g.Key,
                    ["occurrences"] = g.Count(),
                    ["cases"] = new JsonArray(g.Select(x => (JsonNode)x.c.Id).ToArray()),
                    ["suggestedType"] = "하네스",
                    ["why"] = $"같은 구성요소에서 {g.Count()}회 실패 — 회귀 하네스 후보",
                });
            }

            // S3 — 정기(24시간) 또는 심사 이력 없음.
            double? daysSince = lastGate is null ? null : (DateTime.Now - lastGate.Value).TotalDays;
            if (lastGate is null) signals.Add("S3 정기: 심사 이력 없음(최초)");
            else if (daysSince >= 1.0) signals.Add($"S3 정기: 마지막 심사 후 {daysSince:F1}일 경과");

            var triggered = signals.Count > 0;
            var report = new JsonObject
            {
                ["harness"] = "hs-scan",
                ["triggered"] = triggered,
                ["lastGate"] = lastGate?.ToString("yyyy-MM-dd HH:mm"),
                ["failureCaseCount"] = cases.Count,
                ["signals"] = signals,
                ["candidates"] = candidates,
                ["action"] = triggered
                    ? "HS-GATE 수행 의무 — skills/common/hs-gate.md 절차로 점수화 후 HS-CANDIDATES.md에 기록"
                    : "트리거 없음 — 이번 회차 심사 불요",
            };
            Console.WriteLine(report.ToJsonString(JsonOptions));
            return triggered ? 1 : 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{{\"error\":\"hs-scan 실패: {ex.Message}\"}}");
            return 2;
        }
    }

    // 실패 위키 색인 표를 파싱해 케이스 목록을 만든다.
    private static List<FailCase> ParseFailureIndex(string path)
    {
        var list = new List<FailCase>();
        if (!File.Exists(path)) return list;
        foreach (var line in File.ReadAllLines(path))
        {
            var t = line.TrimStart();
            if (!t.StartsWith('|')) continue;
            var cols = t.Split('|', StringSplitOptions.TrimEntries);
            if (cols.Length < 6) continue;
            var m = Regex.Match(cols[1], @"FAIL-\d{4}-\d{3}");
            if (!m.Success) continue;
            list.Add(new FailCase(m.Value, SplitCsv(cols[4]), SplitCsv(cols[5])));
        }
        return list;
    }

    // 쉼표 구분 문자열을 목록으로 나눈다.
    private static List<string> SplitCsv(string s)
        => s.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();

    // HS-CANDIDATES에서 마지막 심사 시각과 이미 심사된 계열을 읽는다.
    private static (DateTime? lastGate, HashSet<string> judged) ParseCandidates(string path)
    {
        var judged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        DateTime? last = null;
        if (!File.Exists(path)) return (null, judged);

        foreach (var line in File.ReadAllLines(path))
        {
            var lg = Regex.Match(line, @"lastGate:\s*([0-9]{4}-[0-9]{2}-[0-9]{2}[ T][0-9]{2}:[0-9]{2})");
            if (lg.Success && DateTime.TryParse(lg.Groups[1].Value, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var dt))
                last = dt;

            var jc = Regex.Match(line, @"judgedClasses:\s*(.+?)`?\s*$");
            if (jc.Success)
                foreach (var c in SplitCsv(jc.Groups[1].Value.Trim('`')))
                    judged.Add(c);
        }
        return (last, judged);
    }

    // 위키 케이스 1건(ID·실패계열·구성요소).
    private sealed record FailCase(string Id, List<string> Classes, List<string> Components);
}
