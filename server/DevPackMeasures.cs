// 개발 팩 측정 공급자의 규칙 기반 검사를 수행한다.
// 측정 결과를 measurement.json 계약에 맞는 JSON으로 만든다.
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

public static class DevPackMeasures
{
    private const string ProviderVersion = "1";

    private static readonly HashSet<string> AllowedFontFamilies = new(StringComparer.OrdinalIgnoreCase)
    {
        "Inter", "ui-sans-serif", "system-ui", "-apple-system", "BlinkMacSystemFont", "Segoe UI", "sans-serif",
    };

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
            "hardcodedColors" => CountHardcodedColors(root),
            "inlineStyles" => CountInlineStyles(root),
            "smallTouchTargets" => CountSmallTouchTargets(root),
            "newFontFamilies" => CountNewFontFamilies(root),
            "skillsWithoutVersion" => CountSkillsWithoutVersion(root),
            "skillDomainViolations" => CountSkillDomainViolations(root),
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

    // style.css 밖에서 하드코딩된 색상 리터럴(hex·rgb) 개수를 센다.
    private static JsonObject CountHardcodedColors(string root)
    {
        var count = 0;
        var evidence = new List<string>();
        var pattern = new Regex(@"#[0-9a-fA-F]{3,8}\b|rgba?\(\s*\d", RegexOptions.Compiled);

        foreach (var file in EnumerateStyleScanFiles(root))
        {
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
        }

        return Metric("hardcodedColors", count, evidence);
    }

    // HTML·JS의 인라인 style 속성 또는 style 문자열 조립 개수를 센다.
    private static JsonObject CountInlineStyles(string root)
    {
        var count = 0;
        var evidence = new List<string>();
        var pattern = new Regex(@"\bstyle\s*=\s*[""']|\.style\.\w+\s*=|[""']style[""']\s*:", RegexOptions.Compiled);

        foreach (var file in EnumerateStyleScanFiles(root))
        {
            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index += 1)
            {
                if (!pattern.IsMatch(lines[index]))
                {
                    continue;
                }

                count += 1;
                AddEvidence(evidence, $"{RelativePath(root, file)}:{index + 1}");
            }
        }

        return Metric("inlineStyles", count, evidence);
    }

    // style.css에서 버튼류 셀렉터의 44px 미만 min-height 선언 개수를 센다(정적 검사 한계 내 근사).
    private static JsonObject CountSmallTouchTargets(string root)
    {
        var file = Path.Combine(root, "dashboard", "style.css");

        if (!File.Exists(file))
        {
            return Metric("smallTouchTargets", 0, ["dashboard/style.css 없음"]);
        }

        var count = 0;
        var evidence = new List<string>();
        var lines = File.ReadAllLines(file);
        var selectorBuffer = new List<string>();
        var isButtonRule = false;

        for (var index = 0; index < lines.Length; index += 1)
        {
            var line = lines[index];

            if (line.Contains('{'))
            {
                selectorBuffer.Add(line[..line.IndexOf('{')]);
                isButtonRule = Regex.IsMatch(string.Join(" ", selectorBuffer), "button", RegexOptions.IgnoreCase);
                selectorBuffer.Clear();
            }
            else if (!line.Contains('}'))
            {
                selectorBuffer.Add(line);
            }

            if (line.Contains('}'))
            {
                isButtonRule = false;
                selectorBuffer.Clear();
                continue;
            }

            if (!isButtonRule)
            {
                continue;
            }

            var match = Regex.Match(line, @"min-height:\s*(\d+(?:\.\d+)?)px");
            if (match.Success && decimal.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) < 44)
            {
                count += 1;
                AddEvidence(evidence, $"{RelativePath(root, file)}:{index + 1}");
            }
        }

        return Metric("smallTouchTargets", count, evidence);
    }

    // style.css의 font-family 선언 중 기준 목록 외 폰트 개수를 센다.
    private static JsonObject CountNewFontFamilies(string root)
    {
        var file = Path.Combine(root, "dashboard", "style.css");

        if (!File.Exists(file))
        {
            return Metric("newFontFamilies", 0, ["dashboard/style.css 없음"]);
        }

        var text = File.ReadAllText(file);
        var count = 0;
        var evidence = new List<string>();

        foreach (Match match in Regex.Matches(text, @"font-family:\s*([^;]+);", RegexOptions.Singleline))
        {
            var lineNumber = text[..match.Index].Count(ch => ch == '\n') + 1;
            var fonts = match.Groups[1].Value
                .Split(',')
                .Select(font => font.Replace("\n", " ").Trim().Trim('"', '\''))
                .Where(font => font.Length > 0);

            foreach (var font in fonts)
            {
                if (AllowedFontFamilies.Contains(font))
                {
                    continue;
                }

                count += 1;
                AddEvidence(evidence, $"{RelativePath(root, file)}:{lineNumber} ({font})");
            }
        }

        return Metric("newFontFamilies", count, evidence);
    }

    // skills 문서 중 '버전:' 줄이 없는 파일 수를 센다.
    private static JsonObject CountSkillsWithoutVersion(string root)
    {
        var skillsDirectory = Path.Combine(root, "skills");

        if (!Directory.Exists(skillsDirectory))
        {
            return Metric("skillsWithoutVersion", 0, ["skills 폴더 없음"]);
        }

        var count = 0;
        var evidence = new List<string>();

        foreach (var file in Directory.EnumerateFiles(skillsDirectory, "*.md", SearchOption.AllDirectories))
        {
            var hasVersionLine = File.ReadAllLines(file).Any(line => line.Contains("버전:", StringComparison.Ordinal));

            if (hasVersionLine)
            {
                continue;
            }

            count += 1;
            AddEvidence(evidence, RelativePath(root, file));
        }

        return Metric("skillsWithoutVersion", count, evidence);
    }

    // verification 문서의 참조 스킬이 변경 경로와 맞지 않는 횟수를 센다.
    private static JsonObject CountSkillDomainViolations(string root)
    {
        var verificationDirectory = Path.Combine(root, "docs", "verification");
        var skills = ReadSkillRoutingRules(root);
        var count = 0;
        var evidence = new List<string>();

        if (!Directory.Exists(verificationDirectory))
        {
            return Metric("skillDomainViolations", 0, ["docs/verification 없음"]);
        }

        foreach (var file in Directory.EnumerateFiles(verificationDirectory, "*.md", SearchOption.TopDirectoryOnly))
        {
            var lines = File.ReadAllLines(file);
            var referencedSkills = ExtractSectionItems(lines, "참조한 스킬");

            if (referencedSkills.Count == 0)
            {
                continue;
            }

            var changedPaths = ExtractSectionItems(lines, "변경 경로");
            foreach (var reference in referencedSkills)
            {
                var skill = ResolveSkillReference(reference, skills);

                if (skill is null || skill.Domain == "common" || skill.Triggers.Contains("항상", StringComparer.Ordinal))
                {
                    continue;
                }

                if (changedPaths.Any(path => SkillMatchesPath(skill, path)))
                {
                    continue;
                }

                count += 1;
                AddEvidence(evidence, $"{RelativePath(root, file)} -> {skill.Path}");
            }
        }

        return Metric("skillDomainViolations", count, evidence);
    }

    // skills 문서에서 도메인과 트리거 정보를 읽는다.
    private static List<SkillRoutingRule> ReadSkillRoutingRules(string root)
    {
        var skillsDirectory = Path.Combine(root, "skills");

        if (!Directory.Exists(skillsDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(skillsDirectory, "*.md", SearchOption.AllDirectories)
            .Select(file =>
            {
                var lines = File.ReadAllLines(file);
                var versionLine = lines.FirstOrDefault(line => line.Contains("버전:", StringComparison.Ordinal)) ?? "";
                var domain = ExtractHeaderPart(versionLine, "도메인") ?? "common";
                var triggerText = ExtractHeaderPart(versionLine, "트리거") ?? "";
                var triggers = triggerText.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
                return new SkillRoutingRule(RelativePath(root, file), Path.GetFileName(file), domain, triggers);
            })
            .ToList();
    }

    // 스킬 참조 문자열을 실제 스킬 규칙으로 해석한다.
    private static SkillRoutingRule? ResolveSkillReference(string reference, List<SkillRoutingRule> skills)
    {
        var normalized = NormalizeListItem(reference);
        return skills.FirstOrDefault(skill =>
            normalized.Contains(skill.Path, StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains(skill.FileName, StringComparison.OrdinalIgnoreCase));
    }

    // 스킬 트리거가 변경 경로와 일치하는지 확인한다.
    private static bool SkillMatchesPath(SkillRoutingRule skill, string changedPath)
    {
        var normalizedPath = NormalizeListItem(changedPath).Replace('\\', '/').Trim('/');

        foreach (var trigger in skill.Triggers)
        {
            var normalizedTrigger = trigger.Replace('\\', '/').Trim();

            if (normalizedTrigger.EndsWith("/**", StringComparison.Ordinal))
            {
                var prefix = normalizedTrigger[..^3].TrimEnd('/');
                if (normalizedPath.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                    normalizedPath.StartsWith($"{prefix}/", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                continue;
            }

            var regex = "^" + Regex.Escape(normalizedTrigger).Replace("\\*", "[^/]*") + "$";
            if (Regex.IsMatch(normalizedPath, regex, RegexOptions.IgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // 헤더 줄에서 특정 필드 값을 읽는다.
    private static string? ExtractHeaderPart(string line, string key)
    {
        var match = Regex.Match(line, $@"{Regex.Escape(key)}:\s*([^|]+)");
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    // 마크다운 섹션 안의 목록 항목을 추출한다.
    private static List<string> ExtractSectionItems(string[] lines, string title)
    {
        var inSection = false;
        var sectionLevel = 0;
        var items = new List<string>();

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

            if (!inSection)
            {
                continue;
            }

            var item = Regex.Match(line, @"^\s*(?:[-*]\s+|\d+[\.)]\s+)(.+)$");
            if (item.Success)
            {
                items.Add(NormalizeListItem(item.Groups[1].Value));
            }
        }

        return items;
    }

    // 목록 항목에서 마크다운 장식을 제거한다.
    private static string NormalizeListItem(string text)
    {
        return text.Trim().Trim('`').Replace("\\", "/");
    }

    // 디자인 정적 검사 대상 파일(HTML·JS, style.css 제외)을 반환한다.
    private static IEnumerable<string> EnumerateStyleScanFiles(string root)
    {
        var dashboardRoot = Path.Combine(root, "dashboard");

        if (!Directory.Exists(dashboardRoot))
        {
            return [];
        }

        return Directory.EnumerateFiles(dashboardRoot, "*.*", SearchOption.AllDirectories)
            .Where(file => (file.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
                            file.EndsWith(".js", StringComparison.OrdinalIgnoreCase)) &&
                           !IsGeneratedOrRuntimePath(file));
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

public sealed record SkillRoutingRule(string Path, string FileName, string Domain, List<string> Triggers);
