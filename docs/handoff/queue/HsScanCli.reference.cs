// HARNESS-02 참조 스캐폴드 — hs-scan: 승격 심사(HS-GATE) 트리거를 '기계'가 탐지한다.
// 주의: docs/handoff/queue/ 아래 *참조본*. 빌드 대상 아님.
// 실행자(sonnet)가 server/HsScanCli.cs로 옮기고 CliRouter에 "hs-scan" 분기를 등록한다.
//
// 왜 존재하나:
//   CODEX-AUTO-15min-routine 4.7에 HS-GATE가 이미 있었지만 한 번도 돌지 않았다.
//   증거: HS-CANDIDATES.md가 2026-07-11까지 아예 존재하지 않았다.
//   원인: 트리거가 "해당하면 수행하라"는 LLM 재량이었다 — 아무도 스스로 발동시키지 않는다.
//   → 트리거 탐지를 프로그램이 하고, 루프는 그 exit code에 복종한다.
//     "LLM은 판단(점수), 프로그램은 기억·탐지(트리거)".
//
// 신호(전부 기계 판정, 해석 없음):
//   S1 반복 실패계열 : 같은 failureClass가 2회 이상 → 승격 후보. (KNOWLEDGE-PROMOTION "반복이 자산의 조건")
//                      실측: FAIL-005 + FAIL-010 = unnormalized_gate 2회 → gate-clean 하네스 승격의 근거였다.
//   S2 실패 누적     : 마지막 HS-GATE 이후 FAIL 케이스가 3건 이상 증가.
//   S3 정기          : 마지막 HS-GATE로부터 24시간 경과.
//   S4 반복 구성요소 : 같은 구성요소가 3회 이상 등장.
//
// exit: 0=트리거 없음, 1=트리거 있음(루프는 HS-GATE 수행 의무), 2=실행 오류.

using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

// 승격 심사 트리거를 탐지하는 하네스 CLI. 판정(점수)은 하지 않는다 — 그건 LLM(hs-gate 스킬)의 몫.
internal static class HsScanCli
{
    // 메타 태그 — 실패 '메커니즘'이 아니라 분류용 꼬리표라 반복해도 승격 신호가 아니다.
    // 이걸 거르지 않으면 design_learning(8회) 같은 태그가 매 회차 노이즈 후보를 만든다.
    private static readonly HashSet<string> MetaClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "design_learning",   // "배웠다"는 꼬리표. 거의 모든 케이스에 붙는다.
        "known_failure",     // "알려진 실패"라는 상태 표시.
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    internal static int Run(string[] args)
    {
        try
        {
            var root = FindRepoRoot();
            var cases = ParseFailureIndex(Path.Combine(root, "docs", "wiki", "failures", "index.md"));
            var (lastGate, judgedClasses) = ParseCandidates(
                Path.Combine(root, "docs", "handoff", "HS-CANDIDATES.md"));

            var signals = new JsonArray();
            var candidates = new JsonArray();

            // S1 — 같은 failureClass 2회 이상, 아직 심사되지 않은 것.
            foreach (var g in cases.SelectMany(c => c.Classes.Select(cl => (cl, c)))
                                   .Where(x => !MetaClasses.Contains(x.cl))   // 메타 태그 제외(노이즈 차단)
                                   .GroupBy(x => x.cl)
                                   .Where(g => g.Count() >= 2))
            {
                if (judgedClasses.Contains(g.Key)) continue;   // 이미 심사됨 → 중복 제기 금지
                signals.Add($"S1 반복 실패계열: {g.Key} {g.Count()}회");
                candidates.Add(new JsonObject
                {
                    ["signal"] = "S1",
                    ["failureClass"] = g.Key,
                    ["occurrences"] = g.Count(),
                    ["cases"] = new JsonArray(g.Select(x => (JsonNode)x.c.Id!).ToArray()),
                    ["suggestedType"] = "하네스",   // 기계 판정 가능하면 하네스, 절차면 스킬 — 최종 결정은 HS-GATE
                    ["why"] = $"같은 실패계열이 {g.Count()}회 반복 — KNOWLEDGE-PROMOTION의 승급 조건(2회+) 충족",
                });
            }

            // S4 — 같은 구성요소 3회 이상.
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
                    ["cases"] = new JsonArray(g.Select(x => (JsonNode)x.c.Id!).ToArray()),
                    ["suggestedType"] = "하네스",
                    ["why"] = $"같은 구성요소에서 {g.Count()}회 실패 — 회귀 하네스 후보",
                });
            }

            // S2 — 마지막 심사 이후 FAIL 3건 이상 증가.
            var newSinceGate = lastGate is null ? cases.Count : cases.Count - CountAtGate(lastGate.Value, cases);
            if (newSinceGate >= 3)
                signals.Add($"S2 실패 누적: 마지막 심사 이후 {newSinceGate}건 증가");

            // S3 — 정기(24시간).
            double? daysSince = lastGate is null ? null : (DateTime.Now - lastGate.Value).TotalDays;
            if (lastGate is null)
                signals.Add("S3 정기: 심사 이력 없음(최초)");
            else if (daysSince >= 1.0)
                signals.Add($"S3 정기: 마지막 심사 후 {daysSince:F1}일 경과");

            var triggered = signals.Count > 0;
            var report = new JsonObject
            {
                ["harness"] = "hs-scan",
                ["triggered"] = triggered,
                ["lastGate"] = lastGate?.ToString("yyyy-MM-dd HH:mm"),
                ["daysSinceLastGate"] = daysSince,
                ["failureCaseCount"] = cases.Count,
                ["signals"] = signals,
                ["candidates"] = candidates,
                ["action"] = triggered
                    ? "HS-GATE 수행 의무 — skills/common/hs-gate.md 절차로 점수화 후 HS-CANDIDATES.md에 append"
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

    // 실패 위키 색인 표를 파싱한다: | ID | 제목 | 상태 | failureClass | 구성요소 |
    private static List<FailCase> ParseFailureIndex(string path)
    {
        var list = new List<FailCase>();
        if (!File.Exists(path)) return list;
        foreach (var line in File.ReadAllLines(path))
        {
            var t = line.TrimStart();
            if (!t.StartsWith("|")) continue;
            var cols = t.Split('|', StringSplitOptions.TrimEntries);
            if (cols.Length < 6) continue;
            var m = Regex.Match(cols[1], @"FAIL-\d{4}-\d{3}");
            if (!m.Success) continue;   // 헤더/구분선 skip
            list.Add(new FailCase(
                m.Value,
                Split(cols[4]),
                Split(cols[5])));
        }
        return list;

        static List<string> Split(string s) =>
            s.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    // HS-CANDIDATES.md에서 마지막 심사 시각과 이미 심사된 failureClass를 읽는다.
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

            // "judgedClasses: unnormalized_gate, path_escape" 형태로 심사 완료 계열을 기록
            var jc = Regex.Match(line, @"judgedClasses:\s*(.+)$");
            if (jc.Success)
                foreach (var c in jc.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries))
                    if (c.Length > 0) judged.Add(c);
        }
        return (last, judged);
    }

    // 마지막 심사 시점의 케이스 수(근사): 심사 기록이 없으면 0.
    private static int CountAtGate(DateTime gate, List<FailCase> cases) => 0;

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }

    private sealed record FailCase(string Id, List<string> Classes, List<string> Components);
}
