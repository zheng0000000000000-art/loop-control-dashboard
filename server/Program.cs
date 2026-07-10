// 정적 대시보드와 로컬 API를 같은 프로세스에서 실행한다.
// 프로젝트 JSON 파일을 직접 읽고 쓰는 라우트를 정의한다.
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.FileProviders;

var cliCommand = args.Length > 0 ? args[0].TrimStart('-') : "";

if (string.Equals(cliCommand, "snapshot-behavior", StringComparison.OrdinalIgnoreCase))
{
    return BehaviorSnapshotCli.Snapshot();
}

if (string.Equals(cliCommand, "verify-behavior", StringComparison.OrdinalIgnoreCase))
{
    return BehaviorSnapshotCli.Verify();
}

if (args.Length > 0 && string.Equals(args[0], "dispatch-executor", StringComparison.OrdinalIgnoreCase))
{
    return DispatchExecutorCli.Run(args);
}

if (args.Length > 0 && string.Equals(args[0], "measure", StringComparison.OrdinalIgnoreCase))
{
    return RunMeasureCli(args);
}

if (args.Length > 0 && string.Equals(args[0], "simtest", StringComparison.OrdinalIgnoreCase))
{
    return RunSimTestCli(args);
}

if (args.Length > 0 && string.Equals(args[0], "simtune", StringComparison.OrdinalIgnoreCase))
{
    return RunSimTuneCli(args);
}

if (args.Length > 0 && string.Equals(args[0], "refeedbacktest", StringComparison.OrdinalIgnoreCase))
{
    return RunRefeedbackTestCli();
}

if (args.Length > 0 && string.Equals(args[0], "tier2test", StringComparison.OrdinalIgnoreCase))
{
    return Tier2ApproverTestCli.Run(args);
}

var builder = WebApplication.CreateBuilder(args);

if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    var bindUrls = builder.Configuration["BindUrls"];
    builder.WebHost.UseUrls(string.IsNullOrWhiteSpace(bindUrls) ? "http://localhost:5173" : bindUrls);
}

var remoteActionToken = builder.Configuration["RemoteActionToken"];
var ntfyOptions = new NtfyOptions(
    builder.Configuration.GetValue<bool>("Ntfy:Enabled"),
    builder.Configuration["Ntfy:Server"] is { Length: > 0 } server ? server : "https://ntfy.sh",
    builder.Configuration["Ntfy:Topic"] ?? "",
    builder.Configuration.GetValue<double?>("Ntfy:ReminderAfterHours") ?? 24);
var workspaceRoot = Directory.GetParent(builder.Environment.ContentRootPath)?.FullName
    ?? throw new InvalidOperationException("Workspace root was not found.");
var gitDataCommitOptions = new GitDataCommitOptions(
    workspaceRoot,
    builder.Configuration.GetValue<bool?>("Git:AutoCommitData") ?? true,
    builder.Configuration.GetValue<bool>("Git:AutoPush"));
var dashboardRoot = Path.Combine(workspaceRoot, "dashboard");
var dataRoot = Path.Combine(dashboardRoot, "data");
var storage = new Storage(dataRoot);
var outboxManager = new OutboxManager(workspaceRoot);
var contributionsLock = new object();
var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
};

foreach (var restoredProjectId in storage.ValidateAndRestoreAllProjects())
{
    Notifier.NotifyRestored(ntfyOptions, ReadProjectDisplayName(storage, restoredProjectId));
}

var app = builder.Build();

// 원격 쓰기 액션에 토큰을 요구한다. 토큰 미설정 시 통과, GET은 항상 무관.
app.Use(async (context, next) =>
{
    if (!string.IsNullOrWhiteSpace(remoteActionToken) &&
        HttpMethods.IsPost(context.Request.Method) &&
        context.Request.Path.StartsWithSegments("/api"))
    {
        var provided = context.Request.Headers["X-Action-Token"].ToString();

        if (!string.Equals(provided, remoteActionToken, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { reasonCode = "auth.invalid_token", reason = "X-Action-Token header is missing or invalid" });
            return;
        }
    }

    await next();
});

app.MapGet("/api/projects/{projectId}/state", (string projectId) => ReadFile(storage, projectId, Storage.StateFile, jsonOptions));
app.MapGet("/api/projects/{projectId}/runlog", (string projectId) => ReadFile(storage, projectId, Storage.RunLogFile, jsonOptions));
app.MapGet("/api/projects/{projectId}/proposal", (string projectId) => ReadFile(storage, projectId, Storage.ProposalFile, jsonOptions));
app.MapGet("/api/projects/{projectId}/reviews", (string projectId) => ReadFile(storage, projectId, Storage.ReviewFile, jsonOptions));
app.MapGet("/api/projects/{projectId}/definition", (string projectId) => ReadFile(storage, projectId, Storage.DefinitionFile, jsonOptions));
app.MapGet("/api/projects/{projectId}/blueprint", (string projectId) => ReadFile(storage, projectId, Storage.BlueprintFile, jsonOptions));
app.MapGet("/api/projects/{projectId}/measurement", (string projectId) => ReadFile(storage, projectId, Storage.MeasurementFile, jsonOptions));
app.MapGet("/api/projects/{projectId}/cycle-summary", (string projectId) => CycleSummary(storage, projectId, jsonOptions));
app.MapGet("/api/inbox", () => Inbox(storage, jsonOptions, ntfyOptions, outboxManager));
app.MapGet("/api/outbox/{taskId}", (string taskId) => DispatchResult(() => outboxManager.ReadTask(taskId), jsonOptions));

app.MapPost("/api/projects/{projectId}/actions/measure", (string projectId) =>
{
    lock (storage.GetProjectLock(projectId))
    {
        return Measure(storage, projectId, jsonOptions, ntfyOptions);
    }
});

app.MapPost("/api/projects/{projectId}/actions/approve", async (string projectId, HttpRequest request) =>
{
    var body = await ReadBodyObject(request);
    lock (storage.GetProjectLock(projectId))
    {
        return Approve(storage, projectId, body, jsonOptions, ntfyOptions, gitDataCommitOptions);
    }
});

app.MapPost("/api/projects/{projectId}/actions/reject", async (string projectId, HttpRequest request) =>
{
    var body = await ReadBodyObject(request);
    lock (storage.GetProjectLock(projectId))
    {
        return Reject(storage, projectId, body, jsonOptions, ntfyOptions, gitDataCommitOptions);
    }
});

app.MapPost("/api/projects/{projectId}/actions/edit-change", async (string projectId, HttpRequest request) =>
{
    var body = await ReadBodyObject(request);
    lock (storage.GetProjectLock(projectId))
    {
        return EditChange(storage, projectId, body, jsonOptions, ntfyOptions, gitDataCommitOptions);
    }
});

app.MapPost("/api/projects/{projectId}/actions/acknowledge", async (string projectId, HttpRequest request) =>
{
    var body = await ReadBodyObject(request);
    lock (storage.GetProjectLock(projectId))
    {
        return Acknowledge(storage, projectId, body, jsonOptions, gitDataCommitOptions);
    }
});

app.MapPost("/api/projects/{projectId}/actions/dispatch", async (string projectId, HttpRequest request) =>
{
    var body = await ReadBodyObject(request);
    return await DispatchResultAsync(() => outboxManager.DispatchAsync(projectId, body, remoteActionToken ?? "", request.Headers["X-Action-Token"].ToString()), jsonOptions);
});

app.MapPost("/api/projects/{projectId}/actions/self-refactor-dispatch", async (string projectId, HttpRequest request) =>
{
    var body = await ReadBodyObject(request);
    return await DispatchResultAsync(async () =>
    {
        var token = request.Headers["X-Action-Token"].ToString();
        var instruction = body["instruction"]?.GetValue<string>() ?? DefaultSelfRefactorInstruction();
        var first = await outboxManager.DispatchAsync(projectId, new JsonObject { ["executor"] = "ollama", ["instruction"] = instruction }, remoteActionToken ?? "", token);

        if (first["status"]?.GetValue<string>() == "import_pending")
        {
            return new JsonObject { ["schemaVersion"] = 2, ["attempts"] = new JsonArray(first) };
        }

        lock (storage.GetProjectLock(projectId))
        {
            RecordDispatchEscalation(storage, projectId, first["taskId"]?.GetValue<string>() ?? "", jsonOptions);
        }

        var second = await outboxManager.DispatchAsync(projectId, new JsonObject { ["executor"] = "claude-code", ["instruction"] = instruction }, remoteActionToken ?? "", token);
        return new JsonObject { ["schemaVersion"] = 2, ["attempts"] = new JsonArray(first, second) };
    }, jsonOptions);
});

app.MapPost("/api/projects/{projectId}/outbox/{taskId}/approve-import", (string projectId, string taskId, HttpRequest request) =>
{
    return DispatchResult(() => outboxManager.ApproveImport(taskId, remoteActionToken ?? "", request.Headers["X-Action-Token"].ToString()), jsonOptions);
});

app.MapPost("/api/projects/{projectId}/outbox/{taskId}/reject-import", async (string projectId, string taskId, HttpRequest request) =>
{
    var body = await ReadBodyObject(request);
    return DispatchResult(() => outboxManager.RejectImport(taskId, body, remoteActionToken ?? "", request.Headers["X-Action-Token"].ToString()), jsonOptions);
});

app.MapPost("/api/contributions", async (HttpRequest request) =>
{
    var body = await ReadBodyObject(request);
    lock (contributionsLock)
    {
        return DispatchResult(() => ContributionStore.Submit(storage, workspaceRoot, body), jsonOptions);
    }
});

app.MapGet("/api/projects/{projectId}/context", (string projectId) => ProjectContext(storage, projectId, jsonOptions, ntfyOptions, workspaceRoot));

app.UseDefaultFiles(new DefaultFilesOptions
{
    FileProvider = new PhysicalFileProvider(dashboardRoot),
});
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(dashboardRoot),
});

app.Run();
return 0;

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

// 모든 프로젝트의 사람 행동 대기 항목을 반환한다.
static IResult Inbox(Storage storage, JsonSerializerOptions jsonOptions, NtfyOptions ntfy, OutboxManager outboxManager)
{
    var items = BuildInboxItems(storage, ntfy);
    outboxManager.AddInboxItems(items);
    ContributionStore.AddPendingInboxItems(storage, items);
    return JsonResult(new JsonObject
    {
        ["schemaVersion"] = 2,
        ["items"] = items,
    }, jsonOptions);
}

// 현재 회차의 측정, 생성, 검토, 사람 대기 시간을 집계한다.
static IResult CycleSummary(Storage storage, string projectId, JsonSerializerOptions jsonOptions)
{
    try
    {
        var bundle = storage.ReadBundle(projectId);
        return JsonResult(BuildCycleSummary(bundle.State, bundle.RunLog, bundle.Proposal), jsonOptions);
    }
    catch (FileNotFoundException)
    {
        return ProblemResult(404, "path.not_found", "project not found");
    }
    catch (Exception error)
    {
        return ProblemResult(500, "system.read_failed", error.Message);
    }
}

// 새 에이전트 온보딩용 한 방 조회: blueprint·최근 회차·미결·관련 스킬 경로.
static IResult ProjectContext(Storage storage, string projectId, JsonSerializerOptions jsonOptions, NtfyOptions ntfy, string workspaceRoot)
{
    try
    {
        var bundle = storage.ReadBundle(projectId);
        var projectName = ProjectDisplayName(bundle.State);
        var pending = new JsonArray();
        AddProjectInboxItems(storage, projectId, projectName, pending, ntfy);
        return JsonResult(new JsonObject
        {
            ["schemaVersion"] = 2,
            ["projectId"] = projectId,
            ["blueprint"] = Engine.CloneNode(bundle.Blueprint),
            ["recentCycle"] = BuildCycleSummary(bundle.State, bundle.RunLog, bundle.Proposal),
            ["pending"] = pending,
            ["relevantSkillPaths"] = SkillRouter.RelevantPaths(workspaceRoot, projectId),
        }, jsonOptions);
    }
    catch (FileNotFoundException)
    {
        return ProblemResult(404, "path.not_found", "project not found");
    }
    catch (Exception error)
    {
        return ProblemResult(500, "system.read_failed", error.Message);
    }
}

// run-log와 현재 proposal로 회차 시간 분해 JSON을 만든다.
static JsonObject BuildCycleSummary(JsonObject state, JsonObject runLog, JsonObject proposal)
{
    var loopIteration = Engine.GetLoopIteration(state);
    var entries = (runLog["entries"]?.AsArray() ?? new JsonArray())
        .OfType<JsonObject>()
        .Where(entry => Number(entry["loopIteration"], loopIteration) == loopIteration)
        .OrderBy(entry => entry["createdAt"]?.GetValue<string>() ?? "", StringComparer.Ordinal)
        .ToList();
    var measurementMs = SumEventDuration(entries, "measure.completed");
    var generationMs = SumEventDuration(entries, "proposal.generated");
    var reviewMs = SumEventDuration(entries, "review.tier1_completed");
    var humanWaitingMs = CalculateHumanWaitingMs(entries, proposal);

    return new JsonObject
    {
        ["schemaVersion"] = 2,
        ["loopIteration"] = loopIteration,
        ["segments"] = new JsonObject
        {
            ["measurementMs"] = measurementMs,
            ["generationMs"] = generationMs,
            ["reviewMs"] = reviewMs,
            ["humanWaitingMs"] = humanWaitingMs,
            ["totalMs"] = measurementMs + generationMs + reviewMs + humanWaitingMs,
        },
    };
}

// 특정 이벤트의 durationMs 값을 합산한다.
static long SumEventDuration(List<JsonObject> entries, string eventName)
{
    return entries
        .Where(entry => entry["event"]?.GetValue<string>() == eventName)
        .Select(entry => Number(entry["params"]?.AsObject()["durationMs"], 0))
        .Sum(value => (long)Math.Max(0, value));
}

// 제출된 proposal이 사람 결재를 기다린 시간을 계산한다.
static long CalculateHumanWaitingMs(List<JsonObject> entries, JsonObject proposal)
{
    var proposalId = proposal["id"]?.GetValue<string>() ?? "";
    var lifecycle = proposal["lifecycle"]?.GetValue<string>() ?? "";

    if (string.IsNullOrWhiteSpace(proposalId))
    {
        return 0;
    }

    var createdAt = entries
        .Where(entry => entry["event"]?.GetValue<string>() == "proposal.created" &&
            entry["params"]?.AsObject()["proposalId"]?.GetValue<string>() == proposalId)
        .Select(entry => entry["createdAt"]?.GetValue<string>())
        .FirstOrDefault();

    if (!DateTimeOffset.TryParse(createdAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
    {
        return 0;
    }

    var decidedAt = entries
        .Where(entry => (entry["event"]?.GetValue<string>() == "review.approved" ||
                entry["event"]?.GetValue<string>() == "review.rejected") &&
            entry["params"]?.AsObject()["proposalId"]?.GetValue<string>() == proposalId)
        .Select(entry => entry["createdAt"]?.GetValue<string>())
        .FirstOrDefault();

    if (DateTimeOffset.TryParse(decidedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
    {
        return Math.Max(0, (long)(end - start).TotalMilliseconds);
    }

    return lifecycle == "submitted"
        ? Math.Max(0, (long)(DateTimeOffset.Now - start).TotalMilliseconds)
        : 0;
}

// 등록된 프로젝트를 훑어 사람 행동 대기 목록을 만든다.
static JsonArray BuildInboxItems(Storage storage, NtfyOptions ntfy)
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
static void AddProjectInboxItems(Storage storage, string projectId, string projectName, JsonArray items, NtfyOptions ntfy)
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
static string? FindProposalCreatedAt(JsonObject runLog, string proposalId)
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
static string SummarizeProposal(JsonObject proposal)
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

// 측정 공급자를 실행하고 블루프린트 괴리를 판정한다.
static IResult Measure(Storage storage, string projectId, JsonSerializerOptions jsonOptions, NtfyOptions ntfy)
{
    var outcome = RunMeasureCore(storage, projectId, jsonOptions, ntfy);
    return outcome.Problem ?? BundleResult(outcome.Bundle!, jsonOptions);
}

// 측정 실행 본체. HTTP 라우트와 CLI가 함께 사용한다.
static MeasureOutcome RunMeasureCore(Storage storage, string projectId, JsonSerializerOptions jsonOptions, NtfyOptions ntfy)
{
    var bundle = storage.ReadBundle(projectId);
    var provider = bundle.Definition["measurementProvider"] as JsonObject;
    var providerId = provider?["id"]?.GetValue<string>();

    if (string.IsNullOrWhiteSpace(providerId))
    {
        return new MeasureOutcome(null, ProblemResult(409, "checklist.provider_missing", "Measurement provider is not configured"), 0);
    }

    if (providerId != "dev-pack-checks" && providerId != "ruined-lab-sim")
    {
        return new MeasureOutcome(null, ProblemResult(409, "checklist.provider_unknown", $"Measurement provider is not supported: {providerId}"), 0);
    }

    storage.CreateRestorePoint(projectId);
    var previousMeasurement = bundle.Measurement;
    var measureTimer = System.Diagnostics.Stopwatch.StartNew();
    bundle.Measurement = providerId switch
    {
        "dev-pack-checks" => DevPackMeasures.Measure(ResolveMeasurementTargetRoot(storage.ProjectPath(projectId), provider!), providerId, bundle.Blueprint),
        "ruined-lab-sim" => GameSimulator.Measure(storage.ProjectPath(projectId), providerId, bundle.Blueprint, provider!),
        _ => bundle.Measurement,
    };
    measureTimer.Stop();
    var violationCount = ApplyMeasurementResult(bundle, providerId, ntfy, previousMeasurement, storage.ProjectPath(projectId), measureTimer.ElapsedMilliseconds);
    Persist(storage, projectId, bundle, jsonOptions, ntfy);
    return new MeasureOutcome(bundle, null, violationCount);
}

// 서버를 띄우지 않고 측정을 실행하는 CLI 진입점. 위반 0=0, 위반 존재=1, 실행 오류=2를 반환한다.
static int RunMeasureCli(string[] args)
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
            outcome = RunMeasureCore(storage, projectId, cliJsonOptions, cliNtfy);
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
static JsonObject BuildCliSummary(string projectId, MeasureOutcome outcome)
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
static JsonObject CliError(string message)
{
    return new JsonObject { ["error"] = message };
}

// 게임 시뮬레이터를 두 번 실행해 같은 시드가 같은 결과를 내는지 확인하는 CLI. 재현 실패·데이터 없음=2, 정상=0.
static int RunSimTestCli(string[] args)
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
static bool SimResultsEqual(SimResult first, SimResult second)
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
static int RunSimTuneCli(string[] args)
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
static int RunRefeedbackTestCli()
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
    var escalated = TryEscalateInsufficientRefeedback(ref tier1, ref runLog, state);
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

// 측정 결과를 상태, 로그, 제안에 반영하고 위반 수를 반환한다.
static int ApplyMeasurementResult(ProjectBundle bundle, string providerId, NtfyOptions ntfy, JsonObject previousMeasurement, string projectPath, long durationMs)
{
    var checks = EvaluateBlueprintChecks(bundle.Blueprint, bundle.Measurement);
    var violations = checks.Where(check => check.Implemented && !check.Passed).ToList();
    var previousChecks = EvaluateBlueprintChecks(bundle.Blueprint, previousMeasurement);
    var regressions = DetectRegressions(previousChecks, checks);
    var stages = ResolveMeasurementStages(bundle.Definition);
    var state = bundle.State;
    // 이미 승인된 반영이 진행 중(apply:in_progress)인데 위반 집합이 승인 시점과 그대로면
    // 아직 아무도 안 고쳤다는 뜻이다 — 이때도 새 제안을 만들면, 사람이 이미 승인해 둔 화면에
    // "새 제안"이 떠서 승인 버튼만 비활성으로 막힌 것처럼 보이게 된다.
    var applyStageAlreadyInProgress = stages.ApplyStageId is not null
        && Engine.GetStageStatus(state, stages.ApplyStageId) == "in_progress"
        && ViolationSignatureUnchanged(state, violations);
    var runLog = Engine.AppendLog(bundle.RunLog, new JsonObject
    {
        ["event"] = "measure.completed",
        ["params"] = new JsonObject { ["providerId"] = providerId, ["violationCount"] = violations.Count, ["durationMs"] = durationMs },
        ["level"] = violations.Count > 0 ? "warning" : "info",
        ["producedBy"] = new JsonObject { ["provider"] = "rule-engine", ["model"] = null },
        ["cost"] = RuntimeCost(),
    }, Engine.GetLoopIteration(state));

    state = ApplyMeasurementStagePatch(bundle.Definition, state, checks, violations, stages);
    SetMeasurementDetails(state, stages.DeviationStageId, checks, violations);
    runLog = ResumeSatisfiedTracks(state, runLog, checks);

    if (applyStageAlreadyInProgress)
    {
        // 새 제안·회귀 판정·tier1 검토를 만들지 않는다 — 이미 승인된 반영이 끝날 때까지 그대로 둔다.
    }
    else if (violations.Count > 0)
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

        if (regressions.Count > 0)
        {
            var relatedProposalId = FindRecentApprovedProposalId(bundle.Reviews, previousMeasurement);

            foreach (var regression in regressions)
            {
                runLog = Engine.AppendLog(runLog, new JsonObject
                {
                    ["event"] = "measurement.regressed",
                    ["params"] = new JsonObject
                    {
                        ["metricId"] = regression.MetricId,
                        ["before"] = regression.PreviousValue is null ? null : Engine.CloneNode(regression.PreviousValue),
                        ["after"] = regression.CurrentValue is null ? null : Engine.CloneNode(regression.CurrentValue),
                        ["reasonCode"] = "checklist.metric_regressed",
                    },
                    ["level"] = "warning",
                    ["producedBy"] = new JsonObject { ["provider"] = "rule-engine", ["model"] = null },
                    ["cost"] = RuntimeCost(),
                }, Engine.GetLoopIteration(state));

                AddSuspendedTrack(state, regression, relatedProposalId);
            }

            bundle.Proposal = CreateRollbackProposal(regressions, relatedProposalId);
            runLog = Engine.AppendLog(runLog, ProposalCreatedLog(bundle.Proposal), Engine.GetLoopIteration(state));
            runLog = Engine.AppendLog(runLog, new JsonObject
            {
                ["event"] = "review.routed",
                ["params"] = new JsonObject
                {
                    ["proposalId"] = bundle.Proposal["id"]?.GetValue<string>() ?? "",
                    ["reasonCode"] = "regression_direct_to_human",
                    ["text"] = "악화 롤백은 체크리스트가 검토할 내용이 없어 1층을 건너뛰고 사람 결재로 직행한다.",
                },
                ["level"] = "warning",
                ["producedBy"] = new JsonObject { ["provider"] = "rule-engine", ["model"] = null },
                ["cost"] = RuntimeCost(),
            }, Engine.GetLoopIteration(state));

            SetRegressionReviewDetails(state, stages.ReviewStageId, regressions);
            Notifier.NotifyMeasurementRegressed(ntfy, ProjectDisplayName(state), regressions.Select(r => r.MetricId).ToList());
        }
        else if (providerId == "ruined-lab-sim")
        {
            var gameDataPath = Path.Combine(projectPath, "game-data.json");
            var gameData = JsonNode.Parse(File.ReadAllText(gameDataPath))!.AsObject();
            var seed = Number(bundle.Definition["measurementProvider"]?.AsObject()["seed"], 42);
            var tuning = BalanceTuner.Search(gameData, bundle.Blueprint, bundle.Definition, seed);

            if (!tuning.ReachedBand && tuning.ChangedLevers.Count == 0)
            {
                if (bundle.Proposal["lifecycle"]?.GetValue<string>() == "submitted")
                {
                    var supersededId = bundle.Proposal["id"]?.GetValue<string>() ?? "";
                    bundle.Proposal["lifecycle"] = "superseded";
                    runLog = Engine.AppendLog(runLog, new JsonObject
                    {
                        ["event"] = "proposal.superseded",
                        ["params"] = new JsonObject { ["proposalId"] = supersededId, ["reasonCode"] = "tuning.no_solution" },
                        ["level"] = "info",
                        ["producedBy"] = new JsonObject { ["provider"] = "rule-engine", ["model"] = null },
                        ["cost"] = RuntimeCost(),
                    }, Engine.GetLoopIteration(state));
                }

                runLog = Engine.AppendLog(runLog, TuningNoSolutionLog(tuning), Engine.GetLoopIteration(state));
                state = ApplyNoSolutionState(bundle.Definition, state, stages);
                SetNoSolutionDetails(state, stages.DeviationStageId, tuning);
            }
            else
            {
                var replacedProposalId = bundle.Proposal["lifecycle"]?.GetValue<string>() == "submitted"
                    ? bundle.Proposal["id"]?.GetValue<string>()
                    : null;
                var tuningGeneration = GenerateTuningProposalWithFallback(bundle.Definition, bundle.Proposal, tuning, previousReviewReport: null);
                bundle.Proposal = tuningGeneration.Proposal;
                runLog = Engine.AppendLog(runLog, tuningGeneration.LogEntry, Engine.GetLoopIteration(state));
                if (!string.IsNullOrWhiteSpace(replacedProposalId))
                {
                    runLog = Engine.AppendLog(runLog, new JsonObject
                    {
                        ["event"] = "proposal.superseded",
                        ["params"] = new JsonObject { ["proposalId"] = replacedProposalId, ["reasonCode"] = "tuning.replaced" },
                        ["level"] = "info",
                        ["producedBy"] = new JsonObject { ["provider"] = "rule-engine", ["model"] = null },
                        ["cost"] = RuntimeCost(),
                    }, Engine.GetLoopIteration(state));
                }

                runLog = Engine.AppendLog(runLog, ProposalCreatedLog(bundle.Proposal), Engine.GetLoopIteration(state));

                var tuningTier1 = OllamaReviewer.Review(bundle.Definition, bundle.Proposal, bundle.Measurement, AssessRisk(bundle.Definition, bundle.Proposal));
                runLog = Engine.AppendLog(runLog, tuningTier1.LogEntry, Engine.GetLoopIteration(state));

                if (tuningTier1.Report is not null)
                {
                    AppendReport(bundle.Reviews, tuningTier1.Report);
                }

                var tuningMaxRegenerations = Number(bundle.Definition["executorPolicy"]?.AsObject()["maxRegenerations"], 0);

                for (var regeneration = 0; regeneration < tuningMaxRegenerations && tuningTier1.Verdict == "needs_changes"; regeneration += 1)
                {
                    if (TryEscalateInsufficientRefeedback(ref tuningTier1, ref runLog, state))
                    {
                        break;
                    }

                    var supersededId = bundle.Proposal["id"]?.GetValue<string>() ?? "";
                    runLog = Engine.AppendLog(runLog, new JsonObject
                    {
                        ["event"] = "proposal.superseded",
                        ["params"] = new JsonObject { ["proposalId"] = supersededId, ["reasonCode"] = "review.needs_changes_regenerate" },
                        ["level"] = "info",
                        ["producedBy"] = new JsonObject { ["provider"] = "rule-engine", ["model"] = null },
                        ["cost"] = RuntimeCost(),
                    }, Engine.GetLoopIteration(state));

                    tuningGeneration = GenerateTuningProposalWithFallback(bundle.Definition, bundle.Proposal, tuning, previousReviewReport: tuningTier1.Report);
                    bundle.Proposal = tuningGeneration.Proposal;
                    runLog = Engine.AppendLog(runLog, tuningGeneration.LogEntry, Engine.GetLoopIteration(state));
                    runLog = Engine.AppendLog(runLog, ProposalCreatedLog(bundle.Proposal), Engine.GetLoopIteration(state));

                    tuningTier1 = OllamaReviewer.Review(bundle.Definition, bundle.Proposal, bundle.Measurement, AssessRisk(bundle.Definition, bundle.Proposal));
                    runLog = Engine.AppendLog(runLog, tuningTier1.LogEntry, Engine.GetLoopIteration(state));

                    if (tuningTier1.Report is not null)
                    {
                        AppendReport(bundle.Reviews, tuningTier1.Report);
                    }
                }

                SetTier1Details(state, stages.ReviewStageId, tuningTier1);
                Notifier.NotifyReviewPending(ntfy, ProjectDisplayName(state), ProposalTitle(bundle.Proposal), tuningTier1.Verdict);
            }
        }
        else
        {
            var generation = GenerateProposalWithFallback(bundle.Definition, bundle.Proposal, violations, previousReviewReport: null);
            bundle.Proposal = generation.Proposal;
            runLog = Engine.AppendLog(runLog, generation.LogEntry, Engine.GetLoopIteration(state));
            runLog = Engine.AppendLog(runLog, ProposalCreatedLog(bundle.Proposal), Engine.GetLoopIteration(state));

            var tier1 = OllamaReviewer.Review(bundle.Definition, bundle.Proposal, bundle.Measurement, AssessRisk(bundle.Definition, bundle.Proposal));
            runLog = Engine.AppendLog(runLog, tier1.LogEntry, Engine.GetLoopIteration(state));

            if (tier1.Report is not null)
            {
                AppendReport(bundle.Reviews, tier1.Report);
            }

            var maxRegenerations = Number(bundle.Definition["executorPolicy"]?.AsObject()["maxRegenerations"], 0);

            for (var regeneration = 0; regeneration < maxRegenerations && tier1.Verdict == "needs_changes"; regeneration += 1)
            {
                if (TryEscalateInsufficientRefeedback(ref tier1, ref runLog, state))
                {
                    break;
                }

                var supersededId = bundle.Proposal["id"]?.GetValue<string>() ?? "";
                runLog = Engine.AppendLog(runLog, new JsonObject
                {
                    ["event"] = "proposal.superseded",
                    ["params"] = new JsonObject { ["proposalId"] = supersededId, ["reasonCode"] = "review.needs_changes_regenerate" },
                    ["level"] = "info",
                    ["producedBy"] = new JsonObject { ["provider"] = "rule-engine", ["model"] = null },
                    ["cost"] = RuntimeCost(),
                }, Engine.GetLoopIteration(state));

                generation = GenerateProposalWithFallback(bundle.Definition, bundle.Proposal, violations, previousReviewReport: tier1.Report);
                bundle.Proposal = generation.Proposal;
                runLog = Engine.AppendLog(runLog, generation.LogEntry, Engine.GetLoopIteration(state));
                runLog = Engine.AppendLog(runLog, ProposalCreatedLog(bundle.Proposal), Engine.GetLoopIteration(state));

                tier1 = OllamaReviewer.Review(bundle.Definition, bundle.Proposal, bundle.Measurement, AssessRisk(bundle.Definition, bundle.Proposal));
                runLog = Engine.AppendLog(runLog, tier1.LogEntry, Engine.GetLoopIteration(state));

                if (tier1.Report is not null)
                {
                    AppendReport(bundle.Reviews, tier1.Report);
                }
            }

            SetTier1Details(state, stages.ReviewStageId, tier1);
            Notifier.NotifyReviewPending(ntfy, ProjectDisplayName(state), ProposalTitle(bundle.Proposal), tier1.Verdict);
        }
    }
    else
    {
        var suggestedBlueprintProposal = CreateSuggestedBlueprintProposal(bundle.Definition, bundle.Blueprint, bundle.Measurement, bundle.Proposal);
        if (suggestedBlueprintProposal is not null)
        {
            bundle.Proposal = suggestedBlueprintProposal;
            runLog = Engine.AppendLog(runLog, ProposalCreatedLog(bundle.Proposal), Engine.GetLoopIteration(state));
            state = ApplySuggestedBlueprintProposalState(bundle.Definition, state, stages);
            Notifier.NotifyReviewPending(ntfy, ProjectDisplayName(state), ProposalTitle(bundle.Proposal), "human_review");
        }
        else if (bundle.Proposal["lifecycle"]?.GetValue<string>() == "submitted")
        {
            bundle.Proposal["lifecycle"] = "superseded";
        }
    }

    // 위의 여러 분기(회귀 롤백·튜닝·기준 추가 제안 등) 중 어느 것이 실행됐든, 최종적으로
    // 확정된 적용 단계 상태를 기준으로 그 단계 상세를 한 번에 맞춰 쓴다 — 분기마다 각자
    // 갱신하면 나중 분기가 앞선 갱신을 덮어써 화면과 실제 상태가 어긋나기 쉽다.
    SetApplyStageDetails(state, stages.ApplyStageId);

    bundle.State = state;
    bundle.RunLog = runLog;
    return violations.Count;
}

// definition의 suggestedBlueprintMetrics 중 아직 blueprint에 없는 항목을 기준 추가 proposal로 만든다.
static JsonObject? CreateSuggestedBlueprintProposal(JsonObject definition, JsonObject blueprint, JsonObject measurement, JsonObject currentProposal)
{
    if (currentProposal["lifecycle"]?.GetValue<string>() == "submitted")
    {
        return null;
    }

    var existingMetricIds = (blueprint["items"]?.AsArray() ?? new JsonArray())
        .OfType<JsonObject>()
        .Select(item => item["metricId"]?.GetValue<string>() ?? "")
        .Where(id => !string.IsNullOrWhiteSpace(id))
        .ToHashSet(StringComparer.Ordinal);
    var measuredMetricIds = (measurement["metrics"]?.AsArray() ?? new JsonArray())
        .OfType<JsonObject>()
        .Select(metric => metric["metricId"]?.GetValue<string>() ?? "")
        .Where(id => !string.IsNullOrWhiteSpace(id))
        .ToHashSet(StringComparer.Ordinal);
    var changes = new JsonArray();
    var startIndex = existingMetricIds.Count;

    foreach (var suggestion in definition["suggestedBlueprintMetrics"]?.AsArray().OfType<JsonObject>() ?? [])
    {
        var metricId = suggestion["metricId"]?.GetValue<string>() ?? "";
        if (string.IsNullOrWhiteSpace(metricId) || existingMetricIds.Contains(metricId) || !measuredMetricIds.Contains(metricId))
        {
            continue;
        }

        changes.Add(new JsonObject
        {
            ["path"] = $"blueprint.items[{startIndex + changes.Count}]",
            ["before"] = null,
            ["after"] = Engine.CloneNode(suggestion),
            ["note"] = $"측정은 존재하지만 기준에는 없는 {metricId} 지표를 blueprint 추가 후보로 올린다.",
        });
    }

    if (changes.Count == 0)
    {
        return null;
    }

    var revisionOf = currentProposal["lifecycle"]?.GetValue<string>() == "submitted" ? currentProposal["id"]?.GetValue<string>() : null;
    return new JsonObject
    {
        ["schemaVersion"] = 2,
        ["id"] = $"proposal-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
        ["title"] = "방별 도달률 기준 추가 제안",
        ["lifecycle"] = "submitted",
        ["kind"] = "blueprint_metric",
        ["createdBy"] = new JsonObject { ["provider"] = "rule-engine", ["model"] = null },
        ["revisionOf"] = revisionOf,
        ["summary"] = "측정에 포함된 방별 도달률을 blueprint 기준 후보로 올린다. 기준 반영은 사람 결재 후에만 가능하다.",
        ["assumptions"] = new JsonArray("방별 도달률은 문턱형 완주율을 보조하는 연속형 관찰 지표다."),
        ["changes"] = changes,
        ["impact"] = new JsonArray
        {
            new JsonObject { ["label"] = "기준 후보", ["value"] = changes.Count.ToString(CultureInfo.InvariantCulture) },
            new JsonObject { ["label"] = "예상 비용", ["value"] = "$0.00" },
        },
    };
}

// 기준 추가 proposal을 사람 검토 단계로 보낸다.
static JsonObject ApplySuggestedBlueprintProposalState(JsonObject definition, JsonObject state, MeasurementStages stages)
{
    var stageStatuses = new JsonObject();
    var blockInfo = new JsonObject();

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
        // 방금 승인돼 적용이 진행 중인데 위반 집합이 승인 시점과 그대로면, 아직 아무도 고치지
        // 않았다는 뜻이다 — 이때 재측정만으로 적용을 blocked로 되돌리고 새 검토를 열면 사람이
        // 매번 다시 승인해야 하는 것처럼 보인다. 위반 집합이 실제로 달라졌을 때만 새 회차를 연다.
        var applyInProgress = stages.ApplyStageId is not null && Engine.GetStageStatus(state, stages.ApplyStageId) == "in_progress";

        if (applyInProgress && ViolationSignatureUnchanged(state, violations))
        {
            return Engine.ApplyStatePatch(definition, state, new JsonObject
            {
                ["loopState"] = "running",
                ["stageStatuses"] = stageStatuses,
            });
        }

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

// 현재 위반 집합이 직전 승인 시점에 저장해 둔 기준선과 같은지 비교한다.
static bool ViolationSignatureUnchanged(JsonObject state, List<MetricCheck> violations)
{
    var baseline = state["applyBaselineViolations"]?.AsArray();

    if (baseline is null)
    {
        return false;
    }

    var baselineSet = baseline.Select(node => node?.GetValue<string>() ?? "").ToHashSet(StringComparer.Ordinal);
    var currentSet = BuildViolationSignature(violations).Select(node => node!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
    return baselineSet.SetEquals(currentSet);
}

// 위반 목록을 "metricId=value" 형태의 비교 가능한 서명 배열로 만든다.
static JsonArray BuildViolationSignature(List<MetricCheck> violations)
{
    return new JsonArray(violations
        .Select(violation => (JsonNode?)JsonValue.Create($"{violation.MetricId}={violation.Value?.ToJsonString() ?? "null"}"))
        .ToArray());
}

// 적용/내보내기 단계 상세를 현재 단계 상태에 맞게 다시 쓴다 — 예전에는 이 단계가 한 번
// blocked로 채워지면 completed로 넘어간 뒤에도 그 문구가 그대로 남아 화면과 실제 상태가 어긋났다.
static void SetApplyStageDetails(JsonObject state, string? stageId)
{
    if (stageId is null)
    {
        return;
    }

    var status = Engine.GetStageStatus(state, stageId);
    var summary = "아직 시작되지 않았다.";
    var metricValue = "대기";
    var issues = new JsonArray();

    if (status == "completed")
    {
        summary = "승인된 변경이 적용 완료됐다.";
        metricValue = "완료";
    }
    else if (status == "in_progress")
    {
        summary = "승인된 변경을 반영하는 중이다 — 실제로 반영한 뒤 재측정하면 다음 단계로 넘어간다.";
        metricValue = "진행 중";
    }
    else if (status == "blocked")
    {
        summary = "변경 승인이 완료될 때까지 차단된다.";
        metricValue = "차단됨";
        issues.Add("이전 단계가 아직 완료되지 않았다.");
    }

    state["stageDetails"] ??= new JsonObject();
    state["stageDetails"]!.AsObject()[stageId] = new JsonObject
    {
        ["summary"] = summary,
        ["metrics"] = new JsonArray(new JsonObject { ["label"] = "적용 상태", ["value"] = metricValue }),
        ["issues"] = issues,
    };
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

// 해 없음 튜닝 결과를 측정 단계 상세에 추가한다.
static void SetNoSolutionDetails(JsonObject state, string? stageId, TuningResult tuning)
{
    if (stageId is null)
    {
        return;
    }

    state["stageDetails"] ??= new JsonObject();
    var details = state["stageDetails"]!.AsObject()[stageId] as JsonObject ?? new JsonObject();
    var metrics = details["metrics"] as JsonArray ?? new JsonArray();
    metrics.Add(new JsonObject { ["label"] = "탐색 후보 수", ["value"] = tuning.CandidatesUsed.ToString(CultureInfo.InvariantCulture) });
    metrics.Add(new JsonObject { ["label"] = "재시작 시도", ["value"] = tuning.RestartAttempts.ToString(CultureInfo.InvariantCulture) });
    metrics.Add(new JsonObject { ["label"] = "주항 거리", ["value"] = $"{tuning.BaselineDistance.ToString("0.###", CultureInfo.InvariantCulture)} -> {tuning.FinalDistance.ToString("0.###", CultureInfo.InvariantCulture)}" });
    metrics.Add(new JsonObject { ["label"] = "평균 진행 방 수", ["value"] = $"{tuning.BaselineProgressedRooms.ToString("0.###", CultureInfo.InvariantCulture)} -> {tuning.FinalProgressedRooms.ToString("0.###", CultureInfo.InvariantCulture)}" });

    var issues = details["issues"] as JsonArray ?? new JsonArray();
    issues.Add("레버 범위 내 해 없음 — 범위 확장은 기준 변경으로 사람 결재 사항");

    details["summary"] = "측정 결과가 기준을 벗어났고, 현재 레버 범위 안에서는 변경 제안을 만들 해를 찾지 못했다.";
    details["metrics"] = metrics;
    details["issues"] = issues;
    state["stageDetails"]!.AsObject()[stageId] = details;
}

// 해 없음 튜닝 결과에 맞춰 결재 단계 대기를 해제한다.
static JsonObject ApplyNoSolutionState(JsonObject definition, JsonObject state, MeasurementStages stages)
{
    var stageStatuses = new JsonObject();
    var blockInfo = new JsonObject();

    if (stages.ReviewStageId is not null)
    {
        stageStatuses[stages.ReviewStageId] = "not_started";
    }

    if (stages.ApplyStageId is not null)
    {
        stageStatuses[stages.ApplyStageId] = "blocked";
        blockInfo[stages.ApplyStageId] = new JsonObject { ["kind"] = "waiting" };
    }

    return Engine.ApplyStatePatch(definition, state, new JsonObject
    {
        ["currentStage"] = stages.DeviationStageId ?? state["currentStage"]?.GetValue<string>(),
        ["stageStatuses"] = stageStatuses,
        ["blockInfo"] = blockInfo,
    });
}

// 해 없음 튜닝 결과 로그 항목을 만든다.
static JsonObject TuningNoSolutionLog(TuningResult tuning)
{
    return new JsonObject
    {
        ["event"] = "tuning.no_solution",
        ["params"] = new JsonObject
        {
            ["candidatesUsed"] = tuning.CandidatesUsed,
            ["residualViolations"] = new JsonArray(tuning.ResidualViolations.Select(text => (JsonNode)text).ToArray()),
            ["text"] = "레버 범위 내 해 없음 — 범위 확장은 기준 변경으로 사람 결재 사항",
        },
        ["level"] = "warning",
        ["producedBy"] = new JsonObject { ["provider"] = "rule-engine", ["model"] = null },
        ["cost"] = RuntimeCost(),
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

// 검토 지적이 재생성 지시로 충분한지 검사하고 부족하면 사람 검토로 격상한다.
static bool TryEscalateInsufficientRefeedback(ref Tier1ReviewResult tier1, ref JsonObject runLog, JsonObject state)
{
    var missingCheckIds = FindInsufficientRefeedback(tier1.Report);

    if (missingCheckIds.Count == 0)
    {
        return false;
    }

    if (tier1.Report is not null)
    {
        tier1.Report["verdict"] = "uncertain";
        tier1.Report["reason"] = "검토 지적이 재생성 지시로 충분하지 않아 사람 검토로 올렸다.";
    }

    runLog = Engine.AppendLog(runLog, RefeedbackInsufficientLog(missingCheckIds), Engine.GetLoopIteration(state));
    tier1 = new Tier1ReviewResult(tier1.Report, tier1.LogEntry, "uncertain", "review.refeedback_insufficient");
    return true;
}

// 실패 finding 중 재지시 정보가 부족한 checkId를 찾는다.
static List<string> FindInsufficientRefeedback(JsonObject? report)
{
    var result = new List<string>();
    var findings = report?["findings"]?.AsArray();

    if (findings is null)
    {
        return result;
    }

    foreach (var finding in findings.OfType<JsonObject>().Where(finding => finding["passed"]?.GetValue<bool>() == false))
    {
        var text = finding["note"]?.GetValue<string>() ?? finding["comment"]?.GetValue<string>() ?? "";
        var normalized = Regex.Replace(text, @"\s+", "");

        if (normalized.Length >= 10)
        {
            continue;
        }

        result.Add(finding["checkId"]?.GetValue<string>() ?? finding["target"]?.GetValue<string>() ?? "unknown");
    }

    return result.Distinct(StringComparer.Ordinal).ToList();
}

// 재지시 정보 부족 로그 항목을 만든다.
static JsonObject RefeedbackInsufficientLog(List<string> checkIds)
{
    return new JsonObject
    {
        ["event"] = "review.refeedback_insufficient",
        ["params"] = new JsonObject
        {
            ["checkIds"] = new JsonArray(checkIds.Select(id => (JsonNode)id).ToArray()),
            ["text"] = "검토 지적 정보가 부족해 재생성하지 않고 사람 검토로 올렸다.",
        },
        ["level"] = "warning",
        ["producedBy"] = new JsonObject { ["provider"] = "rule-engine", ["model"] = null },
        ["cost"] = RuntimeCost(),
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

// 튜닝 결과로 제안을 생성하고 실패 시 rule-engine 서술로 강등한다. 수치는 언제나 BalanceTuner가 결정한다.
static ProposalGeneration GenerateTuningProposalWithFallback(JsonObject definition, JsonObject currentProposal, TuningResult tuning, JsonObject? previousReviewReport)
{
    var revisionOf = currentProposal["lifecycle"]?.GetValue<string>() == "submitted" ? currentProposal["id"]?.GetValue<string>() : null;
    var generated = OllamaExecutor.GenerateForTuning(definition, tuning, previousReviewReport);

    if (!generated.Unavailable)
    {
        var proposal = BuildTuningProposal(tuning, generated.Provider, generated.Model, generated.Title, generated.Summary, generated.Notes, generated.Assumptions, revisionOf);
        return new ProposalGeneration(proposal, GeneratedLogEntry(generated.Provider, generated.Model, generated.DurationMs, false, null, generated.SelfReviewed, generated.SelfReviewPassed));
    }

    var fallbackProposal = BuildTuningProposal(tuning, "rule-engine", null, FallbackTuningTitle(tuning), FallbackTuningSummary(tuning), new Dictionary<string, string>(), [], revisionOf);
    return new ProposalGeneration(fallbackProposal, GeneratedLogEntry("rule-engine", null, generated.DurationMs, true, generated.Error, generated.SelfReviewed, generated.SelfReviewPassed));
}

// 튜닝 결과로 proposal JSON을 만든다. 밴드 도달 실패 시 잔여 위반과 결재 안내를 서버가 직접 덧붙인다(모델 준수 여부에 기대지 않는다).
static JsonObject BuildTuningProposal(TuningResult tuning, string provider, string? model, string title, string summary, Dictionary<string, string> notes, List<string> assumptions, string? revisionOf)
{
    var changes = new JsonArray();
    foreach (var change in tuning.ChangedLevers)
    {
        changes.Add(new JsonObject
        {
            ["path"] = change.Path,
            ["before"] = change.Before,
            ["after"] = change.After,
            ["note"] = TuningNote(tuning, notes.GetValueOrDefault(change.Path, FallbackLeverNote(change))),
        });
    }

    var predictedMetrics = new JsonArray();
    foreach (var metric in tuning.PredictedMetrics)
    {
        predictedMetrics.Add(new JsonObject
        {
            ["metricId"] = metric.MetricId,
            ["before"] = metric.Before,
            ["after"] = metric.After,
            ["band"] = metric.Band,
        });
    }

    var finalSummary = tuning.ReachedBand
        ? summary
        : $"{summary} (최선 후보, 밴드 미달. 잔여 위반: {string.Join(", ", tuning.ResidualViolations)}. 레버 범위 확장은 definition 변경 사항으로 별도 사람 결재가 필요하다.)";

    return new JsonObject
    {
        ["schemaVersion"] = 2,
        ["id"] = $"proposal-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
        ["title"] = title,
        ["lifecycle"] = "submitted",
        ["kind"] = "tuning",
        ["createdBy"] = new JsonObject { ["provider"] = provider, ["model"] = model },
        ["revisionOf"] = revisionOf,
        ["summary"] = finalSummary,
        ["assumptions"] = StringArray(assumptions),
        ["changes"] = changes,
        ["predictedMetrics"] = predictedMetrics,
        ["predictedReachedBand"] = tuning.ReachedBand,
        ["impact"] = new JsonArray
        {
            new JsonObject { ["label"] = "레버 변경", ["value"] = tuning.ChangedLevers.Count.ToString(CultureInfo.InvariantCulture) },
            new JsonObject { ["label"] = "탐색 후보 수", ["value"] = tuning.CandidatesUsed.ToString(CultureInfo.InvariantCulture) },
            new JsonObject { ["label"] = "재시작 시도", ["value"] = tuning.RestartAttempts.ToString(CultureInfo.InvariantCulture) },
            new JsonObject { ["label"] = "예상 비용", ["value"] = "$0.00" },
        },
    };
}

// rule-engine 강등 시 쓰는 고정 제목.
static string FallbackTuningTitle(TuningResult tuning)
{
    return tuning.ReachedBand ? "자동 밸런스 튜닝 제안" : "자동 밸런스 튜닝 — 최선 후보, 밴드 미달";
}

// rule-engine 강등 시 쓰는 고정 요약(밴드 실패 시 잔여 위반을 포함).
static string FallbackTuningSummary(TuningResult tuning)
{
    if (tuning.ReachedBand)
    {
        return $"레버 {tuning.ChangedLevers.Count}개를 조정해 모든 지표가 밴드 안에 들어오도록 예측됐다.";
    }

    var residual = tuning.ResidualViolations.Count == 0 ? "없음" : string.Join(", ", tuning.ResidualViolations);
    return $"레버 {tuning.ChangedLevers.Count}개를 조정한 최선 후보지만 아직 밴드에 도달하지 못했다. 잔여 위반: {residual}.";
}

// 밴드 미달 후보의 note에 최선 후보 표시를 보강한다.
static string TuningNote(TuningResult tuning, string note)
{
    if (tuning.ReachedBand || note.Contains("최선 후보", StringComparison.Ordinal))
    {
        return note;
    }

    return $"{note} 최선 후보, 밴드 미달.";
}

// rule-engine 강등 시 쓰는 고정 레버 note.
static string FallbackLeverNote(LeverChange change)
{
    return $"탐색기가 {change.Path}을(를) {change.Before.ToString("0.###", CultureInfo.InvariantCulture)}에서 {change.After.ToString("0.###", CultureInfo.InvariantCulture)}로 조정(자동 서술 실패로 rule-engine이 대신 기록)";
}

// 실행자로 제안을 생성하고 실패 시 rule-engine으로 강등한다.
static ProposalGeneration GenerateProposalWithFallback(JsonObject definition, JsonObject currentProposal, List<MetricCheck> violations, JsonObject? previousReviewReport)
{
    var revisionOf = currentProposal["lifecycle"]?.GetValue<string>() == "submitted" ? currentProposal["id"]?.GetValue<string>() : null;
    var generated = OllamaExecutor.Generate(definition, violations, previousReviewReport);

    if (!generated.Unavailable)
    {
        var proposal = new JsonObject
        {
            ["schemaVersion"] = 2,
            ["id"] = $"proposal-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            ["title"] = generated.Title,
            ["lifecycle"] = "submitted",
            ["createdBy"] = new JsonObject { ["provider"] = generated.Provider, ["model"] = generated.Model },
            ["revisionOf"] = revisionOf,
            ["summary"] = generated.Summary,
            ["assumptions"] = StringArray(generated.Assumptions),
            ["changes"] = BuildExecutorChanges(violations, generated.Notes),
            ["impact"] = new JsonArray
            {
                new JsonObject { ["label"] = "위반 항목", ["value"] = violations.Count.ToString(CultureInfo.InvariantCulture) },
                new JsonObject { ["label"] = "예상 비용", ["value"] = "$0.00" },
            },
        };

        return new ProposalGeneration(proposal, GeneratedLogEntry(generated.Provider, generated.Model, generated.DurationMs, false, null, generated.SelfReviewed, generated.SelfReviewPassed));
    }

    var fallbackProposal = CreateMeasurementProposal(currentProposal, violations);
    return new ProposalGeneration(fallbackProposal, GeneratedLogEntry("rule-engine", null, generated.DurationMs, true, generated.Error, generated.SelfReviewed, generated.SelfReviewPassed));
}

// 실행자 생성 결과로 changes 배열을 만든다. 수치는 서버가 채운다.
static JsonArray BuildExecutorChanges(List<MetricCheck> violations, Dictionary<string, string> notes)
{
    var changes = new JsonArray();

    foreach (var violation in violations)
    {
        changes.Add(new JsonObject
        {
            ["path"] = violation.MetricId,
            ["before"] = violation.Value is null ? null : Engine.CloneNode(violation.Value),
            ["after"] = violation.Goal is null ? violation.Expected : Engine.CloneNode(violation.Goal),
            ["note"] = notes.GetValueOrDefault(violation.MetricId, ""),
        });
    }

    return changes;
}

// 제안 생성 로그 항목을 만든다.
static JsonObject GeneratedLogEntry(string provider, string? model, long durationMs, bool fallback, string? error, bool selfReviewed, bool selfReviewPassed)
{
    return new JsonObject
    {
        ["event"] = "proposal.generated",
        ["params"] = new JsonObject
        {
            ["provider"] = provider,
            ["model"] = model,
            ["durationMs"] = durationMs,
            ["fallback"] = fallback,
            ["selfReviewed"] = selfReviewed,
            ["selfReviewPassed"] = selfReviewPassed,
            ["reasonCode"] = fallback ? "system.executor_degraded" : "",
            ["text"] = fallback ? (error ?? "") : "",
            ["failReason"] = fallback ? (error ?? "") : "",
        },
        ["level"] = fallback ? "warning" : "info",
        ["producedBy"] = new JsonObject { ["provider"] = provider, ["model"] = model },
        ["cost"] = RuntimeCost(),
    };
}

// 제안 생성 완료 로그 항목을 만든다.
static JsonObject ProposalCreatedLog(JsonObject proposal)
{
    var createdBy = proposal["createdBy"] as JsonObject;
    return new JsonObject
    {
        ["event"] = "proposal.created",
        ["params"] = new JsonObject { ["proposalId"] = proposal["id"]?.GetValue<string>() ?? "" },
        ["level"] = "info",
        ["producedBy"] = new JsonObject
        {
            ["provider"] = createdBy?["provider"]?.GetValue<string>() ?? "rule-engine",
            ["model"] = createdBy?["model"]?.DeepClone(),
        },
        ["cost"] = RuntimeCost(),
    };
}

// 직전 측정에서는 충족했으나 이번 측정에서 위반으로 바뀐 metric을 찾는다.
static List<MetricRegression> DetectRegressions(List<MetricCheck> previousChecks, List<MetricCheck> currentChecks)
{
    var previousByMetric = previousChecks
        .Where(check => check.Implemented)
        .ToDictionary(check => check.MetricId, StringComparer.Ordinal);
    var regressions = new List<MetricRegression>();

    foreach (var current in currentChecks.Where(check => check.Implemented && !check.Passed))
    {
        if (previousByMetric.TryGetValue(current.MetricId, out var previous) && previous.Passed)
        {
            regressions.Add(new MetricRegression(current.MetricId, previous.Value, current.Value, current.Goal, current.Expected, current.Evidence));
        }
    }

    return regressions;
}

// 직전 측정 이후 사람이 승인한 가장 최근 proposal id를 찾는다. 없으면 null(원인 미상).
static string? FindRecentApprovedProposalId(JsonObject reviews, JsonObject previousMeasurement)
{
    var sinceText = previousMeasurement["measuredAt"]?.GetValue<string>();
    var since = DateTimeOffset.TryParse(sinceText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedSince)
        ? parsedSince
        : DateTimeOffset.MinValue;

    return (reviews["reports"]?.AsArray() ?? new JsonArray())
        .OfType<JsonObject>()
        .Where(report => report["verdict"]?.GetValue<string>() == "approved" &&
            report["reviewer"]?.AsObject()["type"]?.GetValue<string>() == "human" &&
            DateTimeOffset.TryParse(report["createdAt"]?.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var createdAt) &&
            createdAt >= since)
        .OrderByDescending(report => report["createdAt"]?.GetValue<string>(), StringComparer.Ordinal)
        .Select(report => report["proposalId"]?.GetValue<string>())
        .FirstOrDefault();
}

// 악화 metric 목록으로 롤백 제안을 만든다. rule-engine이 역산하며 Ollama를 쓰지 않는다.
static JsonObject CreateRollbackProposal(List<MetricRegression> regressions, string? relatedProposalId)
{
    var changes = new JsonArray();
    var metricNames = new List<string>();
    var suspectText = relatedProposalId ?? "원인 미상";

    foreach (var regression in regressions)
    {
        metricNames.Add(regression.MetricId);
        changes.Add(new JsonObject
        {
            ["path"] = regression.MetricId,
            ["before"] = regression.CurrentValue is null ? null : Engine.CloneNode(regression.CurrentValue),
            ["after"] = regression.PreviousValue is null ? null : Engine.CloneNode(regression.PreviousValue),
            ["note"] = $"직전 승인 {suspectText} 이후 {regression.MetricId}이 {ValueTextOrNone(regression.PreviousValue)}→{ValueTextOrNone(regression.CurrentValue)}로 악화됨. 되돌리거나 원인 조사 필요",
        });
    }

    return new JsonObject
    {
        ["schemaVersion"] = 2,
        ["id"] = $"proposal-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
        ["title"] = $"악화 롤백 제안: {string.Join(", ", metricNames)}",
        ["lifecycle"] = "submitted",
        ["kind"] = "rollback",
        ["createdBy"] = new JsonObject { ["provider"] = "rule-engine", ["model"] = null },
        ["revisionOf"] = relatedProposalId,
        ["summary"] = "직전에 충족되던 지표가 악화되어 자동 롤백을 제안한다. 체크리스트가 검토할 내용이 없어 1층을 건너뛰고 사람 결재로 직행한다.",
        ["changes"] = changes,
        ["impact"] = new JsonArray
        {
            new JsonObject { ["label"] = "악화 항목", ["value"] = regressions.Count.ToString(CultureInfo.InvariantCulture) },
            new JsonObject { ["label"] = "예상 비용", ["value"] = "$0.00" },
        },
    };
}

// 악화 라우팅을 검토 단계 상세에 반영한다(1층 검토를 건너뛴 사실을 표시).
static void SetRegressionReviewDetails(JsonObject state, string? stageId, List<MetricRegression> regressions)
{
    if (stageId is null)
    {
        return;
    }

    var issues = regressions
        .Select(regression => $"{regression.MetricId}: {ValueTextOrNone(regression.PreviousValue)}→{ValueTextOrNone(regression.CurrentValue)}로 악화")
        .ToList();

    state["stageDetails"] ??= new JsonObject();
    state["stageDetails"]!.AsObject()[stageId] = new JsonObject
    {
        ["summary"] = "악화가 감지되어 1층 검토를 건너뛰고 사람 결재로 직행했다.",
        ["metrics"] = new JsonArray { new JsonObject { ["label"] = "라우팅", ["value"] = "사람 직행" } },
        ["issues"] = new JsonArray(issues.Select(issue => (JsonNode)issue).ToArray()),
    };
}

// suspendedTracks에 악화 항목을 추가한다(이미 있으면 건너뜀).
static void AddSuspendedTrack(JsonObject state, MetricRegression regression, string? relatedProposalId)
{
    var tracks = state["suspendedTracks"]?.AsArray() ?? new JsonArray();
    state["suspendedTracks"] = tracks;

    var alreadySuspended = tracks.OfType<JsonObject>().Any(track => track["metricId"]?.GetValue<string>() == regression.MetricId);

    if (alreadySuspended)
    {
        return;
    }

    tracks.Add(new JsonObject
    {
        ["metricId"] = regression.MetricId,
        ["detectedAt"] = DateTimeOffset.Now.ToString("O"),
        ["relatedProposalId"] = relatedProposalId,
        ["suspectConfidence"] = "temporal",
    });
}

// suspendedTracks 중 충족으로 복귀한 항목을 해제하고 로그를 남긴다.
static JsonObject ResumeSatisfiedTracks(JsonObject state, JsonObject runLog, List<MetricCheck> checks)
{
    var tracks = state["suspendedTracks"]?.AsArray();

    if (tracks is null || tracks.Count == 0)
    {
        return runLog;
    }

    var checksByMetric = checks.Where(check => check.Implemented).ToDictionary(check => check.MetricId, StringComparer.Ordinal);
    var remaining = new JsonArray();
    var nextRunLog = runLog;

    foreach (var track in tracks.OfType<JsonObject>())
    {
        var metricId = track["metricId"]?.GetValue<string>() ?? "";

        if (checksByMetric.TryGetValue(metricId, out var check) && check.Passed)
        {
            nextRunLog = Engine.AppendLog(nextRunLog, new JsonObject
            {
                ["event"] = "track.resumed",
                ["params"] = new JsonObject { ["metricId"] = metricId },
                ["level"] = "info",
                ["producedBy"] = new JsonObject { ["provider"] = "rule-engine", ["model"] = null },
                ["cost"] = RuntimeCost(),
            }, Engine.GetLoopIteration(state));
        }
        else
        {
            remaining.Add(Engine.CloneNode(track));
        }
    }

    state["suspendedTracks"] = remaining;
    return nextRunLog;
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
        .LastOrDefault(condition => condition["check"]?.GetValue<string>() == "stageStatus")?["stage"]
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
static IResult Approve(Storage storage, string projectId, JsonObject body, JsonSerializerOptions jsonOptions, NtfyOptions ntfy, GitDataCommitOptions gitDataCommitOptions)
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
    var nextStageId = nextStage?["id"]?.GetValue<string>();
    var entersApplyStage = nextStageId is not null && nextStageId == ResolveMeasurementStages(bundle.Definition).ApplyStageId;
    var approvePatch = new JsonObject();
    if (nextStage is null)
    {
        approvePatch["overallStatus"] = "completed";
    }
    else
    {
        approvePatch["currentStage"] = nextStageId;
        approvePatch["stageStatuses"] = new JsonObject { [nextStageId!] = "in_progress" };
        approvePatch["overallStatus"] = "in_progress";

        if (entersApplyStage)
        {
            // 적용 단계로 들어갈 때 현재 위반 집합을 기준선으로 남긴다 — 재측정만으로
            // 아직 아무것도 안 고쳤는데 새 검토가 열리는 것을 막기 위함(ViolationSignatureUnchanged).
            var currentViolations = EvaluateBlueprintChecks(bundle.Blueprint, bundle.Measurement).Where(check => check.Implemented && !check.Passed).ToList();
            approvePatch["applyBaselineViolations"] = BuildViolationSignature(currentViolations);
        }
    }

    state = Engine.ApplyStatePatch(bundle.Definition, state, approvePatch);

    if (entersApplyStage)
    {
        SetApplyStageDetails(state, nextStageId);
    }

    bundle.State = state;
    bundle.RunLog = runLog;
    var isTuningApproval = bundle.Proposal["kind"]?.GetValue<string>() == "tuning";
    var predictedMetrics = isTuningApproval ? bundle.Proposal["predictedMetrics"]?.AsArray() : null;
    var tuningChanges = isTuningApproval ? bundle.Proposal["changes"]?.AsArray() : null;
    bundle.Proposal["lifecycle"] = "decided";
    AppendReport(bundle.Reviews, report);
    var result = Persist(storage, projectId, bundle, jsonOptions, ntfy);
    var committedBundle = bundle;

    if (isTuningApproval && tuningChanges is not null && tuningChanges.Count > 0)
    {
        ApplyTuningChanges(storage, projectId, tuningChanges);
        var remeasure = RunMeasureCore(storage, projectId, jsonOptions, ntfy);

        if (remeasure.Bundle is not null)
        {
            var finalBundle = remeasure.Bundle;
            finalBundle.RunLog = Engine.AppendLog(finalBundle.RunLog, BuildTuningAppliedLog(predictedMetrics, finalBundle.Measurement), Engine.GetLoopIteration(finalBundle.State));
            result = Persist(storage, projectId, finalBundle, jsonOptions, ntfy);
            committedBundle = finalBundle;
        }
    }

    GitDataCommitter.CommitHumanAction(gitDataCommitOptions, projectId, Engine.GetLoopIteration(committedBundle.State), "approve", proposalId);
    return result;
}

// 승인된 튜닝 제안의 레버 변경을 game-data.json에 실제로 기록한다(원자 쓰기는 Storage가 담당).
static void ApplyTuningChanges(Storage storage, string projectId, JsonArray changes)
{
    var gameDataPath = storage.ProjectFilePath(projectId, Storage.GameDataFile);

    if (!File.Exists(gameDataPath))
    {
        return;
    }

    var gameData = JsonNode.Parse(File.ReadAllText(gameDataPath))!.AsObject();

    foreach (var change in changes.OfType<JsonObject>())
    {
        var path = change["path"]?.GetValue<string>();
        var after = change["after"];

        if (string.IsNullOrWhiteSpace(path) || after is null)
        {
            continue;
        }

        var afterValue = double.Parse(after.ToString(), CultureInfo.InvariantCulture);
        var isInteger = after is JsonValue afterJsonValue && afterJsonValue.TryGetValue<int>(out _);
        BalanceTuner.SetLeverValue(gameData, path, afterValue, isInteger);
    }

    storage.WriteProjectFile(projectId, Storage.GameDataFile, gameData);
}

// 튜닝 승인 후 재측정한 실측값과 예측값을 비교하는 로그 항목을 만든다.
static JsonObject BuildTuningAppliedLog(JsonArray? predictedMetrics, JsonObject actualMeasurement)
{
    var actualByMetric = (actualMeasurement["metrics"]?.AsArray() ?? new JsonArray())
        .OfType<JsonObject>()
        .ToDictionary(metric => metric["metricId"]?.GetValue<string>() ?? "", metric => metric["value"]);

    var comparisons = new JsonArray();
    foreach (var predicted in predictedMetrics?.OfType<JsonObject>() ?? [])
    {
        var metricId = predicted["metricId"]?.GetValue<string>() ?? "";
        var predictedAfter = predicted["after"];
        actualByMetric.TryGetValue(metricId, out var actualValue);
        comparisons.Add(new JsonObject
        {
            ["metricId"] = metricId,
            ["predicted"] = predictedAfter is null ? null : Engine.CloneNode(predictedAfter),
            ["actual"] = actualValue is null ? null : Engine.CloneNode(actualValue),
        });
    }

    return new JsonObject
    {
        ["event"] = "tuning.applied",
        ["params"] = new JsonObject { ["comparisons"] = comparisons },
        ["level"] = "info",
        ["producedBy"] = new JsonObject { ["provider"] = "rule-engine", ["model"] = null },
        ["cost"] = RuntimeCost(),
    };
}

// 거절 액션을 처리하고 결과 파일을 기록한다.
static IResult Reject(Storage storage, string projectId, JsonObject body, JsonSerializerOptions jsonOptions, NtfyOptions ntfy, GitDataCommitOptions gitDataCommitOptions)
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

    runLog = ClearRejectedSuspendedTracks(state, bundle.Proposal, runLog);
    bundle.State = state;
    bundle.RunLog = runLog;
    bundle.Proposal["lifecycle"] = "decided";
    AppendReport(bundle.Reviews, report);
    var result = Persist(storage, projectId, bundle, jsonOptions, ntfy);
    GitDataCommitter.CommitHumanAction(gitDataCommitOptions, projectId, Engine.GetLoopIteration(bundle.State), "reject", proposalId);
    return result;
}

// 거절된 제안이 다루던 metric의 suspendedTracks 항목을 정리한다("유지하고 관찰" 선택).
static JsonObject ClearRejectedSuspendedTracks(JsonObject state, JsonObject proposal, JsonObject runLog)
{
    var tracks = state["suspendedTracks"]?.AsArray();

    if (tracks is null || tracks.Count == 0)
    {
        return runLog;
    }

    var rejectedMetricIds = (proposal["changes"]?.AsArray() ?? new JsonArray())
        .OfType<JsonObject>()
        .Select(change => change["path"]?.GetValue<string>())
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .ToHashSet(StringComparer.Ordinal);

    if (rejectedMetricIds.Count == 0)
    {
        return runLog;
    }

    var remaining = new JsonArray();
    var nextRunLog = runLog;

    foreach (var track in tracks.OfType<JsonObject>())
    {
        var metricId = track["metricId"]?.GetValue<string>() ?? "";

        if (rejectedMetricIds.Contains(metricId))
        {
            nextRunLog = Engine.AppendLog(nextRunLog, new JsonObject
            {
                ["event"] = "track.dismissed",
                ["params"] = new JsonObject { ["metricId"] = metricId, ["reasonCode"] = "review.rejected" },
                ["level"] = "info",
                ["producedBy"] = new JsonObject { ["provider"] = "human", ["model"] = null },
                ["cost"] = RuntimeCost(),
            }, Engine.GetLoopIteration(state));
        }
        else
        {
            remaining.Add(Engine.CloneNode(track));
        }
    }

    state["suspendedTracks"] = remaining;
    return nextRunLog;
}

// 변경 항목 편집 액션을 처리하고 결과 파일을 기록한다.
static IResult EditChange(Storage storage, string projectId, JsonObject body, JsonSerializerOptions jsonOptions, NtfyOptions ntfy, GitDataCommitOptions gitDataCommitOptions)
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
    var result = Persist(storage, projectId, bundle, jsonOptions, ntfy);
    var proposalId = bundle.Proposal["id"]?.GetValue<string>();
    GitDataCommitter.CommitHumanAction(gitDataCommitOptions, projectId, Engine.GetLoopIteration(bundle.State), "edit-change", proposalId);
    return result;
}

// 확인 액션을 처리하고 결과 파일을 기록한다.
static IResult Acknowledge(Storage storage, string projectId, JsonObject body, JsonSerializerOptions jsonOptions, GitDataCommitOptions gitDataCommitOptions)
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
        var result = PersistWithoutGuardrail(storage, projectId, bundle, jsonOptions);
        var proposalId = bundle.Proposal["id"]?.GetValue<string>();
        GitDataCommitter.CommitHumanAction(gitDataCommitOptions, projectId, Engine.GetLoopIteration(bundle.State), "acknowledge-checkpoint", proposalId);
        return result;
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
        var result = PersistWithoutGuardrail(storage, projectId, bundle, jsonOptions);
        var proposalId = bundle.Proposal["id"]?.GetValue<string>();
        GitDataCommitter.CommitHumanAction(gitDataCommitOptions, projectId, Engine.GetLoopIteration(bundle.State), "acknowledge-guardrail", proposalId);
        return result;
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

    if (bundle.Proposal.Count == 0 || bundle.Proposal["id"] is null)
    {
        return ProblemResult(409, "review.no_proposal", "No proposal is available");
    }

    if (bundle.Proposal["lifecycle"]?.GetValue<string>() == "decided")
    {
        return ProblemResult(409, "review.already_decided", "Proposal is already decided");
    }

    var stageId = reviewStage["id"]!.GetValue<string>();
    var status = Engine.GetStageStatus(bundle.State, stageId);

    if (status != "pending_review")
    {
        return ProblemResult(409, "review.not_pending", "Review stage is not pending");
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
static IResult Persist(Storage storage, string projectId, ProjectBundle bundle, JsonSerializerOptions jsonOptions, NtfyOptions ntfy)
{
    var guarded = Guardrails.Enforce(bundle.Definition, bundle.State, bundle.RunLog);
    bundle.State = guarded.State;
    bundle.RunLog = guarded.RunLog;
    NotifyGuardrailTransition(ntfy, bundle, guarded);
    storage.WriteBundle(projectId, bundle);
    storage.SaveLoopSnapshot(projectId, bundle);
    return BundleResult(bundle, jsonOptions);
}

// 가드레일 정지·체크포인트 일시정지가 이번 호출에서 방금 발생했으면 알린다.
static void NotifyGuardrailTransition(NtfyOptions ntfy, ProjectBundle bundle, GuardrailResult guarded)
{
    if (!guarded.Changed)
    {
        return;
    }

    var lastEntry = guarded.RunLog["entries"]?.AsArray().OfType<JsonObject>().LastOrDefault();
    var lastEvent = lastEntry?["event"]?.GetValue<string>();
    var projectName = ProjectDisplayName(bundle.State);

    if (lastEvent == "guardrail.halted")
    {
        var text = lastEntry?["params"]?.AsObject()["text"]?.GetValue<string>() ?? "";
        Notifier.NotifyGuardrailHalted(ntfy, projectName, text);
    }
    else if (lastEvent == "checkpoint.paused")
    {
        var checkpointId = lastEntry?["params"]?.AsObject()["checkpointId"]?.GetValue<string>() ?? "";
        Notifier.NotifyCheckpointPaused(ntfy, projectName, checkpointId);
    }
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
        ["cycleSummary"] = BuildCycleSummary(bundle.State, bundle.RunLog, bundle.Proposal),
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

// 자기 리팩터링 dispatch의 기본 지시문을 반환한다.
static string DefaultSelfRefactorInstruction()
{
    return "Program.cs를 Orchestrator.cs/ProposalFlow.cs로 분리, 이동만·로직 무변경, app.js 동일 원칙으로 분리. Completion check: dotnet run --project server -- verify-behavior 통과, dotnet run --project server -- measure dev-pack 무위반, 구조 지표 밴드 진입.";
}

// dispatch 승격 이벤트를 프로젝트 run-log에 기록한다.
static void RecordDispatchEscalation(Storage storage, string projectId, string fromTaskId, JsonSerializerOptions jsonOptions)
{
    var bundle = storage.ReadBundle(projectId);
    storage.CreateRestorePoint(projectId);
    bundle.RunLog = Engine.AppendLog(bundle.RunLog, new JsonObject
    {
        ["event"] = "executor.escalated",
        ["params"] = new JsonObject
        {
            ["from"] = "ollama",
            ["to"] = "claude-code",
            ["taskId"] = fromTaskId,
            ["reasonCode"] = "dispatch.strict_gate_failed",
        },
        ["level"] = "warning",
        ["producedBy"] = new JsonObject { ["provider"] = "rule-engine", ["model"] = null },
        ["cost"] = RuntimeCost(),
    }, Engine.GetLoopIteration(bundle.State));
    storage.WriteProjectFile(projectId, Storage.RunLogFile, bundle.RunLog);
    storage.SaveLoopSnapshot(projectId, bundle);
}

// dispatch 작업 결과를 HTTP 응답으로 변환한다.
static IResult DispatchResult(Func<JsonObject> action, JsonSerializerOptions jsonOptions)
{
    try
    {
        return JsonResult(action(), jsonOptions);
    }
    catch (DispatchHttpException error)
    {
        return ProblemResult(error.StatusCode, error.ReasonCode, error.Message);
    }
    catch (Exception error)
    {
        return ProblemResult(500, "dispatch.failed", error.Message);
    }
}

// 비동기 dispatch 작업 결과를 HTTP 응답으로 변환한다.
static async Task<IResult> DispatchResultAsync(Func<Task<JsonObject>> action, JsonSerializerOptions jsonOptions)
{
    try
    {
        return JsonResult(await action(), jsonOptions);
    }
    catch (DispatchHttpException error)
    {
        return ProblemResult(error.StatusCode, error.ReasonCode, error.Message);
    }
    catch (Exception error)
    {
        return ProblemResult(500, "dispatch.failed", error.Message);
    }
}

// state 객체에서 프로젝트 표시 이름을 읽는다.
static string ProjectDisplayName(JsonObject state)
{
    return state["projectName"]?.GetValue<string>() ?? "project";
}

// 프로젝트 ID로 상태 파일을 읽어 표시 이름을 얻는다.
static string ReadProjectDisplayName(Storage storage, string projectId)
{
    try
    {
        return ProjectDisplayName(storage.ReadProjectFile(projectId, Storage.StateFile).AsObject());
    }
    catch
    {
        return projectId;
    }
}

// 제안 제목을 읽는다.
static string ProposalTitle(JsonObject proposal)
{
    return proposal["title"]?.GetValue<string>() ?? "제안";
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

// 값이 없을 수 있는 노드를 표시용 문자열로 만든다.
static string ValueTextOrNone(JsonNode? node)
{
    return node is null ? "없음" : ValueText(node);
}

// 문자열 목록을 JSON 배열로 만든다.
static JsonArray StringArray(IEnumerable<string> values)
{
    return new JsonArray(values
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Take(5)
        .Select(value => (JsonNode)value)
        .ToArray());
}

public sealed record MeasurementStages(string? MeasureStageId, string? DeviationStageId, string? ReviewStageId, string? ApplyStageId);

public sealed record MetricCheck(string MetricId, JsonNode? Value, JsonNode? Goal, bool Implemented, bool Passed, string Expected, List<string> Evidence);

public sealed record MetricRegression(string MetricId, JsonNode? PreviousValue, JsonNode? CurrentValue, JsonNode? Goal, string Expected, List<string> Evidence);

public sealed record ProposalGeneration(JsonObject Proposal, JsonObject LogEntry);

public sealed record MeasureOutcome(ProjectBundle? Bundle, IResult? Problem, int ViolationCount);
