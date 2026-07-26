// 게이트 매니페스트의 성공 검사마다 실패 반증 증거가 있는지 집계한다.
// 기본 모드는 관찰만 하며, 명시적으로 요구한 게이트만 누락을 차단한다.
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class GateWitnessCheckCli
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // gate-witness-check 진입점. exit 0=관찰 완료, 1=opt-in 게이트 누락, 2=입력 또는 실행 오류.
    internal static int Run(string[] args)
    {
        try
        {
            if (args.Length == 3 &&
                string.Equals(args[1], "--count-output-fixture", StringComparison.OrdinalIgnoreCase))
                return RunCountOutputFixture(args[2]);

            var root = GitTools.FindRepoRoot();
            var manifestPath = ResolveManifestPath(root, args);
            var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))?.AsObject()
                ?? throw new JsonException("manifest root must be an object");
            var gates = manifest["gates"]?.AsArray()
                ?? throw new JsonException("gates must be an array");
            var reports = new JsonArray();
            var totalUnwitnessed = 0;
            var blocking = false;

            foreach (var gate in gates.OfType<JsonObject>())
            {
                var result = InspectGate(root, gate);
                reports.Add(result.Report);
                totalUnwitnessed += result.UnwitnessedCount;
                blocking |= result.RequiresWitness && result.UnwitnessedCount > 0;
            }

            Console.WriteLine(new JsonObject
            {
                ["harness"] = "gate-witness-check",
                ["gates"] = reports,
                ["totalUnwitnessed"] = totalUnwitnessed,
            }.ToJsonString(JsonOptions));
            return blocking ? 1 : 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new
            {
                error = $"gate-witness-check failed: {ex.Message}",
            }));
            return 2;
        }
    }

    // 저장된 stdout 픽스처에서 구조화 음성 사례 수를 읽어 파서 반증 시험을 지원한다.
    private static int RunCountOutputFixture(string fixturePath)
    {
        var root = GitTools.FindRepoRoot();
        var full = Path.GetFullPath(Path.IsPathRooted(fixturePath)
            ? fixturePath
            : Path.Combine(root, fixturePath));
        if (!File.Exists(full))
            throw new FileNotFoundException("output fixture not found", fixturePath);

        var count = 0;
        try
        {
            count = CountJsonCaseValues(File.ReadAllText(full));
        }
        catch (JsonException)
        {
            count = 0;
        }

        Console.WriteLine(new JsonObject
        {
            ["harness"] = "gate-witness-count-output-fixture",
            ["internalNegativeCases"] = count,
        }.ToJsonString(JsonOptions));
        return 0;
    }

    // 선택 인자가 없으면 저장소의 표준 게이트 매니페스트를 사용한다.
    private static string ResolveManifestPath(string root, string[] args)
    {
        if (args.Length > 2)
            throw new ArgumentException("usage: gate-witness-check [manifest-path]");

        var input = args.Length == 2
            ? args[1]
            : Path.Combine("docs", "handoff", "GATE-MANIFEST.json");
        var full = Path.GetFullPath(Path.IsPathRooted(input) ? input : Path.Combine(root, input));
        if (!File.Exists(full))
            throw new FileNotFoundException("manifest not found", input);
        return full;
    }

    // 한 게이트의 성공 검사와 반증 증거를 대조한다.
    private static GateWitnessResult InspectGate(string root, JsonObject gate)
    {
        var gateId = gate["gateId"]?.ToString() ?? "";
        var checks = gate["checks"]?.AsArray().OfType<JsonObject>().ToList()
            ?? throw new JsonException($"gate {gateId}: checks must be an array");
        var requireWitness = ReadBool(gate["requireFailureWitness"]);
        var unwitnessed = new JsonArray();

        foreach (var check in checks.Where(item => ReadInt(item["expectedExit"], 0) == 0))
        {
            if (HasWitness(root, check, checks, requireWitness))
                continue;

            unwitnessed.Add(new JsonObject
            {
                ["order"] = ReadInt(check["order"], 0),
                ["command"] = check["command"]?.ToString() ?? "",
            });
        }

        return new GateWitnessResult(new JsonObject
        {
            ["gateId"] = gateId,
            ["checkCount"] = checks.Count,
            ["witnessedCount"] = checks.Count - unwitnessed.Count,
            ["unwitnessedCount"] = unwitnessed.Count,
            ["unwitnessed"] = unwitnessed,
        }, requireWitness, unwitnessed.Count);
    }

    // 동일 명령의 비영 출구, 명시적 order 연결, 검증된 내부 사례 중 하나를 증거로 인정한다.
    private static bool HasWitness(
        string root,
        JsonObject check,
        List<JsonObject> checks,
        bool validateInternalClaims)
    {
        var command = check["command"]?.ToString() ?? "";
        if (checks.Any(other =>
            !ReferenceEquals(other, check) &&
            string.Equals(other["command"]?.ToString(), command, StringComparison.OrdinalIgnoreCase) &&
            ReadInt(other["expectedExit"], 0) != 0))
            return true;

        if (check["negativeWitness"] is not null)
        {
            var witnessOrder = ReadInt(check["negativeWitness"], int.MinValue);
            if (checks.Any(other => !ReferenceEquals(other, check) &&
                ReadInt(other["order"], int.MaxValue) == witnessOrder))
                return true;
        }

        var claimedCases = ReadInt(check["internalNegativeCases"], 0);
        if (claimedCases < 1)
            return false;
        return !validateInternalClaims || CountInternalNegativeCases(root, check) >= claimedCases;
    }

    // opt-in 게이트에서는 검사를 실제 실행하고 구조화 출력의 음성 사례 수를 읽는다.
    private static int CountInternalNegativeCases(string root, JsonObject check)
    {
        var command = check["command"]?.ToString() ?? "";
        var arguments = new List<string> { command };
        arguments.AddRange(check["args"]?.AsArray().Select(item => item?.ToString() ?? "") ?? []);
        var originalOut = Console.Out;
        var originalDirectory = Environment.CurrentDirectory;
        using var capturedOut = new StringWriter();
        int? exitCode;
        try
        {
            Environment.CurrentDirectory = root;
            Console.SetOut(capturedOut);
            exitCode = HarnessRegistry.TryRun(arguments.ToArray());
        }
        finally
        {
            Console.SetOut(originalOut);
            Environment.CurrentDirectory = originalDirectory;
        }
        if (exitCode != 0)
            return 0;

        try
        {
            return CountJsonCaseValues(capturedOut.ToString());
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    // stdout의 모든 연속 JSON 값을 끝까지 읽고 알려진 카운터의 최댓값을 반환한다.
    private static int CountJsonCaseValues(string output)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(output);
        var offset = 0;
        var maximum = 0;
        while (offset < bytes.Length)
        {
            while (offset < bytes.Length &&
                bytes[offset] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
                offset++;
            if (offset == bytes.Length)
                break;

            var reader = new Utf8JsonReader(bytes.AsSpan(offset));
            var node = JsonNode.Parse(ref reader);
            maximum = Math.Max(maximum, FindCaseCount(node));
            offset += checked((int)reader.BytesConsumed);
        }
        return maximum;
    }

    // 알려진 구조화 카운터만 재귀적으로 읽어 문면 주장을 실행 증거와 분리한다.
    private static int FindCaseCount(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            var direct = new[] { "internalNegativeCases", "negativeCaseCount", "rejectedCaseCount" }
                .Select(name => ReadInt(obj[name], 0))
                .Max();
            return Math.Max(direct, obj.Select(pair => FindCaseCount(pair.Value)).DefaultIfEmpty(0).Max());
        }

        return node is JsonArray array
            ? array.Select(FindCaseCount).DefaultIfEmpty(0).Max()
            : 0;
    }

    // JSON 값을 정수로 안전하게 읽는다.
    private static int ReadInt(JsonNode? node, int fallback)
    {
        return node is not null && int.TryParse(node.ToString(), out var value) ? value : fallback;
    }

    // JSON 값을 불리언으로 안전하게 읽는다.
    private static bool ReadBool(JsonNode? node)
    {
        return node is not null && bool.TryParse(node.ToString(), out var value) && value;
    }
}

internal sealed record GateWitnessResult(JsonObject Report, bool RequiresWitness, int UnwitnessedCount);
