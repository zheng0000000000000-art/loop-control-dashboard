// 자동 결재 감사 데이터를 인박스 응답용으로 요약한다.
// 최근 AI approver 승인과 최신 실측 일부를 함께 노출한다.
using System.Text.Json.Nodes;

public static class AutoApprovalInbox
{
    // 모든 프로젝트의 최근 자동 결재 감사 항목을 만든다.
    public static JsonArray BuildSummaries(Storage storage)
    {
        var summaries = new JsonArray();
        var projects = storage.ReadProjects()["projects"]?.AsArray() ?? new JsonArray();

        foreach (var project in projects.OfType<JsonObject>())
        {
            AddProjectSummaries(storage, summaries, project);
        }

        return new JsonArray(summaries
            .OfType<JsonObject>()
            .OrderByDescending(item => item["createdAt"]?.GetValue<string>(), StringComparer.Ordinal)
            .Take(5)
            .Select(item => Engine.CloneNode(item))
            .ToArray());
    }

    // 한 프로젝트의 자동 결재 항목을 추가한다.
    private static void AddProjectSummaries(Storage storage, JsonArray summaries, JsonObject project)
    {
        var projectId = project["id"]?.GetValue<string>() ?? "";
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return;
        }

        try
        {
            var projectName = project["name"]?.GetValue<string>() ?? projectId;
            var reviews = storage.ReadProjectFile(projectId, Storage.ReviewFile).AsObject();
            var measurement = storage.ReadProjectFile(projectId, Storage.MeasurementFile).AsObject();

            foreach (var report in (reviews["reports"]?.AsArray() ?? new JsonArray()).OfType<JsonObject>().Where(IsAutoApproval))
            {
                summaries.Add(Summary(projectId, projectName, report, measurement));
            }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"[inbox:auto-approval] skipped {projectId}: {error.Message}");
        }
    }

    // AI 결재자 승인 리포트인지 확인한다.
    private static bool IsAutoApproval(JsonObject report)
    {
        var reviewer = report["reviewer"] as JsonObject;
        return reviewer?["type"]?.GetValue<string>() == "ai" &&
            reviewer?["role"]?.GetValue<string>() == "approver" &&
            report["verdict"]?.GetValue<string>() == "approved";
    }

    // 감사 섹션 항목 하나를 만든다.
    private static JsonObject Summary(string projectId, string projectName, JsonObject report, JsonObject measurement)
    {
        return new JsonObject
        {
            ["projectId"] = projectId,
            ["projectName"] = projectName,
            ["proposalId"] = report["proposalId"]?.GetValue<string>() ?? "",
            ["reason"] = report["reason"]?.GetValue<string>() ?? "",
            ["createdAt"] = report["createdAt"]?.GetValue<string>() ?? "",
            ["riskAssessed"] = report["riskAssessed"]?.GetValue<string>() ?? "",
            ["actual"] = MeasurementSummary(measurement),
        };
    }

    // 감사 섹션에 넣을 최신 실측 요약을 만든다.
    private static JsonArray MeasurementSummary(JsonObject measurement)
    {
        return new JsonArray((measurement["metrics"]?.AsArray() ?? new JsonArray())
            .OfType<JsonObject>()
            .Take(4)
            .Select(metric => (JsonNode)new JsonObject
            {
                ["metricId"] = metric["metricId"]?.GetValue<string>() ?? "",
                ["value"] = metric["value"] is null ? null : Engine.CloneNode(metric["value"]!),
            })
            .ToArray());
    }
}
