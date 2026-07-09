// 리팩토링 전후 동작 동일성 확인용 스냅샷 CLI를 제공한다.
// 저장소 데이터를 읽어 deterministic JSON 스냅샷을 만들고 비교한다.
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Globalization;

public static class BehaviorSnapshotCli
{
    private static readonly HashSet<string> StructuralMetricIds = new(StringComparer.Ordinal)
    {
        "programCsLines",
        "appJsLines",
        "maxFunctionLength",
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // 현재 동작 스냅샷을 docs/behavior-snapshot.json에 저장한다.
    public static int Snapshot()
    {
        try
        {
            var root = FindWorkspaceRoot();
            var snapshot = BuildSnapshot(root);
            var outputPath = Path.Combine(root, "docs", "behavior-snapshot.json");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(outputPath, snapshot.ToJsonString(JsonOptions));
            Console.WriteLine(new JsonObject
            {
                ["snapshot"] = RelativePath(root, outputPath),
                ["status"] = "written",
            }.ToJsonString());
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(new JsonObject { ["error"] = error.Message }.ToJsonString());
            return 2;
        }
    }

    // 현재 동작과 저장된 스냅샷을 비교한다.
    public static int Verify()
    {
        try
        {
            var root = FindWorkspaceRoot();
            var snapshotPath = Path.Combine(root, "docs", "behavior-snapshot.json");

            if (!File.Exists(snapshotPath))
            {
                Console.Error.WriteLine(new JsonObject { ["error"] = "docs/behavior-snapshot.json not found" }.ToJsonString());
                return 2;
            }

            var expected = NormalizeBehaviorSnapshot(JsonNode.Parse(File.ReadAllText(snapshotPath))!.AsObject());
            var current = NormalizeBehaviorSnapshot(BuildSnapshot(root));
            var equal = JsonNode.DeepEquals(expected, current);
            Console.WriteLine(new JsonObject
            {
                ["behaviorEqual"] = equal,
                ["snapshot"] = RelativePath(root, snapshotPath),
            }.ToJsonString());
            return equal ? 0 : 1;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(new JsonObject { ["error"] = error.Message }.ToJsonString());
            return 2;
        }
    }

    // 저장소의 현재 동작 요약을 만든다.
    private static JsonObject BuildSnapshot(string root)
    {
        return new JsonObject
        {
            ["schemaVersion"] = 1,
            ["simtune"] = BuildSimTuneSnapshot(root),
            ["measurements"] = BuildMeasurementSnapshots(root),
            ["scenarioReplay"] = BuildScenarioReplaySnapshot(root, "ruined-lab"),
        };
    }

    // 구조 진단 지표를 제외한 동작 스냅샷을 만든다.
    private static JsonObject NormalizeBehaviorSnapshot(JsonObject snapshot)
    {
        var next = Engine.CloneObject(snapshot);
        foreach (var project in next["measurements"]?.AsArray().OfType<JsonObject>() ?? [])
        {
            var metrics = project["metrics"]?.AsArray() ?? new JsonArray();
            var filtered = metrics
                .OfType<JsonObject>()
                .Where(metric => !StructuralMetricIds.Contains(metric["metricId"]?.GetValue<string>() ?? ""))
                .Select(metric => Engine.CloneNode(metric))
                .ToArray();
            project["metrics"] = new JsonArray(filtered);
        }

        return next;
    }

    // ruined-lab 튜닝 탐색 결과를 스냅샷으로 만든다.
    private static JsonObject BuildSimTuneSnapshot(string root)
    {
        var projectPath = Path.Combine(root, "dashboard", "data", "ruined-lab");
        var definition = ReadJsonObject(Path.Combine(projectPath, "workflow-definition.json"));
        var blueprint = ReadJsonObject(Path.Combine(projectPath, "blueprint.json"));
        var gameData = ReadJsonObject(Path.Combine(projectPath, "game-data.json"));
        var seed = Number(definition["measurementProvider"]?.AsObject()["seed"], 42);
        var progress = new List<string>();
        var tuning = BalanceTuner.Search(gameData, blueprint, definition, seed, progress.Add);

        return new JsonObject
        {
            ["projectId"] = "ruined-lab",
            ["seed"] = seed,
            ["reachedBand"] = tuning.ReachedBand,
            ["candidatesUsed"] = tuning.CandidatesUsed,
            ["baselineDistance"] = tuning.BaselineDistance,
            ["finalDistance"] = tuning.FinalDistance,
            ["baselineProgressedRooms"] = tuning.BaselineProgressedRooms,
            ["finalProgressedRooms"] = tuning.FinalProgressedRooms,
            ["changedLevers"] = new JsonArray(tuning.ChangedLevers.Select(change => (JsonNode)new JsonObject
            {
                ["path"] = change.Path,
                ["before"] = change.Before,
                ["after"] = change.After,
            }).ToArray()),
            ["predictedMetrics"] = new JsonArray(tuning.PredictedMetrics.Select(metric => (JsonNode)new JsonObject
            {
                ["metricId"] = metric.MetricId,
                ["before"] = metric.Before,
                ["after"] = metric.After,
                ["band"] = metric.Band,
            }).ToArray()),
            ["residualViolations"] = new JsonArray(tuning.ResidualViolations.Select(item => (JsonNode)item).ToArray()),
            ["trace"] = new JsonArray(progress.Select(item => (JsonNode)item).ToArray()),
        };
    }

    // 모든 프로젝트의 측정 지표를 파일 쓰기 없이 계산한다.
    private static JsonArray BuildMeasurementSnapshots(string root)
    {
        var dataRoot = Path.Combine(root, "dashboard", "data");
        var projects = ReadJsonObject(Path.Combine(dataRoot, "projects.json"))["projects"]?.AsArray() ?? new JsonArray();
        var snapshots = new JsonArray();

        foreach (var project in projects.OfType<JsonObject>())
        {
            var projectId = project["id"]?.GetValue<string>() ?? "";
            var relativePath = project["path"]?.GetValue<string>() ?? "";
            var projectPath = Path.GetFullPath(Path.Combine(root, "dashboard", relativePath));
            var definition = ReadJsonObject(Path.Combine(projectPath, "workflow-definition.json"));
            var blueprint = ReadJsonObject(Path.Combine(projectPath, "blueprint.json"));
            var provider = definition["measurementProvider"] as JsonObject;
            var providerId = provider?["id"]?.GetValue<string>() ?? "";
            var measurement = providerId switch
            {
                "dev-pack-checks" => DevPackMeasures.Measure(Path.GetFullPath(Path.Combine(projectPath, provider?["targetPath"]?.GetValue<string>() ?? ".")), providerId, blueprint),
                "ruined-lab-sim" => GameSimulator.Measure(projectPath, providerId, blueprint, provider ?? new JsonObject()),
                _ => new JsonObject { ["metrics"] = new JsonArray() },
            };

            snapshots.Add(new JsonObject
            {
                ["projectId"] = projectId,
                ["providerId"] = providerId,
                ["metrics"] = NormalizeMetrics(measurement["metrics"]?.AsArray() ?? new JsonArray()),
            });
        }

        return snapshots;
    }

    // scenario.json을 메모리에서 재생하고 요약한다.
    private static JsonObject BuildScenarioReplaySnapshot(string root, string projectId)
    {
        var projectPath = Path.Combine(root, "dashboard", "data", projectId);
        var definition = ReadJsonObject(Path.Combine(projectPath, "workflow-definition.json"));
        var scenario = ReadJsonObject(Path.Combine(projectPath, "scenario.json"));
        var state = ReadJsonObject(Path.Combine(projectPath, "workflow-state.json"));
        var proposal = ReadJsonObject(Path.Combine(projectPath, "patch-proposal.json"));
        var runLog = new JsonObject { ["schemaVersion"] = 3, ["entries"] = new JsonArray() };
        var blockedTransitions = 0;
        var appliedEvents = 0;

        foreach (var scenarioEvent in scenario["events"]?.AsArray().OfType<JsonObject>() ?? [])
        {
            var patch = scenarioEvent["statePatch"] as JsonObject ?? new JsonObject();
            var requestedStage = patch["currentStage"]?.GetValue<string>();

            if (!string.IsNullOrWhiteSpace(requestedStage))
            {
                var gate = Engine.EvaluateGate(definition, state, requestedStage);
                if (gate.HasGate && !gate.Passed)
                {
                    state = Engine.ApplyGateBlockedPatch(definition, state, patch, requestedStage);
                    blockedTransitions += 1;
                    continue;
                }
            }

            state = Engine.ApplyStatePatch(definition, state, patch);
            appliedEvents += 1;

            if (scenarioEvent["proposalPatch"] is JsonObject proposalPatch)
            {
                proposal = MergeObject(proposal, proposalPatch);
            }

            if (scenarioEvent["log"] is JsonObject logEntry)
            {
                runLog = Engine.AppendLog(runLog, logEntry, Engine.GetLoopIteration(state));
            }
        }

        return new JsonObject
        {
            ["projectId"] = projectId,
            ["eventCount"] = scenario["events"]?.AsArray().Count ?? 0,
            ["appliedEvents"] = appliedEvents,
            ["blockedTransitions"] = blockedTransitions,
            ["finalCurrentStage"] = state["currentStage"]?.GetValue<string>() ?? "",
            ["finalLoopState"] = state["loopState"]?.GetValue<string>() ?? "",
            ["finalOverallStatus"] = state["overallStatus"]?.GetValue<string>() ?? "",
            ["finalProposalLifecycle"] = proposal["lifecycle"]?.GetValue<string>() ?? "",
            ["logCount"] = runLog["entries"]?.AsArray().Count ?? 0,
        };
    }

    // metric 배열을 비교 가능한 형태로 정규화한다.
    private static JsonArray NormalizeMetrics(JsonArray metrics)
    {
        return new JsonArray(metrics.OfType<JsonObject>()
            .OrderBy(metric => metric["metricId"]?.GetValue<string>() ?? "")
            .Select(metric => (JsonNode)new JsonObject
            {
                ["metricId"] = metric["metricId"]?.GetValue<string>() ?? "",
                ["value"] = metric["value"] is null ? null : Engine.CloneNode(metric["value"]!),
                ["evidence"] = Engine.CloneNode(metric["evidence"] ?? new JsonArray()),
            }).ToArray());
    }

    // 두 JSON 객체를 얕게 병합한다.
    private static JsonObject MergeObject(JsonObject source, JsonObject patch)
    {
        var next = Engine.CloneObject(source);
        foreach (var (key, value) in patch)
        {
            next[key] = value is null ? null : Engine.CloneNode(value);
        }

        return next;
    }

    // JSON 파일을 객체로 읽는다.
    private static JsonObject ReadJsonObject(string path)
    {
        return JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    }

    // 저장소 루트 디렉터리를 찾는다.
    private static string FindWorkspaceRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "server")) &&
                Directory.Exists(Path.Combine(current.FullName, "dashboard")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Workspace root was not found.");
    }

    // 루트 기준 상대 경로를 만든다.
    private static string RelativePath(string root, string path)
    {
        return Path.GetRelativePath(root, path).Replace('\\', '/');
    }

    // 노드에서 정수 값을 읽는다.
    private static int Number(JsonNode? node, int fallback)
    {
        return node is not null && int.TryParse(node.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    }
}
