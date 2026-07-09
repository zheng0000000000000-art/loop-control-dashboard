// skills/ 아래 파일의 '트리거:' 목록을 프로젝트 데이터 경로와 비교해 관련 스킬 경로를 찾는다.
// 스킬 파일의 메타데이터 줄만 읽는다 — 도메인 지식은 갖지 않는다.
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

public static class SkillRouter
{
    // 이 프로젝트 데이터 경로(dashboard/data/{projectId}/)와 트리거가 겹치는 스킬 파일 경로 목록을 반환한다.
    public static JsonArray RelevantPaths(string workspaceRoot, string projectId)
    {
        var skillsRoot = Path.Combine(workspaceRoot, "skills");
        var candidatePath = $"dashboard/data/{projectId}/";
        var result = new JsonArray();

        if (!Directory.Exists(skillsRoot))
        {
            return result;
        }

        foreach (var file in Directory.EnumerateFiles(skillsRoot, "*.md", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var relative = Path.GetRelativePath(workspaceRoot, file).Replace('\\', '/');
            var headerLine = File.ReadLines(file).Skip(2).FirstOrDefault() ?? "";
            var triggerMatch = Regex.Match(headerLine, "트리거:\\s*([^|]+)\\|");
            var triggers = triggerMatch.Success
                ? triggerMatch.Groups[1].Value.Split(',').Select(token => token.Trim())
                : [];

            if (triggers.Any(trigger => trigger == "항상" || MatchesTrigger(trigger, candidatePath)))
            {
                result.Add(relative);
            }
        }

        return result;
    }

    // 스킬 트리거 glob 패턴 하나가 후보 경로와 겹치는지 확인한다.
    private static bool MatchesTrigger(string trigger, string candidatePath)
    {
        var basePattern = trigger.EndsWith("/**", StringComparison.Ordinal) ? trigger[..^2] : trigger;
        return candidatePath.StartsWith(basePattern, StringComparison.OrdinalIgnoreCase) ||
            basePattern.StartsWith(candidatePath, StringComparison.OrdinalIgnoreCase);
    }
}
