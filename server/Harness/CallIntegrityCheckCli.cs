// 리팩토링으로 이동한 함수의 정의 위치와 호출부 정합성을 확인하는 하네스 CLI.
// DI-R-01~04 수작업 QA를 기본 룰로 고정하고, 필요하면 인자로 단일 심볼도 검사한다.
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

internal static class CallIntegrityCheckCli
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // call-integrity-check 진입점. exit 0=정의·호출 정합, 1=불일치, 2=사용법 오류.
    internal static int Run(string[] args)
    {
        try
        {
            var root = GitTools.FindRepoRoot();
            var rules = args.Length >= 4
                ? new List<CallRule> { new(args[1], args[2], args[3], 1, "server/Program.cs") }
                : DefaultRules();

            var files = Directory.EnumerateFiles(Path.Combine(root, "server"), "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                    && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => new SourceFile(Path.GetRelativePath(root, path).Replace('\\', '/'), File.ReadAllText(path)))
                .ToList();

            var results = new JsonArray();
            var failures = 0;
            foreach (var rule in rules)
            {
                var result = CheckRule(files, rule);
                if (!result.Ok) failures++;
                results.Add(result.Json);
            }

            var report = new JsonObject
            {
                ["harness"] = "call-integrity-check",
                ["mode"] = args.Length >= 4 ? "input" : "default-refactor-rules",
                ["ruleCount"] = rules.Count,
                ["failureCount"] = failures,
                ["verdict"] = failures == 0 ? "PASS" : "FAIL",
                ["rules"] = results,
                ["note"] = "Checks moved-method definitions, qualified call sites, and stale unqualified calls in the old owner file.",
            };
            Console.WriteLine(report.ToJsonString(JsonOptions));
            return failures == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{{\"error\":\"call-integrity-check failed: {ex.Message}\"}}");
            return 2;
        }
    }

    // DI-R-01~04에서 이동한 대표 호출부를 기본 룰로 만든다.
    private static List<CallRule> DefaultRules()
        => new()
        {
            new("CliRouter.TryRun", "server/Cli/CliRouter.cs", "server/Program.cs", 1, "server/Program.cs"),
            new("InboxBuilder.BuildInboxItems", "server/InboxBuilder.cs", "server/Program.cs", 1, "server/Program.cs"),
            new("InboxBuilder.AddProjectInboxItems", "server/InboxBuilder.cs", "server/Program.cs", 1, "server/Program.cs"),
            new("CycleSummaryBuilder.BuildCycleSummary", "server/CycleSummaryBuilder.cs", "server/Program.cs", 1, "server/Program.cs"),
            new("MeasurementService.RunMeasureCore", "server/MeasurementService.cs", "server/Program.cs", 2, "server/Program.cs"),
        };

    // 룰 1건의 정의 위치·호출부·stale 호출을 검사한다.
    private static RuleResult CheckRule(List<SourceFile> files, CallRule rule)
    {
        var parts = rule.Symbol.Split('.', 2);
        if (parts.Length != 2) throw new ArgumentException($"symbol must be Qualifier.Method: {rule.Symbol}");
        var qualifier = parts[0];
        var method = parts[1];

        var definitions = files
            .Where(file => file.RelativePath.Equals(rule.DefinitionFile, StringComparison.OrdinalIgnoreCase)
                && MethodDefinitionRegex(method).IsMatch(file.Text))
            .Select(file => file.RelativePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var qualifiedCalls = files
            .SelectMany(file => QualifiedCallRegex(qualifier, method).Matches(file.Text)
                .Select(match => $"{file.RelativePath}:{LineNumber(file.Text, match.Index)}"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var staleCalls = files
            .Where(file => file.RelativePath.Equals(rule.StaleCallFile, StringComparison.OrdinalIgnoreCase))
            .SelectMany(file => UnqualifiedCallRegex(method).Matches(file.Text)
                .Where(match => !IsMethodDefinitionLine(file.Text, match.Index, method))
                .Select(match => $"{file.RelativePath}:{LineNumber(file.Text, match.Index)}"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var ok = definitions.Count == 1
            && qualifiedCalls.Count >= rule.MinQualifiedCalls
            && staleCalls.Count == 0;

        var json = new JsonObject
        {
            ["symbol"] = rule.Symbol,
            ["definitionFile"] = rule.DefinitionFile,
            ["definitionCount"] = definitions.Count,
            ["qualifiedCallCount"] = qualifiedCalls.Count,
            ["minQualifiedCalls"] = rule.MinQualifiedCalls,
            ["staleCallFile"] = rule.StaleCallFile,
            ["staleUnqualifiedCallCount"] = staleCalls.Count,
            ["verdict"] = ok ? "PASS" : "FAIL",
            ["definitions"] = new JsonArray(definitions.Select(v => (JsonNode)v).ToArray()),
            ["qualifiedCalls"] = new JsonArray(qualifiedCalls.Select(v => (JsonNode)v).ToArray()),
            ["staleUnqualifiedCalls"] = new JsonArray(staleCalls.Select(v => (JsonNode)v).ToArray()),
        };
        return new RuleResult(ok, json);
    }

    // 메서드 정의를 느슨하게 찾는다.
    private static Regex MethodDefinitionRegex(string method)
        => new(@"\b[A-Za-z0-9_<>,\[\]\?]+\s+" + Regex.Escape(method) + @"\s*\(",
            RegexOptions.CultureInvariant);

    // qualifier.method(...) 형태의 호출을 찾는다.
    private static Regex QualifiedCallRegex(string qualifier, string method)
        => new(@"\b" + Regex.Escape(qualifier) + @"\s*\.\s*" + Regex.Escape(method) + @"\s*\(",
            RegexOptions.CultureInvariant);

    // qualifier 없는 method(...) 호출을 찾는다.
    private static Regex UnqualifiedCallRegex(string method)
        => new(@"(?<![A-Za-z0-9_\.])" + Regex.Escape(method) + @"\s*\(",
            RegexOptions.CultureInvariant);

    // 같은 줄이 메서드 정의인지 판정한다.
    private static bool IsMethodDefinitionLine(string text, int index, string method)
    {
        var start = text.LastIndexOf('\n', Math.Max(0, index - 1));
        var end = text.IndexOf('\n', index);
        if (start < 0) start = 0;
        if (end < 0) end = text.Length;
        var line = text[start..end];
        return MethodDefinitionRegex(method).IsMatch(line);
    }

    // 0-based index를 1-based line number로 바꾼다.
    private static int LineNumber(string text, int index)
        => text[..Math.Min(index, text.Length)].Count(ch => ch == '\n') + 1;

    private sealed record CallRule(string Symbol, string DefinitionFile, string StaleCallFile, int MinQualifiedCalls, string OldOwnerFile);

    private sealed record SourceFile(string RelativePath, string Text);

    private sealed record RuleResult(bool Ok, JsonObject Json);
}
