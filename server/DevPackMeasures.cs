// 개발 팩 측정 공급자의 규칙 기반 검사를 수행한다.
// 측정 결과를 measurement.json 계약에 맞는 JSON으로 만든다.
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

public static class DevPackMeasures
{
    private const string ProviderVersion = "1";

    private static readonly string[] ForbiddenWords =
    [
        "game",
        "balance",
        "roguelike",
        "ruined",
        "lab",
        "unity",
        "room",
        "reward",
        "boss",
        "damage",
        "design",
        "gameData",
        "schemaValidation",
        "balanceValidation",
        "patchApproval",
        "unityExport",
    ];

    // 블루프린트 항목 목록에 맞춰 측정 결과를 생성한다.
    public static JsonObject Measure(string targetRoot, string providerId, JsonObject blueprint)
    {
        var fullRoot = Path.GetFullPath(targetRoot);
        var metrics = new JsonArray();

        foreach (var item in blueprint["items"]?.AsArray().OfType<JsonObject>() ?? [])
        {
            var metricId = item["metricId"]?.GetValue<string>() ?? "";
            metrics.Add(MeasureMetric(fullRoot, metricId));
        }

        return new JsonObject
        {
            ["schemaVersion"] = 2,
            ["providerId"] = providerId,
            ["providerVersion"] = ProviderVersion,
            ["measuredAt"] = DateTimeOffset.Now.ToString("O"),
            ["metrics"] = metrics,
        };
    }

    // 단일 지표 ID에 해당하는 검사 결과를 반환한다.
    private static JsonObject MeasureMetric(string root, string metricId)
    {
        return metricId switch
        {
            "domainWordsInEngine" => CountDomainWordsInEngine(root),
            "functionsWithoutComment" => CountFunctionsWithoutComment(root),
            "directiveAcceptanceCriteria" => CountDirectiveAcceptanceCriteria(root),
            "koPoliteEndings" => CountKoPoliteEndings(root),
            "verdictInProposalFile" => CountVerdictInProposalFiles(root),
            "devRoleInRuntimeLogs" => CountDevRoleInRuntimeLogs(root),
            "schemaVersionMissing" => CountSchemaVersionMissing(root),
            _ => Metric(metricId, (JsonNode?)null, ["미구현"]),
        };
    }

    // 엔진 파일에 남은 도메인 단어 등장 횟수를 센다.
    private static JsonObject CountDomainWordsInEngine(string root)
    {
        var count = 0;
        var evidence = new List<string>();
        var files = new[]
        {
            Path.Combine(root, "server", "Engine.cs"),
            Path.Combine(root, "dashboard", "engine.js"),
        };

        foreach (var file in files)
        {
            if (!File.Exists(file))
            {
                evidence.Add($"{RelativePath(root, file)} 없음");
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index += 1)
            {
                var lineCount = ForbiddenWords.Sum(word =>
                    Regex.Matches(lines[index], Regex.Escape(word), RegexOptions.IgnoreCase).Count);

                if (lineCount <= 0)
                {
                    continue;
                }

                count += lineCount;
                AddEvidence(evidence, $"{RelativePath(root, file)}:{index + 1}");
            }
        }

        return Metric("domainWordsInEngine", count, evidence);
    }

    // 코드 파일에서 바로 위 줄 주석이 없는 함수 선언을 센다.
    private static JsonObject CountFunctionsWithoutComment(string root)
    {
        var count = 0;
        var evidence = new List<string>();
        var files = EnumerateCodeFiles(root).ToList();
        var csMethodPattern = new Regex(@"^\s*(public|private|protected|internal)\s+(?:static\s+)?(?:async\s+)?[\w<>\[\],.?]+\s+\w+\s*\(", RegexOptions.Compiled);
        var jsFunctionPattern = new Regex(@"^\s*(?:export\s+)?function\s+\w+\s*\(", RegexOptions.Compiled);

        foreach (var file in files)
        {
            var isCs = file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
            var pattern = isCs ? csMethodPattern : jsFunctionPattern;
            var lines = File.ReadAllLines(file);

            for (var index = 0; index < lines.Length; index += 1)
            {
                if (!pattern.IsMatch(lines[index]))
                {
                    continue;
                }

                var previous = index > 0 ? lines[index - 1].TrimStart() : "";
                if (previous.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                count += 1;
                AddEvidence(evidence, $"{RelativePath(root, file)}:{index + 1}");
            }
        }

        return Metric("functionsWithoutComment", count, evidence);
    }

    // 최신 지시서의 검수 기준 목록 항목 수를 센다.
    private static JsonObject CountDirectiveAcceptanceCriteria(string root)
    {
        var directiveDirectory = Path.Combine(root, "docs", "directives");

        if (!Directory.Exists(directiveDirectory))
        {
            return Metric("directiveAcceptanceCriteria", 0, ["docs/directives 폴더 없음"]);
        }

        var latest = Directory.EnumerateFiles(directiveDirectory, "*.md")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (latest is null)
        {
            return Metric("directiveAcceptanceCriteria", 0, ["docs/directives 지시서 없음"]);
        }

        var count = CountChecklistItemsInSection(File.ReadAllLines(latest), "검수 기준");
        return Metric("directiveAcceptanceCriteria", count, [$"{RelativePath(root, latest)}"]);
    }

    // 한국어 언어 파일에 남은 해요체 또는 합니다체 종결을 센다.
    private static JsonObject CountKoPoliteEndings(string root)
    {
        var file = Path.Combine(root, "dashboard", "data", "lang", "ko.json");
        var count = 0;
        var evidence = new List<string>();
        var pattern = new Regex(@"(?<!필)요\.|(?<!필)요""|습니다", RegexOptions.Compiled);

        if (!File.Exists(file))
        {
            return Metric("koPoliteEndings", 0, ["dashboard/data/lang/ko.json 없음"]);
        }

        var lines = File.ReadAllLines(file);
        for (var index = 0; index < lines.Length; index += 1)
        {
            var matches = pattern.Matches(lines[index]).Count;
            if (matches <= 0)
            {
                continue;
            }

            count += matches;
            AddEvidence(evidence, $"{RelativePath(root, file)}:{index + 1}");
        }

        return Metric("koPoliteEndings", count, evidence);
    }

    // 제안 파일에 판정 키가 저장된 횟수를 센다.
    private static JsonObject CountVerdictInProposalFiles(string root)
    {
        var count = 0;
        var evidence = new List<string>();
        var dataRoot = Path.Combine(root, "dashboard", "data");

        foreach (var file in EnumerateDataFiles(dataRoot, "patch-proposal.json"))
        {
            var node = ReadJsonOrNull(file);
            var fileCount = CountProperty(node, "verdict");
            count += fileCount;

            if (fileCount > 0)
            {
                AddEvidence(evidence, $"{RelativePath(root, file)}");
            }
        }

        return Metric("verdictInProposalFile", count, evidence);
    }

    // 런타임 로그에 개발 역할로 기록된 사람 또는 서버 실행을 센다.
    private static JsonObject CountDevRoleInRuntimeLogs(string root)
    {
        var count = 0;
        var evidence = new List<string>();
        var dataRoot = Path.Combine(root, "dashboard", "data");

        foreach (var file in EnumerateDataFiles(dataRoot, "run-log.json"))
        {
            var entries = ReadJsonOrNull(file)?["entries"]?.AsArray() ?? new JsonArray();
            for (var index = 0; index < entries.Count; index += 1)
            {
                var entry = entries[index]?.AsObject();
                var provider = entry?["producedBy"]?.AsObject()["provider"]?.GetValue<string>() ?? "";
                var role = entry?["cost"]?.AsObject()["role"]?.GetValue<string>() ?? "runtime";

                if (role != "dev" || !IsRuntimeProvider(provider))
                {
                    continue;
                }

                count += 1;
                AddEvidence(evidence, $"{RelativePath(root, file)}:entries[{index}]");
            }
        }

        return Metric("devRoleInRuntimeLogs", count, evidence);
    }

    // 데이터 JSON 중 schemaVersion이 없는 파일 수를 센다.
    private static JsonObject CountSchemaVersionMissing(string root)
    {
        var count = 0;
        var evidence = new List<string>();
        var dataRoot = Path.Combine(root, "dashboard", "data");

        if (!Directory.Exists(dataRoot))
        {
            return Metric("schemaVersionMissing", 0, ["dashboard/data 없음"]);
        }

        foreach (var file in Directory.EnumerateFiles(dataRoot, "*.json", SearchOption.AllDirectories).Where(IsTrackedDataJson))
        {
            var node = ReadJsonOrNull(file);
            if (node is JsonObject obj && obj.ContainsKey("schemaVersion"))
            {
                continue;
            }

            count += 1;
            AddEvidence(evidence, RelativePath(root, file));
        }

        return Metric("schemaVersionMissing", count, evidence);
    }

    // 검수 기준 섹션 안의 목록 항목 수를 계산한다.
    private static int CountChecklistItemsInSection(string[] lines, string title)
    {
        var inSection = false;
        var sectionLevel = 0;
        var count = 0;

        foreach (var line in lines)
        {
            var heading = Regex.Match(line, @"^(#+)\s+(.+)$");
            if (heading.Success)
            {
                var level = heading.Groups[1].Value.Length;
                var headingTitle = heading.Groups[2].Value.Trim();

                if (inSection && level <= sectionLevel)
                {
                    break;
                }

                if (headingTitle.Contains(title, StringComparison.Ordinal))
                {
                    inSection = true;
                    sectionLevel = level;
                }

                continue;
            }

            if (inSection && Regex.IsMatch(line, @"^\s*(?:[-*]\s+|\d+[\.)]\s+)"))
            {
                count += 1;
            }
        }

        return count;
    }

    // 검사 대상 코드 파일 목록을 반환한다.
    private static IEnumerable<string> EnumerateCodeFiles(string root)
    {
        return Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(file => (file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                            file.EndsWith(".js", StringComparison.OrdinalIgnoreCase)) &&
                           !IsGeneratedOrRuntimePath(file));
    }

    // 데이터 폴더에서 특정 이름의 JSON 파일을 찾는다.
    private static IEnumerable<string> EnumerateDataFiles(string dataRoot, string fileName)
    {
        return Directory.Exists(dataRoot)
            ? Directory.EnumerateFiles(dataRoot, fileName, SearchOption.AllDirectories).Where(file => !IsGeneratedOrRuntimePath(file))
            : [];
    }

    // 런타임 실행자로 취급할 provider인지 확인한다.
    private static bool IsRuntimeProvider(string provider)
    {
        var normalized = provider.ToLowerInvariant();
        return normalized is "human" or "storage" or "guardrails" or "checkpoints" or "workflow-engine" or "rule-engine" or "local-server" or "local-dashboard" ||
            normalized.Contains("server", StringComparison.Ordinal);
    }

    // 측정 결과 JSON 객체를 만든다.
    private static JsonObject Metric(string metricId, int? value, IEnumerable<string> evidence)
    {
        return Metric(metricId, value is null ? null : JsonValue.Create(value.Value), evidence);
    }

    // 측정 결과 JSON 객체를 만든다.
    private static JsonObject Metric(string metricId, JsonNode? value, IEnumerable<string> evidence)
    {
        var evidenceArray = new JsonArray();
        foreach (var item in evidence.Take(20))
        {
            evidenceArray.Add(item);
        }

        return new JsonObject
        {
            ["metricId"] = metricId,
            ["value"] = value is null ? null : CloneNode(value),
            ["evidence"] = evidenceArray,
        };
    }

    // JSON 노드를 복사한다.
    private static JsonNode CloneNode(JsonNode value)
    {
        return JsonNode.Parse(value.ToJsonString())!;
    }

    // JSON 파일을 읽고 실패하면 null을 반환한다.
    private static JsonNode? ReadJsonOrNull(string file)
    {
        try
        {
            using var stream = File.OpenRead(file);
            return JsonNode.Parse(stream);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    // JSON 트리에서 특정 속성 이름의 등장 횟수를 센다.
    private static int CountProperty(JsonNode? node, string propertyName)
    {
        if (node is JsonObject obj)
        {
            return obj.Sum(pair => (pair.Key == propertyName ? 1 : 0) + CountProperty(pair.Value, propertyName));
        }

        if (node is JsonArray array)
        {
            return array.Sum(item => CountProperty(item, propertyName));
        }

        return 0;
    }

    // 근거 목록에 최대 개수까지만 항목을 추가한다.
    private static void AddEvidence(List<string> evidence, string item)
    {
        if (evidence.Count < 20)
        {
            evidence.Add(item);
        }
    }

    // 저장소 루트 기준 상대 경로를 만든다.
    private static string RelativePath(string root, string file)
    {
        return Path.GetRelativePath(root, file).Replace('\\', '/');
    }

    // 런타임 산출물 경로인지 확인한다.
    private static bool IsGeneratedOrRuntimePath(string file)
    {
        var normalized = file.Replace('\\', '/');
        return normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/.git/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/.vs/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/history/", StringComparison.OrdinalIgnoreCase);
    }

    // 측정 대상 데이터 JSON인지 확인한다.
    private static bool IsTrackedDataJson(string file)
    {
        var normalized = file.Replace('\\', '/');
        return !IsGeneratedOrRuntimePath(file) &&
            !normalized.Contains("/data/lang/", StringComparison.OrdinalIgnoreCase) &&
            !normalized.EndsWith("/data/projects.json", StringComparison.OrdinalIgnoreCase);
    }
}
