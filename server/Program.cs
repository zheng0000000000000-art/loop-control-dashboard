// 정적 대시보드와 로컬 API를 같은 프로세스에서 실행한다.
// 프로젝트 JSON 파일을 직접 읽고 쓰는 라우트를 정의한다.
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    builder.WebHost.UseUrls("http://localhost:5173");
}

var workspaceRoot = Directory.GetParent(builder.Environment.ContentRootPath)?.FullName
    ?? throw new InvalidOperationException("Workspace root was not found.");
var dashboardRoot = Path.Combine(workspaceRoot, "dashboard");
var dataRoot = Path.Combine(dashboardRoot, "data");
var storage = new Storage(dataRoot);
var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
};

storage.ValidateAndRestoreAllProjects();

var app = builder.Build();

app.MapGet("/api/projects/{projectId}/state", (string projectId) => ReadFile(storage, projectId, Storage.StateFile, jsonOptions));
app.MapGet("/api/projects/{projectId}/runlog", (string projectId) => ReadFile(storage, projectId, Storage.RunLogFile, jsonOptions));
app.MapGet("/api/projects/{projectId}/proposal", (string projectId) => ReadFile(storage, projectId, Storage.ProposalFile, jsonOptions));
app.MapGet("/api/projects/{projectId}/reviews", (string projectId) => ReadFile(storage, projectId, Storage.ReviewFile, jsonOptions));
app.MapGet("/api/projects/{projectId}/definition", (string projectId) => ReadFile(storage, projectId, Storage.DefinitionFile, jsonOptions));
app.MapGet("/api/projects/{projectId}/blueprint", (string projectId) => ReadFile(storage, projectId, Storage.BlueprintFile, jsonOptions));
app.MapGet("/api/projects/{projectId}/measurement", (string projectId) => ReadFile(storage, projectId, Storage.MeasurementFile, jsonOptions));

app.MapPost("/api/projects/{projectId}/actions/measure", (string projectId) =>
{
    lock (storage.GetProjectLock(projectId))
    {
        return Measure(storage, projectId, jsonOptions);
    }
});

app.MapPost("/api/projects/{projectId}/actions/approve", async (string projectId, HttpRequest request) =>
{
    var body = await ReadBodyObject(request);
    lock (storage.GetProjectLock(projectId))
    {
        return Approve(storage, projectId, body, jsonOptions);
    }
});

app.MapPost("/api/projects/{projectId}/actions/reject", async (string projectId, HttpRequest request) =>
{
    var body = await ReadBodyObject(request);
    lock (storage.GetProjectLock(projectId))
    {
        return Reject(storage, projectId, body, jsonOptions);
    }
});

app.MapPost("/api/projects/{projectId}/actions/edit-change", async (string projectId, HttpRequest request) =>
{
    var body = await ReadBodyObject(request);
    lock (storage.GetProjectLock(projectId))
    {
        return EditChange(storage, projectId, body, jsonOptions);
    }
});

app.MapPost("/api/projects/{projectId}/actions/acknowledge", async (string projectId, HttpRequest request) =>
{
    var body = await ReadBodyObject(request);
    lock (storage.GetProjectLock(projectId))
    {
        return Acknowledge(storage, projectId, body, jsonOptions);
    }
});

app.UseDefaultFiles(new DefaultFilesOptions
{
    FileProvider = new PhysicalFileProvider(dashboardRoot),
});
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(dashboardRoot),
});

app.Run();

// 프로젝트 파일을 JSON 응답으로 반환한다.
static IResult ReadFile(Storage storage, string projectId, string fileName, JsonSerializerOptions jsonOptions)
{
    try
    {
        return JsonResult(storage.ReadProjectFile(projectId, fileName), jsonOptions);
    }
    catch (FileNotFoundException)
    {
        return ProblemResult(404, "path.not_found", $"{fileName} not found");
    }
    catch (Exception error)
    {
        return ProblemResult(500, "system.read_failed", error.Message);
    }
}

// 측정 공급자를 실행하고 블루프린트 괴리를 판정한다.
static IResult Measure(Storage storage, string projectId, JsonSerializerOptions jsonOptions)
{
    var bundle = storage.ReadBundle(projectId);
    var provider = bundle.Definition["measurementProvider"] as JsonObject;
    var providerId = provider?["id"]?.GetValue<string>();

    if (string.IsNullOrWhiteSpace(providerId))
    {
        return ProblemResult(409, "checklist.provider_missing", "Measurement provider is not configured");
    }

    if (providerId != "dev-pack-checks")
    {
        return ProblemResult(409, "checklist.provider_unknown", $"Measurement provider is not supported: {providerId}");
    }

    storage.CreateRestorePoint(projectId);
    var targetRoot = ResolveMeasurementTargetRoot(storage.ProjectPath(projectId), provider!);
    bundle.Measurement = DevPackMeasures.Measure(targetRoot, providerId, bundle.Blueprint);
    ApplyMeasurementResult(bundle, providerId);
    return Persist(storage, projectId, bundle, jsonOptions);
}

// 측정 결과를 상태, 로그, 제안에 반영한다.
static void ApplyMeasurementResult(ProjectBundle bundle, string providerId)
{
    var checks = EvaluateBlueprintChecks(bundle.Blueprint, bundle.Measurement);
    var violations = checks.Where(check => check.Implemented && !check.Passed).ToList();
    var stages = ResolveMeasurementStages(bundle.Definition);
    var state = bundle.State;
    var runLog = Engine.AppendLog(bundle.RunLog, new JsonObject
    {
        ["event"] = "measure.completed",
        ["params"] = new JsonObject { ["providerId"] = providerId, ["violationCount"] = violations.Count },
        ["level"] = violations.Count > 0 ? "warning" : "info",
        ["producedBy"] = new JsonObject { ["provider"] = "rule-engine", ["model"] = null },
        ["cost"] = RuntimeCost(),
    }, Engine.GetLoopIteration(state));

    state = ApplyMeasurementStagePatch(bundle.Definition, state, checks, violations, stages);
    SetMeasurementDetails(state, stages.DeviationStageId, checks, violations);

    if (violations.Count > 0)
    {
        runLog = Engine.AppendLog(runLog, new JsonObject
        {
            ["event"] = "stage.warning",
            ["params"] = new JsonObject
            {
                ["stage"] = stages.DeviationStageId ?? "",
                ["text"] = "블루프린트 대비 괴리가 감지됐다.",
                ["reasonCode"] = "checklist.blueprint_deviation",
            },
            ["level"] = "warning",
            ["producedBy"] = new JsonObject { ["provider"] = "rule-engine", ["model"] = null },
            ["cost"] = RuntimeCost(),
        }, Engine.GetLoopIteration(state));

        bundle.Proposal = CreateMeasurementProposal(bundle.Proposal, violations);
        runLog = Engine.AppendLog(runLog, new JsonObject
        {
            ["event"] = "proposal.created",
            ["params"] = new JsonObject { ["proposalId"] = bundle.Proposal["id"]?.GetValue<string>() ?? "" },
            ["level"] = "info",
            ["producedBy"] = new JsonObject { ["provider"] = "rule-engine", ["model"] = null },
            ["cost"] = RuntimeCost(),
        }, Engine.GetLoopIteration(state));

        var tier1 = OllamaReviewer.Review(bundle.Definition, bundle.Proposal, bundle.Measurement, AssessRisk(bundle.Definition, bundle.Proposal));
        runLog = Engine.AppendLog(runLog, tier1.LogEntry, Engine.GetLoopIteration(state));

        if (tier1.Report is not null)
        {
            AppendReport(bundle.Reviews, tier1.Report);
        }

        SetTier1Details(state, stages.ReviewStageId, tier1);
    }
    else if (bundle.Proposal["lifecycle"]?.GetValue<string>() == "submitted")
    {
        bundle.Proposal["lifecycle"] = "superseded";
    }

    bundle.State = state;
    bundle.RunLog = runLog;
}

// 측정 결과에 맞는 단계 상태 패치를 적용한다.
static JsonObject ApplyMeasurementStagePatch(JsonObject definition, JsonObject state, List<MetricCheck> checks, List<MetricCheck> violations, MeasurementStages stages)
{
    var stageStatuses = new JsonObject();
    var blockInfo = new JsonObject();

    if (stages.MeasureStageId is not null)
    {
        stageStatuses[stages.MeasureStageId] = "passed";
    }

    if (stages.DeviationStageId is not null)
    {
        stageStatuses[stages.DeviationStageId] = violations.Count > 0 ? "warning" : "passed";
    }

    if (violations.Count > 0)
    {
        if (stages.ReviewStageId is not null)
        {
            stageStatuses[stages.ReviewStageId] = "pending_review";
        }

        if (stages.ApplyStageId is not null)
        {
            stageStatuses[stages.ApplyStageId] = "blocked";
            blockInfo[stages.ApplyStageId] = new JsonObject { ["kind"] = "waiting" };
        }

        return Engine.ApplyStatePatch(definition, state, new JsonObject
        {
            ["currentStage"] = stages.ReviewStageId ?? stages.DeviationStageId ?? state["currentStage"]?.GetValue<string>(),
            ["loopState"] = "running",
            ["stageStatuses"] = stageStatuses,
            ["blockInfo"] = blockInfo,
        });
    }

    if (stages.ReviewStageId is not null && Engine.GetStageStatus(state, stages.ReviewStageId) == "pending_review")
    {
        stageStatuses[stages.ReviewStageId] = "not_started";
    }

    if (stages.ApplyStageId is not null && Engine.GetStageStatus(state, stages.ApplyStageId) == "in_progress")
    {
        stageStatuses[stages.ApplyStageId] = "completed";
    }
    else if (stages.ApplyStageId is not null && Engine.GetStageStatus(state, stages.ApplyStageId) == "blocked")
    {
        stageStatuses[stages.ApplyStageId] = "not_started";
        blockInfo[stages.ApplyStageId] = null;
    }

    return Engine.ApplyStatePatch(definition, state, new JsonObject
    {
        ["currentStage"] = stages.DeviationStageId ?? state["currentStage"]?.GetValue<string>(),
        ["loopState"] = "aligned",
        ["stageStatuses"] = stageStatuses,
        ["blockInfo"] = blockInfo,
    });
}

// 측정 결과를 단계 상세 지표와 이슈로 기록한다.
static void SetMeasurementDetails(JsonObject state, string? stageId, List<MetricCheck> checks, List<MetricCheck> violations)
{
    if (stageId is null)
    {
        return;
    }

    var metrics = new JsonArray();
    foreach (var check in checks)
    {
        metrics.Add(new JsonObject
        {
            ["label"] = check.MetricId,
            ["value"] = check.Implemented
                ? $"{ValueText(check.Value!)} / {check.Expected}"
                : "미구현",
        });
    }

    var issues = new JsonArray();
    foreach (var violation in violations)
    {
        issues.Add($"{violation.MetricId}: 측정값 {ValueText(violation.Value!)}이 기준 {violation.Expected}을 벗어났다. 근거: {EvidenceSummary(violation.Evidence, 3)}");
    }

    state["stageDetails"] ??= new JsonObject();
    state["stageDetails"]!.AsObject()[stageId] = new JsonObject
    {
        ["summary"] = violations.Count == 0
            ? "측정 결과가 블루프린트 기준을 만족한다."
            : $"측정 결과에서 {violations.Count}개 괴리가 감지됐다.",
        ["metrics"] = metrics,
        ["issues"] = issues,
    };
}

// 1층 검토 결과를 검토 단계 상세에 반영한다.
static void SetTier1Details(JsonObject state, string? stageId, Tier1ReviewResult tier1)
{
    if (stageId is null)
    {
        return;
    }

    var issues = new JsonArray();
    var metrics = new JsonArray
    {
        new JsonObject { ["label"] = "1층 검토 판정", ["value"] = tier1.Verdict },
    };

    if (tier1.ReasonCode is not null)
    {
        issues.Add($"1층 검토 강등: {tier1.ReasonCode}");
    }

    if (tier1.Report?["findings"] is JsonArray findings)
    {
        foreach (var finding in findings.OfType<JsonObject>().Where(finding => finding["passed"]?.GetValue<bool>() == false || finding["uncertain"]?.GetValue<bool>() == true))
        {
            issues.Add($"{finding["checkId"]?.GetValue<string>()}: {finding["note"]?.GetValue<string>() ?? finding["comment"]?.GetValue<string>() ?? ""}");
        }
    }

    state["stageDetails"] ??= new JsonObject();
    state["stageDetails"]!.AsObject()[stageId] = new JsonObject
    {
        ["summary"] = tier1.Verdict == "approved"
            ? "1층 체크리스트를 통과했다. 사람 최종 결재가 필요하다."
            : "1층 체크리스트 결과를 사람이 확인해야 한다.",
        ["metrics"] = metrics,
        ["issues"] = issues,
    };
}

// 블루프린트 항목과 측정값을 비교한다.
static List<MetricCheck> EvaluateBlueprintChecks(JsonObject blueprint, JsonObject measurement)
{
    var measurementById = (measurement["metrics"]?.AsArray() ?? new JsonArray())
        .OfType<JsonObject>()
        .Where(metric => !string.IsNullOrWhiteSpace(metric["metricId"]?.GetValue<string>()))
        .ToDictionary(metric => metric["metricId"]!.GetValue<string>(), StringComparer.Ordinal);
    var checks = new List<MetricCheck>();

    foreach (var item in blueprint["items"]?.AsArray().OfType<JsonObject>() ?? [])
    {
        var metricId = item["metricId"]?.GetValue<string>() ?? "";
        measurementById.TryGetValue(metricId, out var metric);
        var value = metric?["value"];
        var evidence = (metric?["evidence"]?.AsArray() ?? new JsonArray())
            .Select(node => node?.GetValue<string>() ?? "")
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();

        if (value is null)
        {
            checks.Add(new MetricCheck(metricId, null, null, false, true, "미구현", evidence.Count > 0 ? evidence : ["미구현"]));
            continue;
        }

        var goal = GetBlueprintGoal(item);
        var expected = GoalText(item);
        var passed = IsMetricWithinBlueprint(item, value);
        checks.Add(new MetricCheck(metricId, Engine.CloneNode(value), goal, true, passed, expected, evidence));
    }

    return checks;
}

// 블루프린트 기준을 대표하는 값을 반환한다.
static JsonNode? GetBlueprintGoal(JsonObject item)
{
    if (item["target"] is not null)
    {
        return Engine.CloneNode(item["target"]!);
    }

    if (item["band"] is not null)
    {
        return Engine.CloneNode(item["band"]!);
    }

    return null;
}

// 측정값이 블루프린트 기준을 만족하는지 확인한다.
static bool IsMetricWithinBlueprint(JsonObject item, JsonNode value)
{
    if (item["target"] is not null)
    {
        if (TryDecimal(value, out var actual) && TryDecimal(item["target"], out var target))
        {
            return actual == target;
        }

        return value.ToJsonString() == item["target"]!.ToJsonString();
    }

    if (item["band"] is JsonArray band && band.Count >= 2 &&
        TryDecimal(value, out var current) &&
        TryDecimal(band[0], out var minimum) &&
        TryDecimal(band[1], out var maximum))
    {
        return current >= minimum && current <= maximum;
    }

    return true;
}

// 측정 위반 목록을 변경 제안으로 변환한다.
static JsonObject CreateMeasurementProposal(JsonObject currentProposal, List<MetricCheck> violations)
{
    var previousId = currentProposal["lifecycle"]?.GetValue<string>() == "submitted"
        ? currentProposal["id"]?.GetValue<string>()
        : null;
    var proposalId = $"proposal-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
    var changes = new JsonArray();

    foreach (var violation in violations)
    {
        changes.Add(new JsonObject
        {
            ["path"] = violation.MetricId,
            ["before"] = violation.Value is null ? null : Engine.CloneNode(violation.Value),
            ["after"] = violation.Goal is null ? violation.Expected : Engine.CloneNode(violation.Goal),
            ["note"] = EvidenceSummary(violation.Evidence, 3),
        });
    }

    return new JsonObject
    {
        ["schemaVersion"] = 2,
        ["id"] = proposalId,
        ["title"] = "블루프린트 괴리 해소 제안",
        ["lifecycle"] = "submitted",
        ["createdBy"] = new JsonObject { ["provider"] = "rule-engine", ["model"] = null },
        ["revisionOf"] = previousId,
        ["summary"] = "측정 결과가 블루프린트 기준을 벗어난 항목을 수정 대상으로 제안한다.",
        ["changes"] = changes,
        ["impact"] = new JsonArray
        {
            new JsonObject { ["label"] = "위반 항목", ["value"] = violations.Count.ToString(CultureInfo.InvariantCulture) },
            new JsonObject { ["label"] = "예상 비용", ["value"] = "$0.00" },
        },
    };
}

// 측정 단계 관련 stage id를 정의 배열에서 계산한다.
static MeasurementStages ResolveMeasurementStages(JsonObject definition)
{
    var stages = definition["stages"]?.AsArray().OfType<JsonObject>().ToList() ?? [];
    var reviewStage = Engine.GetHumanReviewStage(definition);
    var reviewStageId = reviewStage?["id"]?.GetValue<string>();
    var deviationStageId = reviewStage?["gate"]?.AsArray()
        .OfType<JsonObject>()
        .FirstOrDefault(condition => condition["check"]?.GetValue<string>() == "stageStatus")?["stage"]
        ?.GetValue<string>();
    var deviationIndex = stages.FindIndex(stage => stage["id"]?.GetValue<string>() == deviationStageId);
    var measureStageId = deviationIndex > 0 ? stages[deviationIndex - 1]["id"]?.GetValue<string>() : null;
    var applyStageId = reviewStageId is null ? null : Engine.GetNextStage(definition, reviewStageId)?["id"]?.GetValue<string>();
    return new MeasurementStages(measureStageId, deviationStageId, reviewStageId, applyStageId);
}

// measurementProvider의 대상 경로를 실제 디렉터리로 해석한다.
static string ResolveMeasurementTargetRoot(string projectPath, JsonObject provider)
{
    var targetPath = provider["targetPath"]?.GetValue<string>() ?? ".";
    var candidate = Path.GetFullPath(Path.Combine(projectPath, targetPath));

    if (Directory.Exists(Path.Combine(candidate, "server")) && Directory.Exists(Path.Combine(candidate, "dashboard")))
    {
        return candidate;
    }

    var parent = Directory.GetParent(candidate)?.FullName;
    if (parent is not null && Directory.Exists(Path.Combine(parent, "server")) && Directory.Exists(Path.Combine(parent, "dashboard")))
    {
        return parent;
    }

    return candidate;
}

// definition에 측정 공급자 설정이 있는지 확인한다.
static bool HasMeasurementProvider(JsonObject definition)
{
    var provider = definition["measurementProvider"] as JsonObject;
    return !string.IsNullOrWhiteSpace(provider?["id"]?.GetValue<string>());
}

// 블루프린트 기준을 표시 문자열로 만든다.
static string GoalText(JsonObject item)
{
    if (item["target"] is not null)
    {
        return ValueText(item["target"]!);
    }

    if (item["band"] is JsonArray band && band.Count >= 2)
    {
        return $"{ValueText(band[0]!)}~{ValueText(band[1]!)}";
    }

    return "기준 없음";
}

// 근거 목록을 짧은 표시 문자열로 만든다.
static string EvidenceSummary(List<string> evidence, int take)
{
    return evidence.Count == 0 ? "근거 없음" : string.Join("; ", evidence.Take(take));
}

// 승인 액션을 처리하고 결과 파일을 기록한다.
static IResult Approve(Storage storage, string projectId, JsonObject body, JsonSerializerOptions jsonOptions)
{
    var bundle = storage.ReadBundle(projectId);
    var reviewStage = Engine.GetHumanReviewStage(bundle.Definition, bundle.State);
    var proposalId = bundle.Proposal["id"]?.GetValue<string>() ?? "proposal";
    var precheck = ValidateReviewReady(bundle, reviewStage);

    if (precheck is not null)
    {
        return precheck;
    }

    storage.CreateRestorePoint(projectId);
    var stageId = reviewStage!["id"]!.GetValue<string>();
    var risk = AssessRisk(bundle.Definition, bundle.Proposal);
    var report = CreateReviewReport(bundle.Proposal, "approved", "사람 검토로 승인됐다.", risk, MergeFindings(GetEditFindings(bundle.Proposal), body["editedChanges"] as JsonArray));
    var state = Engine.ApplyStageStatus(bundle.Definition, bundle.State, stageId, "approved");
    if (HasMeasurementProvider(bundle.Definition))
    {
        state = Engine.ApplyStatePatch(bundle.Definition, state, new JsonObject
        {
            ["loopIteration"] = Engine.GetLoopIteration(state) + 1,
            ["loopState"] = "running",
        });
    }

    var runLog = Engine.AppendLog(bundle.RunLog, new JsonObject
    {
        ["event"] = "review.approved",
        ["params"] = new JsonObject { ["proposalId"] = proposalId, ["edited"] = Bool(bundle.Proposal["edited"]) },
        ["level"] = "info",
        ["producedBy"] = new JsonObject { ["provider"] = "human", ["model"] = null },
        ["cost"] = RuntimeCost(),
    }, Engine.GetLoopIteration(state));

    var nextStage = Engine.GetNextStage(bundle.Definition, stageId);
    state = nextStage is null
        ? Engine.ApplyStatePatch(bundle.Definition, state, new JsonObject { ["overallStatus"] = "completed" })
        : Engine.ApplyStatePatch(bundle.Definition, state, new JsonObject
        {
            ["currentStage"] = nextStage["id"]!.GetValue<string>(),
            ["stageStatuses"] = new JsonObject { [nextStage["id"]!.GetValue<string>()] = "in_progress" },
            ["overallStatus"] = "in_progress",
        });

    bundle.State = state;
    bundle.RunLog = runLog;
    bundle.Proposal["lifecycle"] = "decided";
    AppendReport(bundle.Reviews, report);
    return Persist(storage, projectId, bundle, jsonOptions);
}

// 거절 액션을 처리하고 결과 파일을 기록한다.
static IResult Reject(Storage storage, string projectId, JsonObject body, JsonSerializerOptions jsonOptions)
{
    var bundle = storage.ReadBundle(projectId);
    var reviewStage = Engine.GetHumanReviewStage(bundle.Definition, bundle.State);
    var proposalId = bundle.Proposal["id"]?.GetValue<string>() ?? "proposal";
    var precheck = ValidateReviewReady(bundle, reviewStage);

    if (precheck is not null)
    {
        return precheck;
    }

    storage.CreateRestorePoint(projectId);
    var stageId = reviewStage!["id"]!.GetValue<string>();
    var reason = body["reason"]?.GetValue<string>()?.Trim();
    reason = string.IsNullOrWhiteSpace(reason) ? "사람 검토로 거절됐다." : reason;
    var risk = AssessRisk(bundle.Definition, bundle.Proposal);
    var report = CreateReviewReport(bundle.Proposal, "rejected", reason, risk, GetEditFindings(bundle.Proposal));
    var state = Engine.ApplyStageStatus(bundle.Definition, bundle.State, stageId, "failed");
    var runLog = Engine.AppendLog(bundle.RunLog, new JsonObject
    {
        ["event"] = "review.rejected",
        ["params"] = new JsonObject { ["proposalId"] = proposalId, ["text"] = reason },
        ["level"] = "warning",
        ["producedBy"] = new JsonObject { ["provider"] = "human", ["model"] = null },
        ["cost"] = RuntimeCost(),
    }, Engine.GetLoopIteration(state));

    bundle.State = state;
    bundle.RunLog = runLog;
    bundle.Proposal["lifecycle"] = "decided";
    AppendReport(bundle.Reviews, report);
    return Persist(storage, projectId, bundle, jsonOptions);
}

// 변경 항목 편집 액션을 처리하고 결과 파일을 기록한다.
static IResult EditChange(Storage storage, string projectId, JsonObject body, JsonSerializerOptions jsonOptions)
{
    if (body.ContainsKey("path") || body.ContainsKey("before"))
    {
        return ProblemResult(400, "path.readonly", "path and before are read-only");
    }

    var bundle = storage.ReadBundle(projectId);
    var changes = bundle.Proposal["changes"]?.AsArray();
    var index = Number(body["changeIndex"], -1);

    if (changes is null || index < 0 || index >= changes.Count)
    {
        return ProblemResult(400, "path.invalid_index", "changeIndex is outside changes");
    }

    storage.CreateRestorePoint(projectId);
    var change = changes[index]!.AsObject();
    var previousAfter = Engine.CloneNode(change["after"] ?? "");
    var nextAfter = Engine.CloneNode(body["after"] ?? "");
    var nextNote = body["note"]?.GetValue<string>() ?? "";
    change["after"] = nextAfter;
    change["note"] = nextNote;
    bundle.Proposal["edited"] = true;
    bundle.Proposal["lastEditedAt"] = DateTimeOffset.Now.ToString("O");
    var finding = new JsonObject
    {
        ["target"] = change["path"]?.GetValue<string>() ?? $"changes[{index}]",
        ["comment"] = $"이후 값을 {ValueText(previousAfter)}에서 {ValueText(change["after"]!)}로 편집했다. 메모: {nextNote}",
        ["severity"] = "info",
    };
    var editFindings = bundle.Proposal["editFindings"]?.AsArray() ?? new JsonArray();
    bundle.Proposal["editFindings"] = editFindings;
    editFindings.Add(Engine.CloneNode(finding));
    var risk = AssessRisk(bundle.Definition, bundle.Proposal);
    AppendReport(bundle.Reviews, CreateReviewReport(bundle.Proposal, "needs_changes", "검토 중 제안 변경 항목이 편집됐다.", risk, new JsonArray(Engine.CloneNode(finding))));
    bundle.RunLog = Engine.AppendLog(bundle.RunLog, new JsonObject
    {
        ["event"] = "proposal.edited",
        ["params"] = new JsonObject
        {
            ["target"] = change["path"]?.GetValue<string>() ?? $"changes[{index}]",
            ["before"] = ValueText(previousAfter),
            ["after"] = ValueText(change["after"]!),
            ["text"] = nextNote,
        },
        ["level"] = "info",
        ["producedBy"] = new JsonObject { ["provider"] = "human", ["model"] = null },
        ["cost"] = RuntimeCost(),
    }, Engine.GetLoopIteration(bundle.State));
    return Persist(storage, projectId, bundle, jsonOptions);
}

// 확인 액션을 처리하고 결과 파일을 기록한다.
static IResult Acknowledge(Storage storage, string projectId, JsonObject body, JsonSerializerOptions jsonOptions)
{
    var type = body["type"]?.GetValue<string>();
    var id = body["id"]?.GetValue<string>();

    if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(id))
    {
        return ProblemResult(400, "review.missing_acknowledgement", "type and id are required");
    }

    if (type == "checkpoint")
    {
        var bundle = storage.ReadBundle(projectId);
        storage.CreateRestorePoint(projectId);
        bundle.State = Guardrails.AcknowledgeCheckpoint(bundle.State, id);
        bundle.RunLog = Engine.AppendLog(bundle.RunLog, new JsonObject
        {
            ["event"] = "checkpoint.acknowledged",
            ["params"] = new JsonObject { ["checkpointId"] = id },
            ["level"] = "info",
            ["producedBy"] = new JsonObject { ["provider"] = "human", ["model"] = null },
            ["cost"] = RuntimeCost(),
        }, Engine.GetLoopIteration(bundle.State));
        return PersistWithoutGuardrail(storage, projectId, bundle, jsonOptions);
    }
    else if (type == "guardrail")
    {
        var bundle = storage.ReadBundle(projectId);
        storage.CreateRestorePoint(projectId);
        bundle.State = Guardrails.AcknowledgeGuardrail(bundle.State, id);
        bundle.RunLog = Engine.AppendLog(bundle.RunLog, new JsonObject
        {
            ["event"] = "guardrail.acknowledged",
            ["params"] = new JsonObject { ["id"] = id },
            ["level"] = "info",
            ["producedBy"] = new JsonObject { ["provider"] = "human", ["model"] = null },
            ["cost"] = RuntimeCost(),
        }, Engine.GetLoopIteration(bundle.State));
        return PersistWithoutGuardrail(storage, projectId, bundle, jsonOptions);
    }
    else
    {
        return ProblemResult(400, "review.invalid_acknowledgement", "type must be checkpoint or guardrail");
    }
}

// 검토 액션 가능 여부를 확인한다.
static IResult? ValidateReviewReady(ProjectBundle bundle, JsonObject? reviewStage)
{
    if (reviewStage is null)
    {
        return ProblemResult(409, "review.no_gate", "No human review stage is configured");
    }

    var stageId = reviewStage["id"]!.GetValue<string>();
    var status = Engine.GetStageStatus(bundle.State, stageId);

    if (status != "pending_review")
    {
        return ProblemResult(409, "review.not_pending", "Review stage is not pending");
    }

    if (bundle.Proposal.Count == 0 || bundle.Proposal["id"] is null)
    {
        return ProblemResult(409, "review.no_proposal", "No proposal is available");
    }

    var gate = Engine.EvaluateGate(bundle.Definition, bundle.State, stageId);

    if (gate.HasGate && !gate.Passed)
    {
        return JsonResult(new JsonObject
        {
            ["reasonCode"] = "review.gate_blocked",
            ["reason"] = "Gate checks are not satisfied",
            ["failedChecks"] = FailedGateChecks(gate),
        }, new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }, 409);
    }

    return null;
}

// 파일을 기록하고 가드레일 판정 후 응답을 만든다.
static IResult Persist(Storage storage, string projectId, ProjectBundle bundle, JsonSerializerOptions jsonOptions)
{
    var guarded = Guardrails.Enforce(bundle.Definition, bundle.State, bundle.RunLog);
    bundle.State = guarded.State;
    bundle.RunLog = guarded.RunLog;
    storage.WriteBundle(projectId, bundle);
    storage.SaveLoopSnapshot(projectId, bundle);
    return BundleResult(bundle, jsonOptions);
}

// 파일을 기록하고 추가 판정 없이 응답을 만든다.
static IResult PersistWithoutGuardrail(Storage storage, string projectId, ProjectBundle bundle, JsonSerializerOptions jsonOptions)
{
    storage.WriteBundle(projectId, bundle);
    storage.SaveLoopSnapshot(projectId, bundle);
    return BundleResult(bundle, jsonOptions);
}

// 프로젝트 묶음 응답을 만든다.
static IResult BundleResult(ProjectBundle bundle, JsonSerializerOptions jsonOptions)
{
    return JsonResult(new JsonObject
    {
        ["state"] = Engine.CloneNode(bundle.State),
        ["runLog"] = Engine.CloneNode(bundle.RunLog),
        ["proposal"] = Engine.CloneNode(bundle.Proposal),
        ["reviewReport"] = Engine.CloneNode(bundle.Reviews),
        ["measurement"] = Engine.CloneNode(bundle.Measurement),
    }, jsonOptions);
}

// 검토 리포트 객체를 만든다.
static JsonObject CreateReviewReport(JsonObject proposal, string verdict, string reason, string risk, JsonArray findings)
{
    return new JsonObject
    {
        ["id"] = $"review-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
        ["proposalId"] = proposal["id"]?.GetValue<string>() ?? "",
        ["verdict"] = verdict,
        ["reviewer"] = new JsonObject { ["type"] = "human", ["provider"] = "human", ["model"] = null },
        ["riskAssessed"] = risk,
        ["findings"] = Engine.CloneNode(findings),
        ["reason"] = reason,
        ["createdAt"] = DateTimeOffset.Now.ToString("O"),
    };
}

// 검토 리포트를 목록에 추가한다.
static void AppendReport(JsonObject reviews, JsonObject report)
{
    var reports = reviews["reports"]?.AsArray() ?? new JsonArray();
    reviews["schemaVersion"] = 2;
    reviews["reports"] = reports;
    reports.Add(report);
}

// 제안 편집 finding 목록을 반환한다.
static JsonArray GetEditFindings(JsonObject proposal)
{
    return proposal["editFindings"] is JsonArray findings
        ? new JsonArray(findings.Where(item => item is not null).Select(item => Engine.CloneNode(item!)).ToArray())
        : new JsonArray();
}

// 요청 finding 목록을 합친다.
static JsonArray MergeFindings(JsonArray first, JsonArray? second)
{
    var merged = new JsonArray(first.Where(item => item is not null).Select(item => Engine.CloneNode(item!)).ToArray());

    if (second is not null)
    {
        foreach (var item in second)
        {
            merged.Add(item is null ? null : Engine.CloneNode(item));
        }
    }

    return merged;
}

// 구조화된 변경 항목으로 위험도를 산정한다.
static string AssessRisk(JsonObject definition, JsonObject proposal)
{
    var metrics = ProposalMetrics(proposal);
    var rules = definition["reviewPolicy"]?.AsObject()["riskRules"]?.AsArray() ?? new JsonArray();
    var assessed = "high";

    foreach (var rule in rules.OfType<JsonObject>())
    {
        if (rule["if"] is not null && EvaluateRiskExpression(rule["if"]!.GetValue<string>(), metrics))
        {
            return NormalizeRisk(rule["then"]?.GetValue<string>()) ?? "high";
        }

        if (rule["default"] is not null)
        {
            assessed = NormalizeRisk(rule["default"]?.GetValue<string>()) ?? "high";
        }
    }

    return assessed;
}

// 제안 변경 수와 최대 증감률을 계산한다.
static Dictionary<string, decimal> ProposalMetrics(JsonObject proposal)
{
    var changes = proposal["changes"]?.AsArray() ?? new JsonArray();
    var maxDelta = 0m;

    foreach (var change in changes.OfType<JsonObject>())
    {
        if (change["before"] is null || change["after"] is null)
        {
            continue;
        }

        if (decimal.TryParse(change["before"]!.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var before) &&
            decimal.TryParse(change["after"]!.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var after))
        {
            var delta = before == 0 ? (after == 0 ? 0 : 100) : Math.Abs((after - before) / before) * 100;
            maxDelta = Math.Max(maxDelta, delta);
        }
    }

    return new Dictionary<string, decimal>
    {
        ["changeCount"] = changes.Count,
        ["maxValueDeltaPercent"] = maxDelta,
    };
}

// 위험도 규칙 표현식을 평가한다.
static bool EvaluateRiskExpression(string expression, Dictionary<string, decimal> metrics)
{
    return expression.Split("&&", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).All(part =>
    {
        var match = Regex.Match(part, @"^(changeCount|maxValueDeltaPercent)\s*(<=|>=|<|>|===|==)\s*(-?\d+(?:\.\d+)?)$");

        if (!match.Success)
        {
            return false;
        }

        var left = metrics[match.Groups[1].Value];
        var op = match.Groups[2].Value;
        var right = decimal.Parse(match.Groups[3].Value, NumberStyles.Number, CultureInfo.InvariantCulture);
        return op switch
        {
            "<=" => left <= right,
            ">=" => left >= right,
            "<" => left < right,
            ">" => left > right,
            _ => left == right,
        };
    });
}

// 위험도 값을 허용 enum으로 정규화한다.
static string? NormalizeRisk(string? value)
{
    var normalized = value?.ToLowerInvariant();
    return normalized is "low" or "medium" or "high" ? normalized : null;
}

// 실패한 게이트 조건 목록을 만든다.
static JsonArray FailedGateChecks(GateEvaluation gate)
{
    var failed = new JsonArray();

    foreach (var check in gate.Checks.Where(check => !check.Passed))
    {
        failed.Add(new JsonObject
        {
            ["stage"] = check.Condition["stage"]?.GetValue<string>() ?? "",
            ["actual"] = check.Actual,
            ["mustBe"] = Engine.CloneNode(check.Condition["mustBe"] ?? new JsonArray()),
        });
    }

    return failed;
}

// 요청 본문을 JSON 객체로 읽는다.
static async Task<JsonObject> ReadBodyObject(HttpRequest request)
{
    if (request.ContentLength is null or 0)
    {
        return new JsonObject();
    }

    var body = await JsonNode.ParseAsync(request.Body);
    return body as JsonObject ?? new JsonObject();
}

// JSON 응답을 만든다.
static IResult JsonResult(JsonNode node, JsonSerializerOptions jsonOptions, int statusCode = 200)
{
    return Results.Content(node.ToJsonString(jsonOptions), "application/json", Encoding.UTF8, statusCode);
}

// 오류 응답을 만든다.
static IResult ProblemResult(int statusCode, string reasonCode, string reason)
{
    return Results.Json(new JsonObject { ["reasonCode"] = reasonCode, ["reason"] = reason }, statusCode: statusCode);
}

// 런타임 비용 0 객체를 만든다.
static JsonObject RuntimeCost()
{
    return new JsonObject
    {
        ["inputTokens"] = 0,
        ["outputTokens"] = 0,
        ["estimatedUSD"] = 0,
        ["subscriptionCalls"] = 0,
        ["role"] = "runtime",
    };
}

// 노드에서 bool 값을 읽는다.
static bool Bool(JsonNode? node)
{
    return node is not null && bool.TryParse(node.ToString(), out var value) && value;
}

// 노드에서 정수 값을 읽는다.
static int Number(JsonNode? node, int fallback)
{
    return node is not null && int.TryParse(node.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;
}

// 노드에서 decimal 값을 읽는다.
static bool TryDecimal(JsonNode? node, out decimal value)
{
    value = 0;
    return node is not null && decimal.TryParse(node.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out value);
}

// 표시용 값 문자열을 만든다.
static string ValueText(JsonNode node)
{
    return node is JsonValue ? node.ToString() : node.ToJsonString();
}

public sealed record MeasurementStages(string? MeasureStageId, string? DeviationStageId, string? ReviewStageId, string? ApplyStageId);

public sealed record MetricCheck(string MetricId, JsonNode? Value, JsonNode? Goal, bool Implemented, bool Passed, string Expected, List<string> Evidence);
