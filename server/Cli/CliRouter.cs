// CLI 紐낅졊??遺꾧린?섍퀬 CLI ?꾩슜 ?ы띁瑜?紐⑥? ?쇱슦??
// TryEscalateInsufficientRefeedback??Program.cs 濡쒖뺄 ?⑥닔?대?濡??꾩엫?쇰줈 二쇱엯?쒕떎.
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class CliRouter
{
    internal delegate bool TryEscalateDelegate(
        ref Tier1ReviewResult tier1, ref JsonObject runLog, JsonObject state);

    // Program.cs 濡쒖뺄 ?⑥닔瑜?二쇱엯諛쏅뒗 ?꾩엫??
    internal static TryEscalateDelegate EscalateRefeedback = null!;

    // CLI 紐낅졊??遺꾧린?쒕떎. ?대떦 紐낅졊???놁쑝硫?null??諛섑솚?????쒕쾭濡?吏꾪뻾?쒕떎.
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

        if (args.Length > 0 && string.Equals(args[0], "e2e-usage", StringComparison.OrdinalIgnoreCase))
            return E2EUsageCli.Run(args);
        if (args.Length > 0 && string.Equals(args[0], "gate-clean", StringComparison.OrdinalIgnoreCase))
            return GateCleanCli.Run(args);

        if (args.Length > 0 && string.Equals(args[0], "hs-scan", StringComparison.OrdinalIgnoreCase))
            return HsScanCli.Run(args);

        if (args.Length > 0 && string.Equals(args[0], "claim-check", StringComparison.OrdinalIgnoreCase))
            return ClaimCheckCli.Run(args);

        if (args.Length > 0 && string.Equals(args[0], "doc-integrity", StringComparison.OrdinalIgnoreCase))
            return DocIntegrityCli.Run(args);

        return null;
    }

    // ?쒕쾭瑜??꾩슦吏 ?딄퀬 痢≪젙???ㅽ뻾?섎뒗 CLI 吏꾩엯?? ?꾨컲 0=0, ?꾨컲 議댁옱=1, ?ㅽ뻾 ?ㅻ쪟=2瑜?諛섑솚?쒕떎.
    private static int RunMeasureCli(string[] args)
    {
        var cliJsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
        {
            Console.Error.WriteLine(CliError("?ъ슜踰? measure <projectId>").ToJsonString(cliJsonOptions));
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

    // CLI 痢≪젙 寃곌낵 ?붿빟????以?JSON?쇰줈 留뚮뱺??
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

    // CLI ?ㅻ쪟瑜???以?JSON?쇰줈 留뚮뱺??
    private static JsonObject CliError(string message)
    {
        return new JsonObject { ["error"] = message };
    }

    // 寃뚯엫 ?쒕??덉씠?곕? ??踰??ㅽ뻾??媛숈? ?쒕뱶媛 媛숈? 寃곌낵瑜??대뒗吏 ?뺤씤?섎뒗 CLI. ?ы쁽 ?ㅽ뙣쨌?곗씠???놁쓬=2, ?뺤긽=0.
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

    // ??SimResult媛 ?꾩쟾???숈씪?쒖? 鍮꾧탳?쒕떎(?ы쁽??寃뚯씠??.
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

    // ?덈쾭 踰붿쐞 ?덉뿉??諛몃윴???먯깋???ㅽ뻾?섎뒗 CLI. 痢≪젙쨌?쒖븞 ?뚯씪? 嫄대뱶由ъ? ?딅뒗 ?쒖닔 ?ㅽ뿕?⑹씠??
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

    // ?ъ????뺣낫 遺議?媛?쒕? ?쒕쾭 ?뚯씪 ?곌린 ?놁씠 寃利앺븯??CLI??
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

    // ?몃뱶?먯꽌 ?뺤닔 媛믪쓣 ?쎈뒗??RunSimTuneCli ?꾩슜 蹂듭궗蹂? Program.cs 濡쒖뺄 ?⑥닔???묎렐 遺덇?).
    private static int Number(JsonNode? node, int fallback)
    {
        return node is not null && int.TryParse(node.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    }
}
