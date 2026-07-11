// E2E 실사용 시나리오 하네스 — 인프로세스, 상태 변경 없음.
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class E2EUsageCli
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // e2e-usage CLI 진입점. 6개 시나리오를 인프로세스로 실행하고 JSON 결과를 출력한다. failCount>0이면 exit code 2.
    internal static int Run(string[] args)
    {
        var singleProjectId = args.Length > 1 && !string.IsNullOrWhiteSpace(args[1]) ? args[1] : null;

        try
        {
            var workspaceRoot = Directory.GetParent(Directory.GetCurrentDirectory())?.FullName
                ?? throw new InvalidOperationException("Workspace root was not found.");
            var dataRoot = Path.Combine(workspaceRoot, "dashboard", "data");
            var storage = new Storage(dataRoot);
            var ntfy = new NtfyOptions(false, "", "", 24);
            var projectIds = GetProjectIds(storage, singleProjectId);

            var scenarios = new JsonArray
            {
                RunScenario("S1", "프로젝트 열람 정합성", () => S1ProjectLoad(storage, projectIds)),
                RunScenario("S2", "measure 결정론", () => S2MeasureDeterminism(storage, projectIds)),
                RunScenario("S3", "인박스 일관성", () => S3InboxConsistency(storage, projectIds, ntfy)),
                RunScenario("S4", "outbox 조회", () => S4OutboxQuery(workspaceRoot)),
                RunScenario("S5", "엣지 처리", () => S5EdgeHandling(storage)),
                RunScenario("S6", "상태 교차 일관성", () => S6CrossConsistency(storage, projectIds)),
            };

            var failCount = scenarios.OfType<JsonObject>()
                .Count(s => s["result"]?.GetValue<string>() == "fail");

            Console.WriteLine(new JsonObject
            {
                ["scenarios"] = scenarios,
                ["failCount"] = failCount,
            }.ToJsonString(JsonOptions));

            return failCount > 0 ? 2 : 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(new JsonObject
            {
                ["error"] = error.Message,
                ["scenarios"] = new JsonArray(),
                ["failCount"] = -1,
            }.ToJsonString(JsonOptions));
            return 2;
        }
    }

    // 시나리오 함수를 실행하고 결과 JSON 객체를 만든다. 처리되지 않은 예외는 fail로 잡는다.
    private static JsonObject RunScenario(string id, string name, Func<(bool pass, string detail)> scenario)
    {
        try
        {
            var (pass, detail) = scenario();
            return new JsonObject { ["id"] = id, ["name"] = name, ["result"] = pass ? "pass" : "fail", ["detail"] = detail };
        }
        catch (Exception error)
        {
            return new JsonObject { ["id"] = id, ["name"] = name, ["result"] = "fail", ["detail"] = $"exception: {error.GetType().Name}: {error.Message}" };
        }
    }

    // S1: 각 프로젝트 state·measurement·cycle-summary 로드 성공 + schemaVersion + 필수 필드 존재.
    private static (bool pass, string detail) S1ProjectLoad(Storage storage, IReadOnlyList<string> projectIds)
    {
        var issues = new List<string>();

        foreach (var id in projectIds)
        {
            try
            {
                var bundle = storage.ReadBundle(id);

                if (bundle.State["schemaVersion"] is null)
                    issues.Add($"{id}: state.schemaVersion 없음");
                if (bundle.State["loopIteration"] is null)
                    issues.Add($"{id}: state.loopIteration 없음");
                if (bundle.State["currentStage"] is null)
                    issues.Add($"{id}: state.currentStage 없음");
                if (bundle.State["overallStatus"] is null)
                    issues.Add($"{id}: state.overallStatus 없음");
                if (bundle.Measurement["schemaVersion"] is null)
                    issues.Add($"{id}: measurement.schemaVersion 없음");
                if (bundle.Measurement["metrics"] is null)
                    issues.Add($"{id}: measurement.metrics 없음");

                var cycleSummary = CycleSummaryBuilder.BuildCycleSummary(bundle.State, bundle.RunLog, bundle.Proposal);
                if (cycleSummary["schemaVersion"] is null)
                    issues.Add($"{id}: cycle-summary.schemaVersion 없음");
                if (cycleSummary["segments"] is null)
                    issues.Add($"{id}: cycle-summary.segments 없음");
            }
            catch (Exception error)
            {
                issues.Add($"{id}: 로드 실패 — {error.Message}");
            }
        }

        return issues.Count == 0
            ? (true, $"모든 프로젝트 정합성 확인 ({projectIds.Count}개)")
            : (false, string.Join("; ", issues));
    }

    // S2: 같은 프로젝트에서 measure를 2회 실행해 metrics가 동일한지 확인한다.
    private static (bool pass, string detail) S2MeasureDeterminism(Storage storage, IReadOnlyList<string> projectIds)
    {
        var issues = new List<string>();

        foreach (var id in projectIds)
        {
            try
            {
                var bundle = storage.ReadBundle(id);
                var provider = bundle.Definition["measurementProvider"] as JsonObject;
                var providerId = provider?["id"]?.GetValue<string>() ?? "";

                string metrics1, metrics2;

                if (providerId == "dev-pack-checks")
                {
                    var targetRoot = ResolveTargetRoot(storage.ProjectPath(id), provider!);
                    // measuredAt은 호출마다 다르므로 metrics만 비교한다.
                    metrics1 = DevPackMeasures.Measure(targetRoot, providerId, bundle.Blueprint)["metrics"]?.ToJsonString() ?? "";
                    metrics2 = DevPackMeasures.Measure(targetRoot, providerId, bundle.Blueprint)["metrics"]?.ToJsonString() ?? "";
                }
                else if (providerId == "ruined-lab-sim")
                {
                    metrics1 = GameSimulator.Measure(storage.ProjectPath(id), providerId, bundle.Blueprint, provider!)["metrics"]?.ToJsonString() ?? "";
                    metrics2 = GameSimulator.Measure(storage.ProjectPath(id), providerId, bundle.Blueprint, provider!)["metrics"]?.ToJsonString() ?? "";
                }
                else
                {
                    continue;
                }

                if (metrics1 != metrics2)
                    issues.Add($"{id}: 2회 측정 metrics 불일치 — 비결정론");
            }
            catch (Exception error)
            {
                issues.Add($"{id}: measure 실패 — {error.Message}");
            }
        }

        return issues.Count == 0
            ? (true, "measure 결정론 확인")
            : (false, string.Join("; ", issues));
    }

    // S3: 인박스 항목의 assignableTo·kind 규칙 준수, 존재하지 않는 projectId 참조 없음.
    private static (bool pass, string detail) S3InboxConsistency(Storage storage, IReadOnlyList<string> projectIds, NtfyOptions ntfy)
    {
        var issues = new List<string>();
        var projectIdSet = new HashSet<string>(projectIds, StringComparer.OrdinalIgnoreCase);
        var items = InboxBuilder.BuildInboxItems(storage, ntfy);

        foreach (var item in items.OfType<JsonObject>())
        {
            var itemProjectId = item["projectId"]?.GetValue<string>() ?? "";
            var kind = item["kind"]?.GetValue<string>() ?? "";
            var assignableTo = item["assignableTo"]?.GetValue<string>() ?? "";

            if (!projectIdSet.Contains(itemProjectId))
                issues.Add($"항목 projectId '{itemProjectId}'가 projects.json에 없음");
            if (string.IsNullOrWhiteSpace(kind))
                issues.Add($"{itemProjectId}: kind 없음");
            if (string.IsNullOrWhiteSpace(assignableTo))
                issues.Add($"{itemProjectId}/{kind}: assignableTo 없음");
            if (kind == "approval" && assignableTo != "human")
                issues.Add($"{itemProjectId}/approval: assignableTo={assignableTo} (human 기대)");
        }

        return issues.Count == 0
            ? (true, $"인박스 {items.Count}개 항목 일관성 확인")
            : (false, string.Join("; ", issues));
    }

    // S4: import_pending task의 meta·diff 필드 정상, 변경 파일이 files/ 아래에 실존.
    private static (bool pass, string detail) S4OutboxQuery(string workspaceRoot)
    {
        var issues = new List<string>();
        var outboxRoot = Path.Combine(workspaceRoot, "outbox");

        if (!Directory.Exists(outboxRoot))
            return (true, "outbox 디렉터리 없음 — 스킵");

        var checkedCount = 0;

        foreach (var taskDir in Directory.GetDirectories(outboxRoot, "task-*").OrderBy(d => d))
        {
            var metaPath = Path.Combine(taskDir, "meta.json");
            if (!File.Exists(metaPath))
                continue;

            JsonObject? meta;
            try
            {
                meta = JsonNode.Parse(File.ReadAllText(metaPath))?.AsObject();
            }
            catch
            {
                issues.Add($"{Path.GetFileName(taskDir)}: meta.json 파싱 실패");
                continue;
            }

            if (meta is null || meta["status"]?.GetValue<string>() != "import_pending")
                continue;

            checkedCount++;
            var taskId = Path.GetFileName(taskDir);

            if (meta["taskId"] is null) issues.Add($"{taskId}: taskId 없음");
            if (meta["projectId"] is null) issues.Add($"{taskId}: projectId 없음");
            if (meta["changedFiles"] is null) issues.Add($"{taskId}: changedFiles 없음");

            var filesDir = Path.Combine(taskDir, "files");
            foreach (var file in (meta["changedFiles"]?.AsArray() ?? new JsonArray()).OfType<JsonValue>())
            {
                var relative = file.GetValue<string>().Replace('/', Path.DirectorySeparatorChar);
                if (!File.Exists(Path.Combine(filesDir, relative)))
                    issues.Add($"{taskId}: diff 파일 없음 — {file.GetValue<string>()}");
            }
        }

        return issues.Count == 0
            ? (true, $"import_pending task {checkedCount}개 정상 확인")
            : (false, string.Join("; ", issues));
    }

    // S5: 잘못된 입력(없는 projectId·경로 탈출·빈 ID)이 프로세스 크래시가 아니라 잡힌 예외로 처리되는지.
    private static (bool pass, string detail) S5EdgeHandling(Storage storage)
    {
        var passed = new List<string>();
        var issues = new List<string>();

        TestEdge("없는 projectId",
            () => storage.ReadBundle("nonexistent-project-xyz-e2e"),
            passed, issues);

        TestEdge("경로 탈출(path traversal)",
            () => storage.ProjectFilePath("dev-pack", "../../etc/passwd"),
            passed, issues);

        TestEdge("빈 projectId",
            () => storage.ProjectPath(""),
            passed, issues);

        TestEdge("공백 projectId",
            () => storage.ProjectPath("   "),
            passed, issues);

        return issues.Count == 0
            ? (true, $"모든 엣지 입력 정상 처리: {string.Join(", ", passed)}")
            : (false, string.Join("; ", issues));
    }

    // S6: workflow-state ↔ run-log의 loopIteration 모순, proposal 상태 일관성.
    private static (bool pass, string detail) S6CrossConsistency(Storage storage, IReadOnlyList<string> projectIds)
    {
        var issues = new List<string>();

        foreach (var id in projectIds)
        {
            try
            {
                var bundle = storage.ReadBundle(id);
                var stateIteration = Engine.GetLoopIteration(bundle.State);
                var entries = bundle.RunLog["entries"]?.AsArray() ?? new JsonArray();

                foreach (var entry in entries.OfType<JsonObject>())
                {
                    var entryIteration = entry["loopIteration"]?.GetValue<int?>() ?? 0;
                    if (entryIteration > stateIteration)
                    {
                        issues.Add($"{id}: run-log 항목 loopIteration({entryIteration}) > state.loopIteration({stateIteration})");
                        break;
                    }
                }

                var proposalLifecycle = bundle.Proposal["lifecycle"]?.GetValue<string>() ?? "";
                var currentStage = bundle.State["currentStage"]?.GetValue<string>() ?? "";

                if (proposalLifecycle == "submitted" && string.IsNullOrWhiteSpace(currentStage))
                    issues.Add($"{id}: proposal submitted 상태인데 state.currentStage 없음");

                var metrics = bundle.Measurement["metrics"]?.AsArray();
                if (metrics is not null && metrics.Count == 0 &&
                    File.Exists(storage.ProjectFilePath(id, Storage.MeasurementFile)))
                    issues.Add($"{id}: measurement.metrics 배열이 비어있음");
            }
            catch (Exception error)
            {
                issues.Add($"{id}: 읽기 실패 — {error.Message}");
            }
        }

        return issues.Count == 0
            ? (true, "상태 교차 일관성 확인")
            : (false, string.Join("; ", issues));
    }

    // 엣지 입력 테스트 — 예외가 잡히면 passed, 예외 없이 반환되면 issues에 추가한다.
    private static void TestEdge(string label, Action action, List<string> passed, List<string> issues)
    {
        try
        {
            action();
            issues.Add($"{label}: 예외 없이 통과 (거부 기대)");
        }
        catch (Exception error)
        {
            passed.Add($"{label} → {error.GetType().Name}");
        }
    }

    // projects.json에서 프로젝트 ID 목록을 가져온다. singleProjectId가 지정되면 그것만 반환한다.
    private static IReadOnlyList<string> GetProjectIds(Storage storage, string? singleProjectId)
    {
        if (singleProjectId is not null)
            return [singleProjectId];

        return (storage.ReadProjects()["projects"]?.AsArray() ?? new JsonArray())
            .OfType<JsonObject>()
            .Select(p => p["id"]?.GetValue<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .ToList();
    }

    // measurementProvider targetPath를 실제 디렉터리로 변환한다(MeasurementService.ResolveMeasurementTargetRoot 동일 로직).
    private static string ResolveTargetRoot(string projectPath, JsonObject provider)
    {
        var targetPath = provider["targetPath"]?.GetValue<string>() ?? ".";
        var candidate = Path.GetFullPath(Path.Combine(projectPath, targetPath));

        if (Directory.Exists(Path.Combine(candidate, "server")) && Directory.Exists(Path.Combine(candidate, "dashboard")))
            return candidate;

        var parent = Directory.GetParent(candidate)?.FullName;
        if (parent is not null && Directory.Exists(Path.Combine(parent, "server")) && Directory.Exists(Path.Combine(parent, "dashboard")))
            return parent;

        return candidate;
    }
}
