// 프로젝트 JSON 파일을 원자적으로 읽고 쓴다.
// 복원 지점과 루프 스냅샷을 관리한다.
using System.Collections.Concurrent;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

public sealed class Storage
{
    public const string DefinitionFile = "workflow-definition.json";
    public const string StateFile = "workflow-state.json";
    public const string RunLogFile = "run-log.json";
    public const string ProposalFile = "patch-proposal.json";
    public const string ReviewFile = "review-report.json";
    public const string ScenarioFile = "scenario.json";
    public const string BlueprintFile = "blueprint.json";
    public const string MeasurementFile = "measurement.json";

    private static readonly string[] CoreFiles = [StateFile, RunLogFile, ProposalFile, ReviewFile, MeasurementFile];
    private static readonly string[] StartupCheckedFiles = [DefinitionFile, StateFile, RunLogFile, ProposalFile, ReviewFile, ScenarioFile, BlueprintFile, MeasurementFile];
    private readonly ConcurrentDictionary<string, object> projectLocks = new();

    public string DataRoot { get; }
    public JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // 데이터 루트 경로를 저장한다.
    public Storage(string dataRoot)
    {
        DataRoot = Path.GetFullPath(dataRoot);
    }

    // 프로젝트별 직렬화 lock 객체를 반환한다.
    public object GetProjectLock(string projectId)
    {
        return projectLocks.GetOrAdd(projectId, _ => new object());
    }

    // projects.json을 읽는다.
    public JsonObject ReadProjects()
    {
        return ReadJson(Path.Combine(DataRoot, "projects.json")).AsObject();
    }

    // 프로젝트 파일 하나를 읽는다.
    public JsonNode ReadProjectFile(string projectId, string fileName)
    {
        return ReadJson(ProjectFilePath(projectId, fileName));
    }

    // 프로젝트 작업 파일 묶음을 읽는다.
    public ProjectBundle ReadBundle(string projectId)
    {
        return new ProjectBundle(
            ReadProjectFile(projectId, DefinitionFile).AsObject(),
            ReadProjectFile(projectId, StateFile).AsObject(),
            ReadProjectFile(projectId, RunLogFile).AsObject(),
            ReadOptionalProjectFile(projectId, ProposalFile, () => new JsonObject { ["schemaVersion"] = 2 }).AsObject(),
            ReadOptionalProjectFile(projectId, ReviewFile, () => new JsonObject { ["schemaVersion"] = 2, ["reports"] = new JsonArray() }).AsObject(),
            ReadOptionalProjectFile(projectId, BlueprintFile, () => new JsonObject { ["schemaVersion"] = 2, ["items"] = new JsonArray() }).AsObject(),
            ReadOptionalProjectFile(projectId, MeasurementFile, () => new JsonObject { ["schemaVersion"] = 2, ["metrics"] = new JsonArray() }).AsObject()
        );
    }

    // 프로젝트 작업 파일 묶음을 쓴다.
    public void WriteBundle(string projectId, ProjectBundle bundle)
    {
        WriteProjectFile(projectId, StateFile, bundle.State);
        WriteProjectFile(projectId, RunLogFile, bundle.RunLog);
        WriteProjectFile(projectId, ProposalFile, bundle.Proposal);
        WriteProjectFile(projectId, ReviewFile, bundle.Reviews);
        if (ShouldWriteMeasurement(projectId, bundle.Measurement))
        {
            WriteProjectFile(projectId, MeasurementFile, bundle.Measurement);
        }
    }

    // 상태 변경 전 복원 지점을 만든다.
    public string CreateRestorePoint(string projectId)
    {
        var restoreDirectory = Path.Combine(ProjectHistoryPath(projectId), $"restore-{Timestamp()}");
        Directory.CreateDirectory(restoreDirectory);

        foreach (var fileName in CoreFiles)
        {
            var source = ProjectFilePath(projectId, fileName);
            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(restoreDirectory, fileName), overwrite: true);
            }
        }

        return restoreDirectory;
    }

    // 현재 루프 결과 스냅샷을 저장한다.
    public void SaveLoopSnapshot(string projectId, ProjectBundle bundle)
    {
        var loopIteration = Engine.GetLoopIteration(bundle.State);
        var snapshotDirectory = Path.Combine(ProjectHistoryPath(projectId), $"loop-{loopIteration}");
        Directory.CreateDirectory(snapshotDirectory);
        WriteJson(Path.Combine(snapshotDirectory, StateFile), bundle.State);
        WriteJson(Path.Combine(snapshotDirectory, RunLogFile), bundle.RunLog);
        WriteJson(Path.Combine(snapshotDirectory, ProposalFile), bundle.Proposal);
        WriteJson(Path.Combine(snapshotDirectory, ReviewFile), bundle.Reviews);
        if (ShouldWriteMeasurement(projectId, bundle.Measurement))
        {
            WriteJson(Path.Combine(snapshotDirectory, MeasurementFile), bundle.Measurement);
        }
    }

    // 서버 시작 시 JSON 파일을 검증하고 최신 복원 지점으로 복구한다.
    public void ValidateAndRestoreAllProjects()
    {
        foreach (var project in ProjectIds())
        {
            lock (GetProjectLock(project))
            {
                try
                {
                    ValidateAndRestoreProject(project);
                }
                catch (Exception error)
                {
                    Console.Error.WriteLine($"[warning] Project validation skipped for {project}: {error.Message}");
                }
            }
        }
    }

    // 프로젝트 파일 경로를 계산한다.
    public string ProjectFilePath(string projectId, string fileName)
    {
        var projectPath = ProjectPath(projectId);
        var fullPath = Path.GetFullPath(Path.Combine(projectPath, fileName));

        if (!fullPath.StartsWith(projectPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Project file path is outside the project folder.");
        }

        return fullPath;
    }

    // 프로젝트 폴더 경로를 계산한다.
    public string ProjectPath(string projectId)
    {
        var projects = ReadProjects();
        var entries = projects["projects"]?.AsArray() ?? new JsonArray();
        var configuredPath = entries
            .OfType<JsonObject>()
            .FirstOrDefault(project => string.Equals(project["id"]?.GetValue<string>(), projectId, StringComparison.OrdinalIgnoreCase))?["path"]
            ?.GetValue<string>();

        if (configuredPath is null)
        {
            throw new InvalidOperationException($"Project is not registered: {projectId}");
        }

        var normalized = configuredPath.Replace('\\', '/').Trim();
        string relativePath;

        if (normalized.StartsWith("./data/", StringComparison.OrdinalIgnoreCase))
        {
            relativePath = normalized["./data/".Length..];
        }
        else if (normalized.StartsWith("data/", StringComparison.OrdinalIgnoreCase))
        {
            relativePath = normalized["data/".Length..];
        }
        else
        {
            relativePath = projectId;
        }

        var fullPath = Path.GetFullPath(Path.Combine(DataRoot, relativePath));

        if (!fullPath.StartsWith(DataRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Project path is outside the data folder.");
        }

        return fullPath;
    }

    // 프로젝트 파일을 원자적으로 쓴다.
    public void WriteProjectFile(string projectId, string fileName, JsonNode node)
    {
        WriteJson(ProjectFilePath(projectId, fileName), node);
    }

    // 선택 프로젝트 파일을 읽고 없으면 대체값을 만든다.
    private JsonNode ReadOptionalProjectFile(string projectId, string fileName, Func<JsonNode> fallback)
    {
        var path = ProjectFilePath(projectId, fileName);
        return File.Exists(path) ? ReadJson(path) : fallback();
    }

    // 측정 파일을 실제로 기록해야 하는지 확인한다.
    private bool ShouldWriteMeasurement(string projectId, JsonObject measurement)
    {
        return File.Exists(ProjectFilePath(projectId, MeasurementFile)) ||
            (measurement["metrics"]?.AsArray().Count ?? 0) > 0;
    }

    // JSON 파일을 읽고 파싱한다.
    private JsonNode ReadJson(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonNode.Parse(stream) ?? throw new InvalidOperationException($"Empty JSON file: {path}");
    }

    // JSON 파일을 임시 파일에 쓴 뒤 교체한다.
    private void WriteJson(string path, JsonNode node)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(tempPath, node.ToJsonString(JsonOptions) + Environment.NewLine, new UTF8Encoding(false));

        if (File.Exists(path))
        {
            try
            {
                File.Replace(tempPath, path, null);
                return;
            }
            catch (PlatformNotSupportedException)
            {
            }
            catch (IOException)
            {
            }
        }

        File.Move(tempPath, path, overwrite: true);
    }

    // 프로젝트 이력 폴더 경로를 반환한다.
    private string ProjectHistoryPath(string projectId)
    {
        return Path.Combine(ProjectPath(projectId), "history");
    }

    // 등록된 프로젝트 ID 목록을 반환한다.
    private IEnumerable<string> ProjectIds()
    {
        return (ReadProjects()["projects"]?.AsArray() ?? new JsonArray())
            .OfType<JsonObject>()
            .Select(project => project["id"]?.GetValue<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))!;
    }

    // 프로젝트 JSON 파일을 검증하고 필요하면 복원한다.
    private void ValidateAndRestoreProject(string projectId)
    {
        var brokenFiles = StartupCheckedFiles
            .Where(fileName => File.Exists(ProjectFilePath(projectId, fileName)))
            .Where(fileName => !CanParse(ProjectFilePath(projectId, fileName)))
            .ToList();

        if (brokenFiles.Count == 0)
        {
            return;
        }

        var restoredFrom = TryRestoreLatest(projectId);

        if (restoredFrom is null)
        {
            Console.Error.WriteLine($"[warning] Project {projectId} has broken JSON but no restore point. Startup continues without restoring this project.");
            return;
        }

        var bundle = ReadBundle(projectId);
        bundle.RunLog = Engine.AppendLog(
            bundle.RunLog,
            new JsonObject
            {
                ["event"] = "system.restored",
                ["params"] = new JsonObject
                {
                    ["text"] = string.Join(", ", brokenFiles),
                    ["reasonCode"] = "json.parse_failed",
                    ["restoredFrom"] = restoredFrom,
                },
                ["level"] = "warning",
                ["producedBy"] = new JsonObject { ["provider"] = "storage", ["model"] = null },
                ["cost"] = RuntimeCost(),
            },
            Engine.GetLoopIteration(bundle.State)
        );
        WriteBundle(projectId, bundle);
    }

    // JSON 파싱 가능 여부를 확인한다.
    private static bool CanParse(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            JsonNode.Parse(stream);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // 최신 복원 지점을 프로젝트 폴더로 복사한다.
    private string? TryRestoreLatest(string projectId)
    {
        var historyPath = ProjectHistoryPath(projectId);
        var restorePath = Directory.Exists(historyPath)
            ? Directory.GetDirectories(historyPath, "restore-*").OrderByDescending(path => path).FirstOrDefault()
            : null;

        if (restorePath is null)
        {
            return null;
        }

        foreach (var fileName in CoreFiles)
        {
            var source = Path.Combine(restorePath, fileName);
            if (File.Exists(source))
            {
                File.Copy(source, ProjectFilePath(projectId, fileName), overwrite: true);
            }
        }

        return Path.GetFileName(restorePath);
    }

    // 파일명에 사용할 시각 문자열을 만든다.
    private static string Timestamp()
    {
        return DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ");
    }

    // 런타임 비용 0 객체를 만든다.
    private static JsonObject RuntimeCost()
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
}

public sealed class ProjectBundle
{
    // 작업 파일 묶음을 보관한다.
    public ProjectBundle(JsonObject definition, JsonObject state, JsonObject runLog, JsonObject proposal, JsonObject reviews, JsonObject blueprint, JsonObject measurement)
    {
        Definition = definition;
        State = state;
        RunLog = runLog;
        Proposal = proposal;
        Reviews = reviews;
        Blueprint = blueprint;
        Measurement = measurement;
    }

    public JsonObject Definition { get; set; }
    public JsonObject State { get; set; }
    public JsonObject RunLog { get; set; }
    public JsonObject Proposal { get; set; }
    public JsonObject Reviews { get; set; }
    public JsonObject Blueprint { get; set; }
    public JsonObject Measurement { get; set; }
}
