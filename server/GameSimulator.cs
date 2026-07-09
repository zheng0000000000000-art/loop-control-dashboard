// 렌더링 없는 수치 전투 시뮬레이터. game-data.json을 읽어 완주율·방별 사망률·보상 분포를 낸다.
// SimCombat은 스텁이다 — 실제 턴제 전투 규칙은 사람이 채운다.
using System.Globalization;
using System.Text.Json.Nodes;

public static class GameSimulator
{
    private const string ProviderVersion = "1";

    // game-data.json을 읽어 시뮬레이션 측정 결과를 만든다.
    public static JsonObject Measure(string projectPath, string providerId, JsonObject blueprint, JsonObject providerConfig)
    {
        var seed = Number(providerConfig["seed"], 42);
        var runs = Number(providerConfig["dryRunSamples"], 500);
        var gameDataPath = Path.Combine(projectPath, "game-data.json");
        var metrics = new JsonArray();

        if (File.Exists(gameDataPath))
        {
            var gameData = JsonNode.Parse(File.ReadAllText(gameDataPath))!.AsObject();
            var result = RunSimulation(gameData, seed, runs);

            foreach (var item in blueprint["items"]?.AsArray().OfType<JsonObject>() ?? [])
            {
                var metricId = item["metricId"]?.GetValue<string>() ?? "";
                metrics.Add(MetricFromResult(metricId, result));
            }
        }

        return new JsonObject
        {
            ["schemaVersion"] = 2,
            ["providerId"] = providerId,
            ["providerVersion"] = ProviderVersion,
            ["measuredAt"] = DateTimeOffset.Now.ToString("O"),
            ["seed"] = seed,
            ["metrics"] = metrics,
        };
    }

    // 지정한 시드로 시뮬레이션을 runs회 실행해 지표 분포를 낸다. 같은 시드는 항상 같은 결과를 낸다(재현성 계약).
    public static SimResult RunSimulation(JsonObject gameData, int seed, int runs)
    {
        var player = ParsePlayer(gameData);
        var rooms = ParseRooms(gameData);
        var random = new Random(seed);

        var roomAttempts = new int[rooms.Count];
        var roomDeaths = new int[rooms.Count];
        var hpSums = new double[rooms.Count];
        var rewards = new List<double>(runs);
        var completions = 0;

        for (var run = 0; run < runs; run += 1)
        {
            var hp = player.MaxHp;
            var rewardTotal = 0.0;
            var completedAllRooms = true;

            for (var roomIndex = 0; roomIndex < rooms.Count; roomIndex += 1)
            {
                roomAttempts[roomIndex] += 1;
                var outcome = SimCombat(new PlayerState(hp, player.MaxHp, player.Attack), rooms[roomIndex], random);
                hpSums[roomIndex] += outcome.RemainingHp;
                rewardTotal += outcome.RewardGained;

                if (!outcome.Survived)
                {
                    roomDeaths[roomIndex] += 1;
                    completedAllRooms = false;
                    break;
                }

                hp = outcome.RemainingHp;
            }

            if (completedAllRooms)
            {
                completions += 1;
            }

            rewards.Add(rewardTotal);
        }

        var completionRate = runs == 0 ? 0 : (double)completions / runs * 100;
        var roomDeathRates = new double[rooms.Count];
        var avgHpPerRoom = new double[rooms.Count];

        for (var index = 0; index < rooms.Count; index += 1)
        {
            roomDeathRates[index] = roomAttempts[index] == 0 ? 0 : (double)roomDeaths[index] / roomAttempts[index] * 100;
            avgHpPerRoom[index] = roomAttempts[index] == 0 ? 0 : hpSums[index] / roomAttempts[index];
        }

        var rewardMean = rewards.Count == 0 ? 0 : rewards.Average();
        var rewardStdDev = rewards.Count == 0
            ? 0
            : Math.Sqrt(rewards.Sum(reward => Math.Pow(reward - rewardMean, 2)) / rewards.Count);

        return new SimResult(completionRate, roomDeathRates, avgHpPerRoom, rewardMean, rewardStdDev);
    }

    // 방 하나의 전투 결과를 계산하는 자리다. 실제 턴제 규칙은 사람이 채울 스텁 — 지금은 항상 고정값을 반환한다.
    private static RoomOutcome SimCombat(PlayerState player, RoomState room, Random random)
    {
        // TODO(사람 작성 예정): room.Enemies·room.Reward와 random을 사용해 실제 전투 결과를 계산한다.
        return new RoomOutcome(Survived: true, RemainingHp: player.Hp, RewardGained: 0);
    }

    // 측정 지표 하나를 SimResult에서 뽑아 JSON으로 만든다.
    private static JsonObject MetricFromResult(string metricId, SimResult result)
    {
        return metricId switch
        {
            "completionRate" => Metric(metricId, result.CompletionRate, ["시뮬레이션 전체 실행"]),
            "room1DeathRate" => Metric(metricId, RoomValue(result.RoomDeathRates, 0), ["rooms[0]"]),
            "room3DeathRate" => Metric(metricId, RoomValue(result.RoomDeathRates, 2), ["rooms[2]"]),
            "avgRewardPerRun" => Metric(metricId, result.RewardPerRunMean, [$"stddev={result.RewardPerRunStdDev.ToString("0.##", CultureInfo.InvariantCulture)}"]),
            _ => Metric(metricId, (JsonNode?)null, ["미구현"]),
        };
    }

    // 방 배열에서 안전하게 인덱스 값을 읽는다.
    private static double RoomValue(double[] values, int index)
    {
        return index >= 0 && index < values.Length ? values[index] : 0;
    }

    // game-data의 player 절을 읽는다.
    private static PlayerState ParsePlayer(JsonObject gameData)
    {
        var player = gameData["player"]?.AsObject();
        var maxHp = Number(player?["maxHp"], 100);
        return new PlayerState(maxHp, maxHp, Number(player?["attack"], 10));
    }

    // game-data의 rooms 배열을 읽는다.
    private static List<RoomState> ParseRooms(JsonObject gameData)
    {
        var rooms = new List<RoomState>();

        foreach (var roomNode in gameData["rooms"]?.AsArray().OfType<JsonObject>() ?? [])
        {
            var enemies = roomNode["enemies"]?.AsObject();
            var reward = roomNode["rewards"]?.AsObject();
            rooms.Add(new RoomState(
                roomNode["id"]?.GetValue<string>() ?? "",
                new EnemyGroup(Number(enemies?["hp"], 0), Number(enemies?["attack"], 0), Number(enemies?["count"], 0)),
                new RoomReward(Double(reward?["commonDropRate"], 0), Number(reward?["healAmount"], 0))));
        }

        return rooms;
    }

    // 측정 결과 JSON 객체를 만든다.
    private static JsonObject Metric(string metricId, double value, IEnumerable<string> evidence)
    {
        return Metric(metricId, JsonValue.Create(value), evidence);
    }

    // 측정 결과 JSON 객체를 만든다.
    private static JsonObject Metric(string metricId, JsonNode? value, IEnumerable<string> evidence)
    {
        var evidenceArray = new JsonArray();
        foreach (var item in evidence)
        {
            evidenceArray.Add(item);
        }

        return new JsonObject
        {
            ["metricId"] = metricId,
            ["value"] = value is null ? null : JsonNode.Parse(value.ToJsonString()),
            ["evidence"] = evidenceArray,
        };
    }

    // 노드에서 정수 값을 읽는다.
    private static int Number(JsonNode? node, int fallback)
    {
        return node is not null && int.TryParse(node.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    }

    // 노드에서 실수 값을 읽는다.
    private static double Double(JsonNode? node, double fallback)
    {
        return node is not null && double.TryParse(node.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    }
}

public sealed record SimResult(double CompletionRate, double[] RoomDeathRates, double[] AvgHpPerRoom, double RewardPerRunMean, double RewardPerRunStdDev);

public sealed record PlayerState(int Hp, int MaxHp, int Attack);

public sealed record EnemyGroup(int Hp, int Attack, int Count);

public sealed record RoomReward(double CommonDropRate, int HealAmount);

public sealed record RoomState(string Id, EnemyGroup Enemies, RoomReward Reward);

public sealed record RoomOutcome(bool Survived, int RemainingHp, double RewardGained);
