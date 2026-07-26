// Transition Request 생성기 — 게이트 판정은 하지 않는다.
// ADR-016 §15: 게이트 판정의 정본은 server/Harness/DiCompletionCheckCli.cs다.
// 그쪽이 --manifest로 픽스처 반증 시험이 가능하고 HarnessRegistry 네이티브이기 때문이다.
// 여기 있던 게이트 실행 코드는 그 결정으로 제거했다 — 두 벌이 있으면 갈리고,
// 갈린 결과 하나가 TRUSTED_BASELINE 선언 근거가 된 것이 ADR-016 §6의 사건이다.
//
// 하는 일: 이미 나온 게이트 보고서를 근거로 transition request 후보를 만든다.
// 통과해도 canonical state는 건드리지 않는다 — 요청이지 전이가 아니다.
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class ProgramVerifierCli
{
    private const string RequestDirRel = "outputs/transition-requests";
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // 명령을 분기한다. request는 게이트 보고서를 읽어 전이 요청 후보를 만든다.
    internal static int Run(string[] args)
    {
        var sub = args.Length > 1 ? args[1] : "";
        if (string.Equals(sub, "request", StringComparison.OrdinalIgnoreCase)) return Request(args);
        if (string.Equals(sub, "verify", StringComparison.OrdinalIgnoreCase))
        {
            // 판정은 여기서 하지 않는다. 조용히 다른 일을 하지 말고 어디로 가야 하는지 말한다.
            Console.Error.WriteLine("{\"error\":\"게이트 판정은 di-completion-check가 한다(ADR-016 §15). "
                + "사용법: di-completion-check --gate <gateId> --task <id> 로 보고를 낸 뒤 "
                + "program-verify request --gate <gateId> --report <path>\"}");
            return 2;
        }

        Console.Error.WriteLine("{\"error\":\"사용법: program-verify request --gate <gateId> --report <path> [--launch <launchId>]\"}");
        return 2;
    }

    // 게이트 보고서를 근거로 transition request 후보를 만든다. 근거가 없으면 만들지 않는다.
    private static int Request(string[] args)
    {
        var optionFailure = CliOptions.Validate(args, 2, ["gate", "report", "launch"], []);
        if (optionFailure is not null)
        {
            Console.Error.WriteLine(new JsonObject { ["error"] = optionFailure }.ToJsonString(JsonOptions));
            return 2;
        }

        var gateId = ReadOption(args, "--gate");
        var reportPath = ReadOption(args, "--report");
        var launchId = ReadOption(args, "--launch");
        if (string.IsNullOrWhiteSpace(gateId) || string.IsNullOrWhiteSpace(reportPath))
        {
            Console.Error.WriteLine("{\"error\":\"--gate <gateId> 와 --report <path> 가 필요하다.\"}");
            return 2;
        }

        var root = RepoRoot();
        // 판정 규칙은 GateReportReader 한 곳에 있다. trust-origin과 같은 규칙을 쓴다.
        var rejection = GateReportReader.Reject(root, reportPath, gateId, out var report);
        if (rejection is not null || report is null)
        {
            Console.Error.WriteLine(new JsonObject
            {
                ["gateId"] = gateId,
                ["verdict"] = "NO-REQUEST",
                ["reason"] = rejection ?? "gate-report-unparsable",
            }.ToJsonString(JsonOptions));
            return 1;
        }

        var requestPath = WriteRequest(root, gateId, launchId, report);
        Console.WriteLine(new JsonObject
        {
            ["command"] = "program-verify request",
            ["gateId"] = gateId,
            ["launchId"] = launchId,
            ["gateReport"] = reportPath,
            ["transitionRequestPath"] = requestPath,
            ["note"] = "요청이지 전이가 아니다. canonical state는 사람 결재 + state-transition으로만 바뀐다.",
        }.ToJsonString(JsonOptions));
        return 0;
    }
    // canonical 변경은 사람 결재와 StateApplier 전이를 거쳐야 한다(계약 §4).
    private static string WriteRequest(string root, string gateId, string? launchId, JsonObject report)
    {
        var dir = Path.Combine(root, RequestDirRel);
        Directory.CreateDirectory(dir);
        var stamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");
        var name = $"TR-{gateId}-{stamp}.json";
        var request = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["kind"] = "TRANSITION_REQUEST",
            ["status"] = "AWAITING_HUMAN_APPROVAL",
            ["gateId"] = gateId,
            ["launchId"] = launchId,
            ["createdAt"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["createdBy"] = "program-verify",
            // 요청이 어떤 판정에서 나왔는지 묶는다. 판정 없이 만들어진 요청과 구분된다.
            ["evidence"] = report.DeepClone(),
            ["note"] = "이 파일은 요청이지 전이가 아니다. canonical state는 사람 결재 + state-transition으로만 바뀐다.",
        };
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, request.ToJsonString(JsonOptions), Utf8NoBom);
        return Path.Combine(RequestDirRel, name).Replace('\\', '/');
    }
    // --이름 다음 값을 읽는다. 없으면 null이다.
    private static string? ReadOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        }
        return null;
    }
    // 저장소 루트를 찾는다. 실행 위치가 어디든 같은 기준으로 검사를 돌리기 위함이다.
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git"))) return dir.FullName;
            dir = dir.Parent;
        }
        return Directory.GetCurrentDirectory();
    }
}
