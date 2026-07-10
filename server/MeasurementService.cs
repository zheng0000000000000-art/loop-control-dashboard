// 측정 실행 본체 — RunMeasureCore와 경로 해석 헬퍼를 담는다.
// ApplyMeasurementResult·Persist는 Program.cs 로컬 함수이므로 위임자로 주입한다.
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;

internal static class MeasurementService
{
    // 측정 결과 반영 함수 주입자.
    internal static Func<ProjectBundle, string, NtfyOptions, JsonObject, string, long, int> ApplyResult = null!;
    // 상태 저장 함수 주입자.
    internal static Action<Storage, string, ProjectBundle, JsonSerializerOptions, NtfyOptions> PersistBundle = null!;

    // 측정 공급자를 실행하고 위반 수와 번들을 반환한다.
    internal static MeasureOutcome RunMeasureCore(Storage storage, string projectId, JsonSerializerOptions jsonOptions, NtfyOptions ntfy)
    {
        var bundle = storage.ReadBundle(projectId);
        var provider = bundle.Definition["measurementProvider"] as JsonObject;
        var providerId = provider?["id"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(providerId))
        {
            return new MeasureOutcome(null, Results.Json(new JsonObject { ["reasonCode"] = "checklist.provider_missing", ["reason"] = "Measurement provider is not configured" }, statusCode: 409), 0);
        }

        if (providerId != "dev-pack-checks" && providerId != "ruined-lab-sim")
        {
            return new MeasureOutcome(null, Results.Json(new JsonObject { ["reasonCode"] = "checklist.provider_unknown", ["reason"] = $"Measurement provider is not supported: {providerId}" }, statusCode: 409), 0);
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
        var violationCount = ApplyResult(bundle, providerId, ntfy, previousMeasurement, storage.ProjectPath(projectId), measureTimer.ElapsedMilliseconds);
        PersistBundle(storage, projectId, bundle, jsonOptions, ntfy);
        return new MeasureOutcome(bundle, null, violationCount);
    }

    // measurementProvider 설정을 실제 디렉터리 경로로 변환한다.
    private static string ResolveMeasurementTargetRoot(string projectPath, JsonObject provider)
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
}
