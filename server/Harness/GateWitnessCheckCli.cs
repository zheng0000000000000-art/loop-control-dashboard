// 게이트 매니페스트의 성공 검사마다 실패 반증 증거가 있는지 집계한다.
// 기본 모드는 관찰만 하며, 명시적으로 요구한 게이트만 누락을 차단한다.
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class GateWitnessCheckCli
{

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
            }.ToJsonString(HarnessJson.Options));
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
            // 중간에 끊긴 출력은 부분만 읽어 쓰지 않는다. 읽지 못한 것은 증거가 아니다.
            count = Unmeasured;
        }

        Console.WriteLine(new JsonObject
        {
            ["harness"] = "gate-witness-count-output-fixture",
            ["internalNegativeCases"] = count,
        }.ToJsonString(HarnessJson.Options));
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
            if (HasWitness(root, check, checks))
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

    // 동일 명령의 비영 출구, 명시적 order 연결, 실측된 내부 사례 중 하나를 증거로 인정한다.
    private static bool HasWitness(
        string root,
        JsonObject check,
        List<JsonObject> checks)
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

        // 실측과 **정확히** 같아야 한다. `>=`로 두면 적게 적을수록 쉽게 통과해서, 매니페스트 숫자가
        // 실재보다 작아지는 쪽으로 흘러도 아무도 모른다. 1이라고 적으면 어떤 self-test든 반증된
        // 것으로 세어졌다. casesRun이 표를 표와 비교하던 것과 같은 병이다(2026-07-27).
        //
        // requireFailureWitness로 이 검증을 건너뛰지 않는다. **재보지 않은 주장은 증거가 아니다** —
        // 건너뛰면 그 게이트는 문면 숫자만으로 "반증됨"이 된다. 차단 여부는 Run이 따로 정하므로
        // (blocking |= RequiresWitness && ...), 항상 재도 준비 안 된 게이트를 빨갛게 만들지 않는다.
        return CountInternalNegativeCases(root, check) == claimedCases;
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
            return Unmeasured;

        try
        {
            return CountJsonCaseValues(capturedOut.ToString());
        }
        catch (JsonException)
        {
            return Unmeasured;
        }
    }

    // 재보지 못했음을 뜻한다. 0이 아니다 — 0은 "세어봤더니 없더라"라서 구분되어야 한다.
    private const int Unmeasured = -1;

    // 명령이 자기 결과를 선언하는 자리로 인정하는 이름들. 이 셋 중 **하나만** 최상위에 있어야 한다.
    private static readonly string[] CaseCountKeys =
        ["internalNegativeCases", "negativeCaseCount", "rejectedCaseCount"];

    // stdout의 연속 JSON 값을 끝까지 읽고, **최상위에 카운터를 선언한 문서가 정확히 하나일 때만**
    // 그 값을 인정한다.
    //
    // 종전에는 출력 전체를 재귀로 훑어 **최댓값**을 썼다. 그러면 요약이 아니라 어딘가 깊이 박힌
    // 큰 수가 답이 된다. `>=` 시절에는 관대해서 안 드러났지만 `==`로 바꾼 뒤에는 반대로
    // **정상 코드가 게이트를 깨는** 쪽으로 터진다 — self-test가 중간 산출물에 더 큰 수를 하나
    // 찍기 시작하면 그만이다(2026-07-27 자진 신고 항목).
    //
    // 문서가 여럿이면 어느 것이 요약인지 프로그램이 알 수 없다. **모르면 증거가 아니다.**
    private static int CountJsonCaseValues(string output)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(output);
        var offset = 0;
        var declared = new List<int>();
        while (offset < bytes.Length)
        {
            while (offset < bytes.Length &&
                bytes[offset] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
                offset++;
            if (offset == bytes.Length)
                break;

            var reader = new Utf8JsonReader(bytes.AsSpan(offset));
            var node = JsonNode.Parse(ref reader);
            var value = TopLevelCaseCount(node);
            if (value != Unmeasured)
                declared.Add(value);
            offset += checked((int)reader.BytesConsumed);
        }

        return declared.Count == 1 ? declared[0] : Unmeasured;
    }

    // 문서 **최상위**의 카운터를 읽는다. 중첩된 값은 보지 않는다 — 요약이 아니기 때문이다.
    // 카운터 이름이 둘 이상 동시에 있으면 어느 쪽이 답인지 알 수 없으므로 인정하지 않는다.
    private static int TopLevelCaseCount(JsonNode? node)
    {
        if (node is not JsonObject obj)
            return Unmeasured;

        var present = CaseCountKeys.Where(name => obj[name] is not null).ToList();
        return present.Count == 1 ? ReadInt(obj[present[0]], Unmeasured) : Unmeasured;
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
