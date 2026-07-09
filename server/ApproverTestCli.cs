// 상위 AI 결재자 경로를 임시 데이터 사본에서 검증한다.
// 서버 실제 데이터는 수정하지 않고 정책 분기와 감사 기록을 확인한다.
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

public static class ApproverTestCli
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // 결재자 테스트 CLI를 실행한다.
    public static int Run(string[] args)
    {
        var scenario = args.Length > 1 ? args[1] : "approve";
        var tempRoot = Path.Combine(Path.GetTempPath(), $"loop-approver-test-{Guid.NewGuid():N}");

        try
        {
            var workspaceRoot = FindWorkspaceRoot();
            CopyDirectory(Path.Combine(workspaceRoot, "dashboard", "data"), Path.Combine(tempRoot, "data"));
            var result = RunScenario(scenario, tempRoot);
            Console.WriteLine(result.ToJsonString(JsonOptions));
            return ScenarioPassed(scenario, result) ? 0 : 1;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(new JsonObject { ["error"] = error.Message }.ToJsonString(JsonOptions));
            return 2;
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    // 단일 테스트 시나리오를 실행한다.
    private static JsonObject RunScenario(string scenario, string tempRoot)
    {
        var storage = new Storage(Path.Combine(tempRoot, "data"));
        var projectId = "dev-pack";
        var bundle = storage.ReadBundle(projectId);
        PrepareBundle(bundle, scenario);
        var tier1 = Tier1Result(bundle, scenario);
        var beforeLoop = Engine.GetLoopIteration(bundle.State);
        var handled = ApproverWorkflow.TryRun(bundle, projectId, new NtfyOptions(false, "", "", 24), storage, JsonOptions, tier1, ApplyTestApproval);
        var autoReport = AutoReport(bundle.Reviews);

        return new JsonObject
        {
            ["scenario"] = scenario,
            ["handled"] = handled,
            ["beforeLoop"] = beforeLoop,
            ["afterLoop"] = Engine.GetLoopIteration(bundle.State),
            ["proposalLifecycle"] = bundle.Proposal["lifecycle"]?.GetValue<string>() ?? "",
            ["latestApproverVerdict"] = autoReport?["verdict"]?.GetValue<string>() ?? "",
            ["latestReasonCode"] = LastReasonCode(bundle.RunLog),
            ["subscriptionCalls"] = SumSubscriptionCalls(bundle.RunLog),
            ["reviewerRole"] = autoReport?["reviewer"]?.AsObject()["role"]?.GetValue<string>() ?? "",
            ["autoReportRetainedAfterHumanRollback"] = scenario == "rollback-audit" && SimulateHumanRollbackReport(bundle.Reviews),
        };
    }

    // 테스트용 번들 상태를 만든다.
    private static void PrepareBundle(ProjectBundle bundle, string scenario)
    {
        var reviewStage = Engine.GetHumanReviewStage(bundle.Definition, bundle.State) ?? new JsonObject { ["id"] = "changeReview" };
        var stageId = reviewStage["id"]!.GetValue<string>();
        bundle.Definition["reviewPolicy"]!.AsObject()["approverTier"] = ApproverPolicy(scenario);
        bundle.State = Engine.ApplyStatePatch(bundle.Definition, bundle.State, new JsonObject
        {
            ["loopState"] = "running",
            ["mode"] = scenario == "pacing" ? "degraded" : "normal",
            ["suspendedTracks"] = new JsonArray(),
            ["currentStage"] = stageId,
            ["stageStatuses"] = new JsonObject { [stageId] = "pending_review" },
        });
        bundle.Proposal = Proposal(scenario);
    }

    // 테스트용 결재 정책을 만든다.
    private static JsonObject ApproverPolicy(string scenario)
    {
        return new JsonObject
        {
            ["provider"] = "claude-code",
            ["enabled"] = true,
            ["maxRisk"] = "low",
            ["maxAutoApprovalsPerLoop"] = 3,
            ["requires"] = new JsonArray("tier1_passed", "no_suspended_tracks", "not_meta_change"),
            ["mockDecision"] = new JsonObject { ["decision"] = "approve", ["reason"] = "mock approver accepted low risk proposal" },
            ["mockTimeout"] = scenario == "timeout",
        };
    }

    // 테스트용 제안을 만든다.
    private static JsonObject Proposal(string scenario)
    {
        return new JsonObject
        {
            ["schemaVersion"] = 2,
            ["id"] = $"proposal-approver-test-{scenario}",
            ["title"] = "상위 AI 결재 테스트",
            ["lifecycle"] = "submitted",
            ["createdBy"] = new JsonObject { ["provider"] = "rule-engine", ["model"] = null },
            ["summary"] = "임시 사본에서 상위 AI 결재 경로를 검증한다.",
            ["changes"] = new JsonArray
            {
                new JsonObject
                {
                    ["path"] = "programCsLines",
                    ["before"] = 100,
                    ["after"] = scenario == "medium" ? 130 : 101,
                    ["note"] = "검증용 낮은 위험 변경",
                },
            },
            ["impact"] = new JsonArray(),
        };
    }

    // 테스트용 1층 통과 결과를 만든다.
    private static Tier1ReviewResult Tier1Result(ProjectBundle bundle, string scenario)
    {
        var risk = scenario == "medium" ? "medium" : "low";
        var report = new JsonObject
        {
            ["id"] = "review-tier1-test",
            ["proposalId"] = bundle.Proposal["id"]?.GetValue<string>() ?? "",
            ["verdict"] = "approved",
            ["reviewer"] = new JsonObject { ["type"] = "ai", ["provider"] = "ollama", ["model"] = "tier1-test" },
            ["riskAssessed"] = risk,
            ["findings"] = new JsonArray(),
            ["reason"] = "테스트 1층 통과",
            ["createdAt"] = DateTimeOffset.Now.ToString("O"),
        };
        return new Tier1ReviewResult(report, new JsonObject(), "approved", null);
    }

    // 테스트 승인 적용 경로를 실행한다.
    private static IResult ApplyTestApproval(Storage storage, string projectId, ProjectBundle bundle, JsonObject reviewStage, JsonObject report, string provider, JsonObject cost, JsonSerializerOptions jsonOptions, NtfyOptions ntfy, out ProjectBundle committedBundle)
    {
        var stageId = reviewStage["id"]!.GetValue<string>();
        bundle.State = Engine.ApplyStageStatus(bundle.Definition, bundle.State, stageId, "approved");
        bundle.State = Engine.ApplyStatePatch(bundle.Definition, bundle.State, new JsonObject { ["loopIteration"] = Engine.GetLoopIteration(bundle.State) + 1 });
        bundle.Proposal["lifecycle"] = "decided";
        var reports = bundle.Reviews["reports"]?.AsArray() ?? new JsonArray();
        bundle.Reviews["reports"] = reports;
        reports.Add(report);
        bundle.RunLog = Engine.AppendLog(bundle.RunLog, ApprovedLog(bundle.Proposal["id"]?.GetValue<string>() ?? "", provider, cost), Engine.GetLoopIteration(bundle.State));
        committedBundle = bundle;
        return Results.Json(new JsonObject());
    }

    // 테스트 승인 로그를 만든다.
    private static JsonObject ApprovedLog(string proposalId, string provider, JsonObject cost)
    {
        return new JsonObject
        {
            ["event"] = "review.approved",
            ["params"] = new JsonObject { ["proposalId"] = proposalId, ["edited"] = false },
            ["level"] = "info",
            ["producedBy"] = new JsonObject { ["provider"] = provider, ["model"] = null },
            ["cost"] = Engine.CloneNode(cost),
        };
    }

    // 시나리오 기대 결과를 확인한다.
    private static bool ScenarioPassed(string scenario, JsonObject result)
    {
        return scenario switch
        {
            "approve" => Bool(result["handled"]) && result["proposalLifecycle"]?.GetValue<string>() == "decided" && Number(result["subscriptionCalls"]) >= 1,
            "medium" => !Bool(result["handled"]) && result["latestReasonCode"]?.GetValue<string>() == "approver.risk_too_high",
            "timeout" => !Bool(result["handled"]) && result["latestReasonCode"]?.GetValue<string>() == "approver.call_failed",
            "pacing" => !Bool(result["handled"]) && result["latestReasonCode"]?.GetValue<string>() == "approver.pacing_degraded",
            "rollback-audit" => Bool(result["autoReportRetainedAfterHumanRollback"]),
            _ => false,
        };
    }

    // 테스트용 사람 롤백 리포트를 추가하고 자동 결재 리포트 잔존 여부를 반환한다.
    private static bool SimulateHumanRollbackReport(JsonObject reviews)
    {
        var reports = reviews["reports"]?.AsArray() ?? new JsonArray();
        reviews["reports"] = reports;
        reports.Add(new JsonObject
        {
            ["id"] = "review-human-rollback-test",
            ["proposalId"] = "proposal-rollback-test",
            ["verdict"] = "approved",
            ["reviewer"] = new JsonObject { ["type"] = "human", ["provider"] = "human", ["model"] = null },
            ["riskAssessed"] = "high",
            ["findings"] = new JsonArray(),
            ["reason"] = "사람 롤백 결재 테스트",
            ["createdAt"] = DateTimeOffset.Now.ToString("O"),
        });
        return AutoReport(reviews) is not null;
    }

    // 마지막 자동 결재 리포트를 찾는다.
    private static JsonObject? AutoReport(JsonObject reviews)
    {
        return (reviews["reports"]?.AsArray() ?? new JsonArray())
            .OfType<JsonObject>()
            .LastOrDefault(report => report["reviewer"]?.AsObject()["role"]?.GetValue<string>() == "approver");
    }

    // 마지막 reasonCode를 실행 로그에서 찾는다.
    private static string LastReasonCode(JsonObject runLog)
    {
        return (runLog["entries"]?.AsArray() ?? new JsonArray())
            .OfType<JsonObject>()
            .Reverse()
            .Select(entry => entry["params"]?.AsObject()["reasonCode"]?.GetValue<string>())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
    }

    // 실행 로그의 구독 호출 수를 합산한다.
    private static int SumSubscriptionCalls(JsonObject runLog)
    {
        return (runLog["entries"]?.AsArray() ?? new JsonArray())
            .OfType<JsonObject>()
            .Sum(entry => Number(entry["cost"]?.AsObject()["subscriptionCalls"]));
    }

    // 테스트용 데이터 폴더를 복사한다.
    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            if (!directory.Contains($"{Path.DirectorySeparatorChar}history", StringComparison.OrdinalIgnoreCase))
            {
                CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
            }
        }

        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }
    }

    // 테스트 임시 폴더를 정리한다.
    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // 테스트 임시 폴더 정리 실패는 결과를 바꾸지 않는다.
        }
    }

    // 현재 작업 위치에서 저장소 루트를 찾는다.
    private static string FindWorkspaceRoot()
    {
        var current = Directory.GetCurrentDirectory();
        return string.Equals(Path.GetFileName(current), "server", StringComparison.OrdinalIgnoreCase)
            ? Directory.GetParent(current)!.FullName
            : current;
    }

    // 노드에서 bool 값을 읽는다.
    private static bool Bool(JsonNode? node)
    {
        return node is not null && bool.TryParse(node.ToString(), out var value) && value;
    }

    // 노드에서 정수를 읽는다.
    private static int Number(JsonNode? node)
    {
        return node is not null && int.TryParse(node.ToString(), out var value) ? value : 0;
    }
}
