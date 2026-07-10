// 인박스 항목 목록을 구성하는 순수 정적 빌더.
using System.Globalization;
using System.Text.Json.Nodes;

internal static class InboxBuilder
{
    // 등록된 프로젝트를 훑어 사람 행동 대기 목록을 만든다.
    internal static JsonArray BuildInboxItems(Storage storage, NtfyOptions ntfy)
    {
        var items = new JsonArray();
        var projects = storage.ReadProjects()["projects"]?.AsArray() ?? new JsonArray();

        foreach (var project in projects.OfType<JsonObject>())
        {
            var projectId = project["id"]?.GetValue<string>() ?? "";
            var projectName = project["name"]?.GetValue<string>() ?? projectId;

            if (string.IsNullOrWhiteSpace(projectId))
            {
                continue;
            }

            try
            {
                AddProjectInboxItems(storage, projectId, projectName, items, ntfy);
            }
            catch (Exception error)
            {
                Console.Error.WriteLine($"[inbox] skipped {projectId}: {error.Message}");
            }
        }

        return items;
    }

    // 한 프로젝트의 결재·확인·악화 대기 항목을 추가한다.
    internal static void AddProjectInboxItems(Storage storage, string projectId, string projectName, JsonArray items, NtfyOptions ntfy)
    {
        var state = storage.ReadProjectFile(projectId, Storage.StateFile).AsObject();
        var proposal = storage.ReadProjectFile(projectId, Storage.ProposalFile).AsObject();
        var runLog = storage.ReadProjectFile(projectId, Storage.RunLogFile).AsObject();
        var reviewStage = Engine.GetHumanReviewStage(storage.ReadProjectFile(projectId, Storage.DefinitionFile).AsObject(), state);
        var proposalId = proposal["id"]?.GetValue<string>() ?? "";
        var isReviewPending = reviewStage is not null && Engine.GetStageStatus(state, reviewStage["id"]!.GetValue<string>()) == "pending_review";

        if (proposal["lifecycle"]?.GetValue<string>() == "submitted" && isReviewPending)
        {
            var waitingSince = FindProposalCreatedAt(runLog, proposalId) ?? state["lastUpdated"]?.GetValue<string>() ?? DateTimeOffset.Now.ToString("O");
            var item = new JsonObject
            {
                ["projectId"] = projectId,
                ["projectName"] = projectName,
                ["kind"] = "approval",
                ["proposalId"] = proposalId,
                ["title"] = proposal["title"]?.GetValue<string>() ?? "제안",
                ["waitingSince"] = waitingSince,
                ["summary"] = SummarizeProposal(proposal),
                ["assignableTo"] = "human",
            };
            items.Add(item);
            if (DateTimeOffset.TryParse(waitingSince, CultureInfo.InvariantCulture, DateTimeStyles.None, out var since))
            {
                Notifier.NotifyPendingReminder(ntfy, $"{projectId}:{proposalId}", projectName, item["title"]!.GetValue<string>(), DateTimeOffset.Now - since);
            }
        }

        var loopState = state["loopState"]?.GetValue<string>() ?? "running";
        if (loopState == "paused" || loopState == "halted")
        {
            var reason = loopState == "paused" ? state["pausedBy"] as JsonObject : state["haltedBy"] as JsonObject;
            items.Add(new JsonObject
            {
                ["projectId"] = projectId,
                ["projectName"] = projectName,
                ["kind"] = loopState == "paused" ? "checkpoint" : "guardrail",
                ["title"] = loopState == "paused" ? "체크포인트 확인 필요" : "가드레일 확인 필요",
                ["waitingSince"] = state["lastUpdated"]?.GetValue<string>() ?? DateTimeOffset.Now.ToString("O"),
                ["summary"] = reason?["checkpointId"]?.GetValue<string>() ?? reason?["type"]?.GetValue<string>() ?? loopState,
                ["assignableTo"] = "human",
            });
        }

        foreach (var track in state["suspendedTracks"]?.AsArray().OfType<JsonObject>() ?? [])
        {
            items.Add(new JsonObject
            {
                ["projectId"] = projectId,
                ["projectName"] = projectName,
                ["kind"] = "regression",
                ["title"] = "악화 확인 필요",
                ["waitingSince"] = track["createdAt"]?.GetValue<string>() ?? state["lastUpdated"]?.GetValue<string>() ?? DateTimeOffset.Now.ToString("O"),
                ["summary"] = track["metricId"]?.GetValue<string>() ?? "regression",
                ["assignableTo"] = "human",
            });
        }
    }

    // proposal.created 로그에서 제안 생성 시각을 찾는다.
    internal static string? FindProposalCreatedAt(JsonObject runLog, string proposalId)
    {
        return (runLog["entries"]?.AsArray() ?? new JsonArray())
            .OfType<JsonObject>()
            .Where(entry => entry["event"]?.GetValue<string>() == "proposal.created" &&
                entry["params"]?.AsObject()["proposalId"]?.GetValue<string>() == proposalId)
            .OrderByDescending(entry => entry["createdAt"]?.GetValue<string>(), StringComparer.Ordinal)
            .Select(entry => entry["createdAt"]?.GetValue<string>())
            .FirstOrDefault();
    }

    // 제안의 핵심 변경과 예측 완주율을 짧게 요약한다.
    internal static string SummarizeProposal(JsonObject proposal)
    {
        var changes = proposal["changes"]?.AsArray().OfType<JsonObject>().Take(2)
            .Select(change => $"{change["path"]?.GetValue<string>()}: {ValueTextOrNone(change["before"])}→{ValueTextOrNone(change["after"])}")
            .ToList() ?? [];
        var completion = proposal["predictedMetrics"]?.AsArray().OfType<JsonObject>()
            .FirstOrDefault(metric => metric["metricId"]?.GetValue<string>() == "completionRate");

        if (completion is not null)
        {
            changes.Add($"completionRate {ValueTextOrNone(completion["before"])}→{ValueTextOrNone(completion["after"])}");
        }

        return changes.Count == 0 ? proposal["summary"]?.GetValue<string>() ?? "" : string.Join(", ", changes);
    }

    // 노드 값을 표시 문자열로 만든다(SummarizeProposal 전용 복사본, Program.cs 로컬 함수 접근 불가).
    private static string ValueText(JsonNode node) =>
        node is System.Text.Json.Nodes.JsonValue ? node.ToString() : node.ToJsonString();

    // 값이 없을 수 있는 노드를 표시용 문자열로 만든다.
    private static string ValueTextOrNone(JsonNode? node) =>
        node is null ? "없음" : ValueText(node);
}
