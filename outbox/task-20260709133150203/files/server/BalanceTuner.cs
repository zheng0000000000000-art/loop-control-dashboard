// definition에 선언된 레버 범위 안에서 game-data를 그리디 탐색해 blueprint 위반을 줄이는 후보를 찾는다.
// 레버 밖 경로는 절대 건드리지 않는다 — 탐색 대상은 언제나 선언된 레버 목록뿐이다.
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

public static class BalanceTuner
{
    private const double ScoreEpsilon = 1e-9;
    private const double ShapingWeight = 0.000001;
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
    var baselineScore = ScoreCandidate(blueprint, baselineResult);
    var currentScore = baselineScore;
    var candidatesUsed = 1;
    var touchedLevers = new HashSet<string>(StringComparer.Ordinal);
    onProgress?.Invoke(ScoreLine("[tuning] baseline", currentScore, candidatesUsed));

    RunSearchLoop(blueprint, levers, seed, dryRunSamples, maxLeversPerProposal, maxCandidates, touchedLevers, onProgress, ref current, ref currentResult, ref currentScore, ref candidatesUsed);
    var changedLevers = CollectChangedLevers(levers, baseline, current);
    var prediction = BuildPrediction(blueprint, baselineResult, currentResult);

    return new TuningResult(
        prediction.ResidualViolations.Count == 0,
        current,
        candidatesUsed,
        changedLevers,
        prediction.PredictedMetrics,
        prediction.ResidualViolations,
        baselineScore.PrimaryDistance,
        currentScore.PrimaryDistance,
        baselineScore.AverageProgressedRooms,
        currentScore.AverageProgressedRooms);
}

// 후보 탐색 루프를 실행한다.
private static void RunSearchLoop(JsonObject blueprint, List<TunableLever> levers, int seed, int dryRunSamples, int maxLeversPerProposal, int maxCandidates, HashSet<string> touchedLevers, Action<string>? onProgress, ref JsonObject current, ref SimResult currentResult, ref CandidateScore currentScore, ref int candidatesUsed)
{
    while (currentScore.PrimaryDistance > ScoreEpsilon && candidatesUsed < maxCandidates)
    {
        var best = FindBestCandidate(current, blueprint, levers, touchedLevers, maxLeversPerProposal, seed, dryRunSamples, currentScore, ref candidatesUsed, maxCandidates, onProgress);
        if (best is null)
        {
            ReportSearchStop(candidatesUsed, maxCandidates, currentScore, onProgress);
            break;
        }

        current = best.GameData;
        currentResult = best.Result;
        currentScore = best.Score;
        foreach (var leverPath in best.LeverPaths)
        {
            touchedLevers.Add(leverPath);
        }

        onProgress?.Invoke($"[tuning] 채택: candidates={candidatesUsed} distance={currentScore.PrimaryDistance:0.###} progress={currentScore.AverageProgressedRooms:0.###} score={currentScore.Total:0.######} levers={string.Join(", ", best.LeverPaths)}");
    }
}

// 현재 상태에서 가장 나은 후보를 찾는다.
private static Candidate? FindBestCandidate(JsonObject current, JsonObject blueprint, List<TunableLever> levers, HashSet<string> touchedLevers, int maxLeversPerProposal, int seed, int dryRunSamples, CandidateScore currentScore, ref int candidatesUsed, int maxCandidates, Action<string>? onProgress)
{
    var best = TryBestSingleStep(current, blueprint, levers, touchedLevers, maxLeversPerProposal, seed, dryRunSamples, 1, currentScore, ref candidatesUsed, maxCandidates, onProgress);

    if ((best is null || !PrimaryImproved(best.Score, currentScore)) && candidatesUsed < maxCandidates)
    {
        best = BetterCandidate(best, TryBestSingleStep(current, blueprint, levers, touchedLevers, maxLeversPerProposal, seed, dryRunSamples, 2, currentScore, ref candidatesUsed, PhaseLimit(candidatesUsed, maxCandidates, 3), onProgress), currentScore);
    }

    if ((best is null || !PrimaryImproved(best.Score, currentScore)) && candidatesUsed < maxCandidates)
    {
        best = BetterCandidate(best, TryBestSingleStep(current, blueprint, levers, touchedLevers, maxLeversPerProposal, seed, dryRunSamples, 3, currentScore, ref candidatesUsed, PhaseLimit(candidatesUsed, maxCandidates, 2), onProgress), currentScore);
    }

    if ((best is null || !PrimaryImproved(best.Score, currentScore)) && candidatesUsed < maxCandidates)
    {
        best = BetterCandidate(best, TryBestTwoLeverStep(current, blueprint, levers, touchedLevers, maxLeversPerProposal, seed, dryRunSamples, currentScore, ref candidatesUsed, maxCandidates, onProgress), currentScore);
    }

    return best;
}

// 탐색 종료 사유를 기록한다.
private static void ReportSearchStop(int candidatesUsed, int maxCandidates, CandidateScore currentScore, Action<string>? onProgress)
{
    if (candidatesUsed >= maxCandidates)
    {
        onProgress?.Invoke($"[tuning] 후보 상한 도달: candidates={candidatesUsed} distance={currentScore.PrimaryDistance:0.###}에서 종료");
        return;
    }

    onProgress?.Invoke($"[tuning] 개선되는 이웃 없음: candidates={candidatesUsed} distance={currentScore.PrimaryDistance:0.###} progress={currentScore.AverageProgressedRooms:0.###}에서 종료");
}

// 변경된 레버 목록을 계산한다.
private static List<LeverChange> CollectChangedLevers(List<TunableLever> levers, JsonObject baseline, JsonObject current)
{
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

    return changedLevers;
}

// 예측 지표와 잔여 위반을 계산한다.
private static PredictionSummary BuildPrediction(JsonObject blueprint, SimResult baselineResult, SimResult currentResult)
{
    var predictedMetrics = new List<PredictedMetricChange>();
    var residualViolations = new List<string>();
    foreach (var item in blueprint["items"]?.AsArray().OfType<JsonObject>() ?? [])
    {
        var metricId = item["metricId"]?.GetValue<string>() ?? "";
        var beforeValue = GameSimulator.MetricValue(metricId, baselineResult) ?? 0;
        var afterValue = GameSimulator.MetricValue(metricId, currentResult) ?? 0;
        var band = item["band"]?.AsArray();
        var bandText = band is not null && band.Count >= 2 ? $"{ParseDouble(band[0])}~{ParseDouble(band[1])}" : "";
        predictedMetrics.Add(new PredictedMetricChange(metricId, beforeValue, afterValue, bandText));
        AddResidualViolation(residualViolations, metricId, afterValue, band, bandText);
    }

    return new PredictionSummary(predictedMetrics, residualViolations);
}

// 밴드 밖 예측 값을 잔여 위반으로 추가한다.
private static void AddResidualViolation(List<string> residualViolations, string metricId, double afterValue, JsonArray? band, string bandText)
{
    if (band is null || band.Count < 2)
    {
        return;
    }

    var min = ParseDouble(band[0]);
    var max = ParseDouble(band[1]);
    if (afterValue < min || afterValue > max)
    {
        residualViolations.Add($"{metricId}={afterValue:0.###} (밴드 {bandText} 밖)");
    }
}

private sealed record PredictionSummary(List<PredictedMetricChange> PredictedMetrics, List<string> ResidualViolations);
    // 단일 레버를 지정 배수만큼 움직인 후보 중 가장 나은 것을 찾는다.
    private static SearchCandidate? TryBestSingleStep(JsonObject current, JsonObject blueprint, List<TunableLever> levers, HashSet<string> touchedLevers, int maxLeversPerProposal, int seed, int samples, int multiplier, TuningScore currentScore, ref int candidatesUsed, int maxCandidates, Action<string>? onProgress)
    {
        SearchCandidate? best = null;
        var beforeCount = candidatesUsed;

        foreach (var lever in levers)
        {
            if (!CanTouch(touchedLevers, [lever.Path], maxLeversPerProposal))
            {
                continue;
            }

            foreach (var direction in new[] { -1, 1 })
            {
                if (candidatesUsed >= maxCandidates)
                {
                    break;
                }

                var candidate = BuildSingleCandidate(current, blueprint, lever, direction, multiplier, seed, samples);
                if (candidate is null)
                {
                    continue;
                }

                candidatesUsed += 1;
                if (IsBetter(candidate.Score, best?.Score ?? currentScore))
                {
                    best = candidate;
                }
            }
        }

        LogPhase(onProgress, $"step=±{multiplier}", candidatesUsed - beforeCount, currentScore, best?.Score ?? currentScore);
        return best;
    }

    // 두 레버를 한 스텝씩 함께 움직인 후보 중 가장 나은 것을 찾는다.
    private static SearchCandidate? TryBestTwoLeverStep(JsonObject current, JsonObject blueprint, List<TunableLever> levers, HashSet<string> touchedLevers, int maxLeversPerProposal, int seed, int samples, TuningScore currentScore, ref int candidatesUsed, int maxCandidates, Action<string>? onProgress)
    {
        SearchCandidate? best = null;
        var beforeCount = candidatesUsed;

        for (var firstIndex = 0; firstIndex < levers.Count; firstIndex += 1)
        {
            for (var secondIndex = firstIndex + 1; secondIndex < levers.Count; secondIndex += 1)
            {
                var first = levers[firstIndex];
                var second = levers[secondIndex];

                if (!CanTouch(touchedLevers, [first.Path, second.Path], maxLeversPerProposal))
                {
                    continue;
                }

                foreach (var firstDirection in new[] { -1, 1 })
                {
                    foreach (var secondDirection in new[] { -1, 1 })
                    {
                        if (candidatesUsed >= maxCandidates)
                        {
                            break;
                        }

                        var candidate = BuildPairCandidate(current, blueprint, first, firstDirection, second, secondDirection, seed, samples);
                        if (candidate is null)
                        {
                            continue;
                        }

                        candidatesUsed += 1;
                        if (IsBetter(candidate.Score, best?.Score ?? currentScore))
                        {
                            best = candidate;
                        }
                    }
                }
            }
        }

        LogPhase(onProgress, "two-lever", candidatesUsed - beforeCount, currentScore, best?.Score ?? currentScore);
        return best;
    }

    // 단일 레버 후보를 만들고 시뮬레이션 점수를 계산한다.
    private static SearchCandidate? BuildSingleCandidate(JsonObject current, JsonObject blueprint, TunableLever lever, int direction, int multiplier, int seed, int samples)
    {
        var currentValue = GetLeverValue(current, lever.Path);
        var candidateValue = Math.Clamp(currentValue + direction * lever.Step * multiplier, lever.Min, lever.Max);

        if (Math.Abs(candidateValue - currentValue) < ScoreEpsilon)
        {
            return null;
        }

        var candidate = CloneGameData(current);
        SetLeverValue(candidate, lever.Path, candidateValue, IsIntegerStep(lever.Step));
        var result = GameSimulator.RunSimulation(candidate, seed, samples);
        return new SearchCandidate(candidate, result, ScoreCandidate(blueprint, result), [lever.Path]);
    }

    // 두 레버 후보를 만들고 시뮬레이션 점수를 계산한다.
    private static SearchCandidate? BuildPairCandidate(JsonObject current, JsonObject blueprint, TunableLever first, int firstDirection, TunableLever second, int secondDirection, int seed, int samples)
    {
        var firstValue = GetLeverValue(current, first.Path);
        var secondValue = GetLeverValue(current, second.Path);
        var nextFirst = Math.Clamp(firstValue + firstDirection * first.Step, first.Min, first.Max);
        var nextSecond = Math.Clamp(secondValue + secondDirection * second.Step, second.Min, second.Max);

        if (Math.Abs(nextFirst - firstValue) < ScoreEpsilon && Math.Abs(nextSecond - secondValue) < ScoreEpsilon)
        {
            return null;
        }

        var candidate = CloneGameData(current);
        SetLeverValue(candidate, first.Path, nextFirst, IsIntegerStep(first.Step));
        SetLeverValue(candidate, second.Path, nextSecond, IsIntegerStep(second.Step));
        var result = GameSimulator.RunSimulation(candidate, seed, samples);
        return new SearchCandidate(candidate, result, ScoreCandidate(blueprint, result), [first.Path, second.Path]);
    }

    // 후보 점수를 비교한다. 주항이 같을 때만 진행도 보조항이 순서를 바꾼다.
    private static bool IsBetter(TuningScore candidate, TuningScore best)
    {
        if (candidate.PrimaryDistance < best.PrimaryDistance - ScoreEpsilon)
        {
            return true;
        }

        return Math.Abs(candidate.PrimaryDistance - best.PrimaryDistance) <= ScoreEpsilon &&
            candidate.Total < best.Total - ScoreEpsilon;
    }

    // 주항 거리가 줄었는지 확인한다.
    private static bool PrimaryImproved(TuningScore candidate, TuningScore current)
    {
        return candidate.PrimaryDistance < current.PrimaryDistance - ScoreEpsilon;
    }

    // 남은 후보 예산을 아직 남은 확장 단계 수에 맞춰 나눈다.
    private static int PhaseLimit(int candidatesUsed, int maxCandidates, int remainingPhases)
    {
        var remaining = Math.Max(0, maxCandidates - candidatesUsed);
        return candidatesUsed + Math.Max(1, remaining / Math.Max(1, remainingPhases));
    }

    // 두 후보 중 현재 점수 기준으로 더 나은 후보를 고른다.
    private static SearchCandidate? BetterCandidate(SearchCandidate? first, SearchCandidate? second, TuningScore currentScore)
    {
        if (first is null)
        {
            return second;
        }

        if (second is null)
        {
            return first;
        }

        return IsBetter(second.Score, first.Score) && IsBetter(second.Score, currentScore) ? second : first;
    }

    // 새 레버를 추가해도 제안 레버 수 상한을 넘지 않는지 확인한다.
    private static bool CanTouch(HashSet<string> touchedLevers, IEnumerable<string> candidatePaths, int maxLeversPerProposal)
    {
        return touchedLevers.Concat(candidatePaths).Distinct(StringComparer.Ordinal).Count() <= maxLeversPerProposal;
    }

    // 탐색 단계의 후보 수와 점수 변화를 기록한다.
    private static void LogPhase(Action<string>? onProgress, string label, int tried, TuningScore before, TuningScore after)
    {
        onProgress?.Invoke($"[tuning] {label} 후보 {tried}개 시도, distance {before.PrimaryDistance:0.###}→{after.PrimaryDistance:0.###}, progress {before.AverageProgressedRooms:0.###}→{after.AverageProgressedRooms:0.###}, score {before.Total:0.######}→{after.Total:0.######}");
    }

    // 점수 표시 문자열을 만든다.
    private static string ScoreLine(string prefix, TuningScore score, int candidatesUsed)
    {
        return $"{prefix}: distance={score.PrimaryDistance:0.###} progress={score.AverageProgressedRooms:0.###} score={score.Total:0.######} candidates={candidatesUsed}";
    }

    // blueprint 거리와 진행도 보조항을 결합한 후보 점수를 만든다.
    private static TuningScore ScoreCandidate(JsonObject blueprint, SimResult result)
    {
        var primary = ViolationDistance(blueprint, result);
        var maxRooms = result.RoomDeathRates.Length;
        var shapingDistance = Math.Max(0, maxRooms - result.AverageProgressedRooms);
        return new TuningScore(primary, result.AverageProgressedRooms, primary + shapingDistance * ShapingWeight);
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

public sealed record TuningScore(double PrimaryDistance, double AverageProgressedRooms, double Total);

public sealed record SearchCandidate(JsonObject GameData, SimResult Result, TuningScore Score, List<string> LeverPaths);

public sealed record TuningResult(bool ReachedBand, JsonObject FinalGameData, int CandidatesUsed, List<LeverChange> ChangedLevers, List<PredictedMetricChange> PredictedMetrics, List<string> ResidualViolations, double BaselineDistance, double FinalDistance, double BaselineProgressedRooms, double FinalProgressedRooms);
