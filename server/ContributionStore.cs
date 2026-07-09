// 에이전트·사람의 기여 제출(스킬 초안·체크리스트 제안·장애·blueprint 제안)을 받아 저장한다.
// incident는 사실 기록이라 docs/incidents/에 즉시 쓰고, 나머지는 사람 승급 대기 목록에 넣는다.
using System.Text.Json.Nodes;

public static class ContributionStore
{
    public const string ContributionsFile = "contributions.json";
    private static readonly HashSet<string> ValidKinds = new(StringComparer.Ordinal)
    {
        "skill_draft",
        "checklist_suggestion",
        "incident",
        "blueprint_suggestion",
    };

    // 기여 제출을 검증하고 kind에 따라 저장한다.
    public static JsonObject Submit(Storage storage, string workspaceRoot, JsonObject body)
    {
        var kind = body["kind"]?.GetValue<string>() ?? "";
        var content = body["content"]?.GetValue<string>() ?? "";
        var evidence = body["evidence"]?.GetValue<string>() ?? "";

        if (!ValidKinds.Contains(kind))
        {
            throw new DispatchHttpException(400, "contribution.invalid_kind", $"kind must be one of {string.Join(", ", ValidKinds)}");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new DispatchHttpException(400, "contribution.content_required", "content is required");
        }

        var id = $"contribution-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
        var submittedAt = DateTimeOffset.Now.ToString("O");
        var incidentPath = kind == "incident" ? WriteIncident(workspaceRoot, id, content, evidence, submittedAt) : null;
        var status = kind == "incident" ? "recorded" : "pending";

        var record = new JsonObject
        {
            ["event"] = "contribution.submitted",
            ["params"] = new JsonObject
            {
                ["id"] = id,
                ["kind"] = kind,
                ["content"] = content,
                ["evidence"] = evidence,
                ["status"] = status,
                ["incidentPath"] = incidentPath,
            },
            ["level"] = "info",
            ["producedBy"] = new JsonObject { ["provider"] = "agent", ["model"] = null },
            ["createdAt"] = submittedAt,
        };

        AppendRecord(storage, record);
        return record;
    }

    // 사람 승급 대기 중인 기여(incident 제외)를 인박스 항목으로 반환한다.
    public static void AddPendingInboxItems(Storage storage, JsonArray items)
    {
        var store = ReadStore(storage);
        foreach (var record in store["records"]?.AsArray().OfType<JsonObject>() ?? [])
        {
            var contributionParams = record["params"]?.AsObject();
            if (contributionParams?["status"]?.GetValue<string>() != "pending")
            {
                continue;
            }

            items.Add(new JsonObject
            {
                ["projectId"] = "",
                ["projectName"] = "",
                ["kind"] = "contribution",
                ["contributionId"] = contributionParams["id"]?.GetValue<string>() ?? "",
                ["title"] = $"기여 제안: {contributionParams["kind"]?.GetValue<string>() ?? ""}",
                ["waitingSince"] = record["createdAt"]?.GetValue<string>() ?? DateTimeOffset.Now.ToString("O"),
                ["summary"] = contributionParams["content"]?.GetValue<string>() ?? "",
                ["assignableTo"] = "human",
            });
        }
    }

    // docs/incidents/에 사실 기록 파일을 즉시 만든다.
    private static string WriteIncident(string workspaceRoot, string id, string content, string evidence, string submittedAt)
    {
        var incidentsDirectory = Path.Combine(workspaceRoot, "docs", "incidents");
        Directory.CreateDirectory(incidentsDirectory);
        var datePrefix = DateOnly.FromDateTime(DateTimeOffset.Parse(submittedAt).Date).ToString("yyyy-MM-dd");
        var fileName = $"{datePrefix}-agent-contribution-{id[^6..]}.md";
        var path = Path.Combine(incidentsDirectory, fileName);
        var text = $"""
        # {TitleFrom(content)}

        ## 제출

        기여 ID: {id}
        제출 시각: {submittedAt}

        ## 내용

        {content}

        ## 근거

        {evidence}
        """;
        File.WriteAllText(path, text, new System.Text.UTF8Encoding(false));
        return Path.Combine("docs", "incidents", fileName).Replace('\\', '/');
    }

    // content 첫 줄을 제목 길이로 줄인다.
    private static string TitleFrom(string content)
    {
        var firstLine = content.Split('\n')[0].Trim();
        return firstLine.Length > 80 ? firstLine[..80].TrimEnd() + "…" : firstLine;
    }

    // 전역 contributions.json을 읽는다.
    private static JsonObject ReadStore(Storage storage)
    {
        return storage.ReadGlobalFile(ContributionsFile, () => new JsonObject { ["schemaVersion"] = 2, ["records"] = new JsonArray() }).AsObject();
    }

    // 새 기여 레코드를 contributions.json에 추가한다.
    private static void AppendRecord(Storage storage, JsonObject record)
    {
        var store = ReadStore(storage);
        var records = store["records"]!.AsArray();
        records.Add(record.DeepClone());
        storage.WriteGlobalFile(ContributionsFile, store);
    }
}
