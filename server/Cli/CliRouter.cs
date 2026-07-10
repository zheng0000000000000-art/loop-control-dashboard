// CLI 명령을 분기하고 CLI 전용 헬퍼를 모은 라우터.
// TryEscalateInsufficientRefeedback는 Program.cs 로컬 함수이므로 위임으로 주입한다.
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class CliRouter
{
    internal delegate bool TryEscalateDelegate(
        ref Tier1ReviewResult tier1, ref JsonObject runLog, JsonObject state);

    // Program.cs 로컬 함수를 주입받는 위임자.
    internal static TryEscalateDelegate EscalateRefeedback = null!;

    // CLI 명령을 분기한다. 해당 명령이 없으면 null을 반환해 웹 서버로 진행한다.
    internal static int? TryRun(string[] args)
    {
        var cliCommand = args.Length > 0 ? args[0].TrimStart('-') : "";

        if (string.Equals(cliCommand, "snapshot-behavior", StringComparison.OrdinalIgnoreCase))
            return BehaviorSnapshotCli.Snapshot();

        if (string.Equals(cliCommand, "verify-behavior", StringComparison.OrdinalIgnoreCase))
            return BehaviorSnapshotCli.Verify();

        if (args.Length > 0 && string.Equals(args[0], "dispatch-executor", StringComparison.OrdinalIgnoreCase))
            return DispatchExecutorCli.Run(args);

        if (args.Length > 0 && string.Equals(args[0], "measure", StringComparison.OrdinalIgnoreCase))
            return RunMeasureCli(args);

        if (args.Length > 0 && string.Equals(args[0], "simtest", StringComparison.OrdinalIgnoreCase))
            return RunSimTestCli(args);

        if (args.Length > 0 && string.Equals(args[0], "simtune", StringComparison.OrdinalIgnoreCase))
            return RunSimTuneCli(args);

        if (args.Length > 0 && string.Equals(args[0], "refeedbacktest", StringComparison.OrdinalIgnoreCase))
            return RunRefeedbackTestCli();

        if (args.Length > 0 && string.Equals(args[0], "tier2test", StringComparison.OrdinalIgnoreCase))
            return Tier2ApproverTestCli.Run(args);

        return null;
    }

    // 서버를 띄우지 않고 측정을 실행하는 CLI 진입점. 위반 0=0, 위반 존재=1, 실행 오류=2를 반환한다.
    private static int RunMeasureCli(string[] args)
    {
        var cliJsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
        {
            Console.Error.WriteLine(CliError("사용법: measure <projectId>").ToJsonString(cliJsonOptions));
            return 2;
        }

        var projectId = args[1];

        try
        {
            var workspaceRoot = Directory.GetParent(Directory.GetCurrentDirectory())?.FullName
                ?? throw new InvalidOperationException("Workspace root was not found.");
            var dataRoot = Path.Combine(workspaceRoot, "dashboard", "data");
            var storage = new Storage(dataRoot);
            storage.ValidateAndRestoreAllProjects();
            var cliNtfy = new NtfyOptions(false, "", "", 24);

            MeasureOutcome outcome;
            lock (storage.GetProjectLock(projectId))
            {
                outcome = MeasurementService.RunMeasureCore(storage, projectId, cliJsonOptions, cliNtfy);
            }

            if (outcome.Problem is not null || outcome.Bundle is null)
            {
                Console.Error.WriteLine(CliError($"measurement failed for {projectId}").ToJsonString(cliJsonOptions));
                return 2;
            }

            Console.WriteLine(BuildCliSummary(projectId, outcome).ToJsonString(cliJsonOptions));
            return outcome.ViolationCount > 0 ? 1 : 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(CliError(error.Message).ToJsonString(cliJsonOptions));
            return 2;
        }
    }

    // CLI 측정 결과 요약을 한 줄 JSON으로 만든다.
    private static JsonObject BuildCliSummary(string projectId, MeasureOutcome outcome)
    {
        var bundle = outcome.Bundle!;
        return new JsonObject
        {
            ["projectId"] = projectId,
            ["violationCount"] = outcome.ViolationCount,
            ["proposalId"] = bundle.Proposal["id"]?.GetValue<string>(),
            ["proposalLifecycle"] = bundle.Proposal["lifecycle"]?.GetValue<string>(),
            ["createdBy"] = bundle.Proposal["createdBy"]?.DeepClone(),
            ["currentStage"] = bundle.State["currentStage"]?.GetValue<string>(),
            ["overallStatus"] = bundle.State["overallStatus"]?.GetValue<string>(),
        };
    }

    // CLI 오류를 한 줄 JSON으로 만든다.
    private static JsonObject CliError(string message)
    {
        return new JsonObject { ["error"] = message };
    }

    // 게임 시뮬레이터를 두 번 실행해 같은 시드가 같은 결과를 내는지 확인하는 CLI. 재현 실패·데이터 없음=2, 정상=0.
    private static int RunSimTestCli(string[] args)
    {
        var cliJsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        var projectId = args.Length > 1 && !string.IsNullOrWhiteSpace(args[1]) ? args[1] : "ruined-lab";
        var seed = args.Length > 2 && int.TryParse(args[2], out var customSeed) ? customSeed : 42;
        const int runs = 500;

        try
        {
            var workspaceRoot = Directory.GetParent(Directory.GetCurrentDirectory())?.FullName
                ?? throw new InvalidOperationException("Workspace root was not found.");
            var dataRoot = Path.Combine(workspaceRoot, "dashboard", "data");
            var storage = new Storage(dataRoot);
            var gameDataPath = Path.Combine(storage.ProjectPath(projectId), "game-data.json");

            if (!File.Exists(gameDataPath))
            {
                Console.Error.WriteLine(CliError($"game-data.json not found for {projectId}").ToJsonString(cliJsonOptions));
                return 2;
            }

            var gameData = JsonNode.Parse(File.ReadAllText(gameDataPath))!.AsObject();
            var first = GameSimulator.RunSimulation(gameData, seed, runs);
            var second = GameSimulator.RunSimulation(gameData, seed, runs);
            var reproducible = SimResultsEqual(first, second);

            var summary = new JsonObject
            {
                ["projectId"] = projectId,
                ["seed"] = seed,
                ["runs"] = runs,
                ["reproducible"] = reproducible,
                ["completionRate"] = first.CompletionRate,
                ["roomReachRates"] = new JsonArray(first.RoomReachRates.Select(value => (JsonNode)value).ToArray()),
                ["roomDeathRates"] = new JsonArray(first.RoomDeathRates.Select(value => (JsonNode)value).ToArray()),
                ["avgHpPerRoom"] = new JsonArray(first.AvgHpPerRoom.Select(value => (JsonNode)value).ToArray()),
                ["rewardPerRunMean"] = first.RewardPerRunMean,
                ["rewardPerRunStdDev"] = first.RewardPerRunStdDev,
            };

            Console.WriteLine(summary.ToJsonString(cliJsonOptions));
            return reproducible ? 0 : 2;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(CliError(error.Message).ToJsonString(cliJsonOptions));
            return 2;
        }
    }

    // 두 SimResult가 완전히 동일한지 비교한다(재현성 게이트).
    private static bool SimResultsEqual(SimResult first, SimResult second)
    {
        return first.CompletionRate.Equals(second.CompletionRate) &&
            first.RoomReachRates.SequenceEqual(second.RoomReachRates) &&
            first.RoomDeathRates.SequenceEqual(second.RoomDeathRates) &&
            first.AvgHpPerRoom.SequenceEqual(second.AvgHpPerRoom) &&
            first.RewardPerRunMean.Equals(second.RewardPerRunMean) &&
            first.RewardPerRunStdDev.Equals(second.RewardPerRunStdDev) &&
            first.AverageProgressedRooms.Equals(second.AverageProgressedRooms);
    }

    // 레버 범위 안에서 밸런스 탐색을 실행하는 CLI. 측정·제안 파일은 건드리지 않는 순수 실험용이다.
    private static int RunSimTuneCli(string[] args)
    {
        var cliJsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        var projectId = args.Length > 1 && !string.IsNullOrWhiteSpace(args[1]) ? args[1] : "ruined-lab";

        try
        {
            var workspaceRoot = Directory.GetParent(Directory.GetCurrentDirectory())?.FullName
                ?? throw new InvalidOperationException("Workspace root was not found.");
            var dataRoot = Path.Combine(workspaceRoot, "dashboard", "data");
            var storage = new Storage(dataRoot);
            var projectPath = storage.ProjectPath(projectId);
            var gameDataPath = Path.Combine(projectPath, "game-data.json");

            if (!File.Exists(gameDataPath))
            {
                Console.Error.WriteLine(CliError($"game-data.json not found for {projectId}").ToJsonString(cliJsonOptions));
                return 2;
            }

            var gameData = JsonNode.Parse(File.ReadAllText(gameDataPath))!.AsObject();
            var definition = storage.ReadProjectFile(projectId, Storage.DefinitionFile).AsObject();
            var blueprint = storage.ReadProjectFile(projectId, Storage.BlueprintFile).AsObject();
            var seed = Number(definition["measurementProvider"]?.AsObject()["seed"], 42);

            var tuning = BalanceTuner.Search(gameData, blueprint, definition, seed, message => Console.WriteLine(message));

            var summary = new JsonObject
            {
                ["projectId"] = projectId,
                ["seed"] = seed,
                ["candidatesUsed"] = tuning.CandidatesUsed,
                ["reachedBand"] = tuning.ReachedBand,
                ["baselineDistance"] = tuning.BaselineDistance,
                ["finalDistance"] = tuning.FinalDistance,
                ["baselineProgressedRooms"] = tuning.BaselineProgressedRooms,
                ["finalProgressedRooms"] = tuning.FinalProgressedRooms,
                ["restartAttempts"] = tuning.RestartAttempts,
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
                ["residualViolations"] = new JsonArray(tuning.ResidualViolations.Select(text => (JsonNode)text).ToArray()),
            };

            Console.WriteLine(summary.ToJsonString(cliJsonOptions));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(CliError(error.Message).ToJsonString(cliJsonOptions));
            return 2;
        }
    }

    // 재지시 정보 부족 가드를 서버 파일 쓰기 없이 검증하는 CLI다.
    private static int RunRefeedbackTestCli()
    {
        var cliJsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        var report = new JsonObject
        {
            ["verdict"] = "needs_changes",
            ["findings"] = new JsonArray
            {
                new JsonObject
                {
                    ["target"] = "mock-target",
                    ["checkId"] = "mock-check",
                    ["passed"] = false,
                    ["note"] = "",
                },
            },
        };
        var tier1 = new Tier1ReviewResult(report, new JsonObject(), "needs_changes", null);
        var runLog = new JsonObject { ["schemaVersion"] = 3, ["entries"] = new JsonArray() };
        var state = new JsonObject { ["loopIteration"] = 0 };
        var escalated = EscalateRefeedback(ref tier1, ref runLog, state);
        var result = new JsonObject
        {
            ["escalated"] = escalated,
            ["verdict"] = tier1.Verdict,
            ["reasonCode"] = tier1.ReasonCode,
            ["reportVerdict"] = report["verdict"]?.GetValue<string>() ?? "",
            ["event"] = runLog["entries"]?.AsArray().LastOrDefault()?.AsObject()["event"]?.GetValue<string>() ?? "",
            ["checkIds"] = Engine.CloneNode(runLog["entries"]?.AsArray().LastOrDefault()?.AsObject()["params"]?.AsObject()["checkIds"] ?? new JsonArray()),
        };

        Console.WriteLine(result.ToJsonString(cliJsonOptions));
        return escalated && tier1.Verdict == "uncertain" ? 0 : 1;
    }

    // 노드에서 정수 값을 읽는다(RunSimTuneCli 전용 복사본, Program.cs 로컬 함수에 접근 불가).
    private static int Number(JsonNode? node, int fallback)
    {
        return node is not null && int.TryParse(node.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    }
}
