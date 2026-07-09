// definition에 선언된 레버 범위 안에서 game-data를 그리디 탐색해 blueprint 위반을 줄이는 후보를 찾는다.
// 레버 밖 경로는 절대 건드리지 않는다 — 탐색 대상은 언제나 선언된 레버 목록뿐이다.
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

public static class BalanceTuner
{
    private static readonly Regex PathSegmentPattern = new(@"^(\w+)(?:\[(\d+)\])?$", RegexOptions.Compiled);

    // definition의 tunableLevers를 실제 game-data 구조에 맞춰 구체적인 경로 목록으로 펼친다(rooms[*] 와일드카드 확장).
    public static List<TunableLever> ParseLevers(JsonObject definition, JsonObject gameData)
    {
        var levers = new List<TunableLever>();
        var roomCount = gameData["rooms"]?.AsArray().Count ?? 0;

        foreach (var leverNode in definition["tunableLevers"]?.AsArray().OfType<JsonObject>() ?? [])
        {
            var path = leverNode["path"]?.GetValue<string>() ?? "";
            var min = ParseDouble(leverNode["min"]);
            var max = ParseDouble(leverNode["max"]);
            var step = ParseDouble(leverNode["step"]);

            if (path.Contains("[*]", StringComparison.Ordinal))
            {
                for (var index = 0; index < roomCount; index += 1)
                {
                    levers.Add(new TunableLever(path.Replace("[*]", $"[{index}]"), min, max, step));
                }
            }
            else
            {
                levers.Add(new TunableLever(path, min, max, step));
            }
        }

        return levers;
    }

    // 레버 범위 안에서 game-data를 탐욕 탐색해 blueprint 위반을 줄이는 후보를 찾는다.
    public static TuningResult Search(JsonObject gameData, JsonObject blueprint, JsonObject definition, int seed, Action<string>? onProgress = null)
    {
        var levers = ParseLevers(definition, gameData);
        var tuningConfig = definition["tuning"]?.AsObject();
        var maxCandidates = Number(tuningConfig?["maxCandidates"], 40);
        var dryRunSamples = Number(tuningConfig?["dryRunSamples"], 500);
        var maxLeversPerProposal = Number(tuningConfig?["maxLeversPerProposal"], 3);

        var baseline = CloneGameData(gameData);
        var baselineResult = GameSimulator.RunSimulation(baseline, seed, dryRunSamples);
        var current = baseline;
        var currentResult = baselineResult;
        var currentDistance = ViolationDistance(blueprint, currentResult);
        var candidatesUsed = 1;
        var touchedLevers = new HashSet<string>(StringComparer.Ordinal);

        onProgress?.Invoke($"[tuning] baseline: distance={currentDistance:0.###} candidates={candidatesUsed}");

        while (currentDistance > 1e-9 && candidatesUsed < maxCandidates)
        {
            JsonObject? bestCandidate = null;
            SimResult? bestResult = null;
            var bestDistance = currentDistance;
            string? bestLeverPath = null;

            var allowedLevers = touchedLevers.Count < maxLeversPerProposal
                ? levers
                : levers.Where(lever => touchedLevers.Contains(lever.Path)).ToList();

            foreach (var lever in allowedLevers)
            {
                foreach (var direction in new[] { -1, 1 })
                {
                    if (candidatesUsed >= maxCandidates)
                    {
                        break;
                    }

                    var currentValue = GetLeverValue(current, lever.Path);
                    var candidateValue = Math.Clamp(currentValue + direction * lever.Step, lever.Min, lever.Max);

                    if (Math.Abs(candidateValue - currentValue) < 1e-9)
                    {
                        continue;
                    }

                    var candidate = CloneGameData(current);
                    SetLeverValue(candidate, lever.Path, candidateValue, IsIntegerStep(lever.Step));
                    var result = GameSimulator.RunSimulation(candidate, seed, dryRunSamples);
                    candidatesUsed += 1;
                    var distance = ViolationDistance(blueprint, result);

                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestCandidate = candidate;
                        bestResult = result;
                        bestLeverPath = lever.Path;
                    }
                }
            }

            if (bestCandidate is null || bestLeverPath is null)
            {
                onProgress?.Invoke($"[tuning] 개선되는 이웃 없음 — candidates={candidatesUsed} distance={currentDistance:0.###}에서 종료");
                break;
            }

            current = bestCandidate;
            currentResult = bestResult!;
            currentDistance = bestDistance;
            touchedLevers.Add(bestLeverPath);
            onProgress?.Invoke($"[tuning] candidate {candidatesUsed}: distance={currentDistance:0.###} lever={bestLeverPath} -> {GetLeverValue(current, bestLeverPath):0.###}");
        }

        var changedLevers = new List<LeverChange>();
        foreach (var lever in levers)
        {
            var before = GetLeverValue(baseline, lever.Path);
            var after = GetLeverValue(current, lever.Path);

            if (Math.Abs(before - after) > 1e-9)
            {
                changedLevers.Add(new LeverChange(lever.Path, before, after));
            }
        }

        var predictedMetrics = new List<PredictedMetricChange>();
        var residualViolations = new List<string>();

        foreach (var item in blueprint["items"]?.AsArray().OfType<JsonObject>() ?? [])
        {
            var metricId = item["metricId"]?.GetValue<string>() ?? "";
            var beforeValue = GameSimulator.MetricValue(metricId, baselineResult) ?? 0;
            var afterValue = GameSimulator.MetricValue(metricId, currentResult) ?? 0;
            var band = item["band"]?.AsArray();
            var bandText = band is not null && band.Count >= 2
                ? $"{ParseDouble(band[0])}~{ParseDouble(band[1])}"
                : "";
            predictedMetrics.Add(new PredictedMetricChange(metricId, beforeValue, afterValue, bandText));

            if (band is not null && band.Count >= 2)
            {
                var min = ParseDouble(band[0]);
                var max = ParseDouble(band[1]);
                if (afterValue < min || afterValue > max)
                {
                    residualViolations.Add($"{metricId}={afterValue:0.###} (밴드 {bandText} 밖)");
                }
            }
        }

        return new TuningResult(residualViolations.Count == 0, current, candidatesUsed, changedLevers, predictedMetrics, residualViolations);
    }

    // 레버 경로가 가리키는 현재 수치를 읽는다.
    public static double GetLeverValue(JsonObject gameData, string path)
    {
        return ParseDouble(Navigate(gameData, path));
    }

    // 레버 경로가 가리키는 값을 기록한다. isInteger면 반올림한 정수로 저장한다.
    public static void SetLeverValue(JsonObject gameData, string path, double value, bool isInteger)
    {
        var segments = path.Split('.');
        JsonNode current = gameData;

        for (var index = 0; index < segments.Length - 1; index += 1)
        {
            current = StepInto(current, segments[index]);
        }

        var (name, _) = ParseSegment(segments[^1]);
        current.AsObject()[name] = isInteger ? JsonValue.Create((int)Math.Round(value)) : JsonValue.Create(value);
    }

    // 점 표기 경로를 따라 노드를 찾아간다.
    private static JsonNode Navigate(JsonObject root, string path)
    {
        var segments = path.Split('.');
        JsonNode current = root;

        foreach (var segment in segments)
        {
            current = StepInto(current, segment);
        }

        return current;
    }

    // 경로 한 단계를 내려간다(배열 인덱스 포함).
    private static JsonNode StepInto(JsonNode current, string segment)
    {
        var (name, index) = ParseSegment(segment);
        var next = current.AsObject()[name] ?? throw new InvalidOperationException($"레버 경로를 찾을 수 없다: {segment}");
        return index is null ? next : next.AsArray()[index.Value]!;
    }

    // "name[index]" 형태의 경로 조각을 이름과 인덱스로 나눈다.
    private static (string Name, int? Index) ParseSegment(string segment)
    {
        var match = PathSegmentPattern.Match(segment);

        if (!match.Success)
        {
            throw new InvalidOperationException($"레버 경로 형식이 올바르지 않다: {segment}");
        }

        return (match.Groups[1].Value, match.Groups[2].Success ? int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) : null);
    }

    // blueprint 항목별로 밴드·목표에서 벗어난 거리를 합산한다.
    private static double ViolationDistance(JsonObject blueprint, SimResult result)
    {
        var distance = 0.0;

        foreach (var item in blueprint["items"]?.AsArray().OfType<JsonObject>() ?? [])
        {
            var metricId = item["metricId"]?.GetValue<string>() ?? "";
            var value = GameSimulator.MetricValue(metricId, result);

            if (value is null)
            {
                continue;
            }

            var band = item["band"]?.AsArray();
            if (band is not null && band.Count >= 2)
            {
                var min = ParseDouble(band[0]);
                var max = ParseDouble(band[1]);

                if (value.Value < min)
                {
                    distance += min - value.Value;
                }
                else if (value.Value > max)
                {
                    distance += value.Value - max;
                }

                continue;
            }

            var target = item["target"];
            if (target is not null)
            {
                distance += Math.Abs(value.Value - ParseDouble(target));
            }
        }

        return distance;
    }

    // 스텝 값이 정수 단위인지 확인한다.
    private static bool IsIntegerStep(double step)
    {
        return Math.Abs(step - Math.Round(step)) < 1e-9;
    }

    // game-data JSON을 복사한다(탐색 후보마다 독립된 사본이 필요하다).
    private static JsonObject CloneGameData(JsonObject gameData)
    {
        return JsonNode.Parse(gameData.ToJsonString())!.AsObject();
    }

    // 노드에서 실수 값을 읽는다.
    private static double ParseDouble(JsonNode? node)
    {
        return node is not null && double.TryParse(node.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }

    // 노드에서 정수 값을 읽는다.
    private static int Number(JsonNode? node, int fallback)
    {
        return node is not null && int.TryParse(node.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    }
}

public sealed record TunableLever(string Path, double Min, double Max, double Step);

public sealed record LeverChange(string Path, double Before, double After);

public sealed record PredictedMetricChange(string MetricId, double Before, double After, string Band);

public sealed record TuningResult(bool ReachedBand, JsonObject FinalGameData, int CandidatesUsed, List<LeverChange> ChangedLevers, List<PredictedMetricChange> PredictedMetrics, List<string> ResidualViolations);
