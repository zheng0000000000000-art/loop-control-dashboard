// 프로젝트 조회 API의 엣지 입력이 500이 아니라 계약된 4xx로 거부되는지 확인한다.
// 실행 중인 서버를 대상으로 GET만 수행하며, 상태를 바꾸지 않는다.
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class ProjectApiEdgeCheckCli
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // project-api-edge-check 진입점. exit 0=모든 HTTP 상태 기대값 일치, 1=계약 위반, 2=사용법/실행 오류.
    internal static int Run(string[] args)
    {
        var baseUrl = args.Length > 1 ? args[1] : "http://127.0.0.1:5173";
        if (!Uri.TryCreate(baseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri))
        {
            Console.Error.WriteLine("{\"error\":\"usage: project-api-edge-check [baseUrl]\"}");
            return 2;
        }

        try
        {
            using var client = new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(10) };
            var checks = BuildChecks();
            var results = new JsonArray();
            var failureCount = 0;

            foreach (var check in checks)
            {
                var result = RunCheck(client, check);
                if (result.Verdict != "PASS")
                    failureCount++;

                results.Add(new JsonObject
                {
                    ["name"] = check.Name,
                    ["method"] = "GET",
                    ["path"] = check.Path,
                    ["expectation"] = check.Expectation,
                    ["statusCode"] = result.StatusCode,
                    ["bodyExcerpt"] = result.BodyExcerpt,
                    ["error"] = result.Error,
                    ["verdict"] = result.Verdict,
                });
            }

            Console.WriteLine(new JsonObject
            {
                ["harness"] = "project-api-edge-check",
                ["baseUrl"] = baseUri.ToString().TrimEnd('/'),
                ["checkCount"] = checks.Count,
                ["failureCount"] = failureCount,
                ["verdict"] = failureCount == 0 ? "PASS" : "FAIL",
                ["checks"] = results,
                ["note"] = "Missing project read APIs must return 4xx, not 5xx. This harness performs GET requests only.",
            }.ToJsonString(JsonOptions));

            return failureCount == 0 ? 0 : 1;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(new JsonObject
            {
                ["harness"] = "project-api-edge-check",
                ["error"] = error.Message,
            }.ToJsonString(JsonOptions));
            return 2;
        }
    }

    // 정상·엣지 조회 계약을 내장 케이스로 만든다.
    private static List<ApiCheck> BuildChecks()
    {
        return new List<ApiCheck>
        {
            new("projects-json", "data/projects.json", "200"),
            new("valid-project-state", "api/projects/dev-pack/state", "200"),
            new("missing-project-state", "api/projects/__missing__/state", "4xx"),
            new("missing-project-context", "api/projects/__missing__/context", "4xx"),
            new("missing-project-measurement", "api/projects/__missing__/measurement", "4xx"),
            new("missing-project-cycle-summary", "api/projects/__missing__/cycle-summary", "4xx"),
            new("missing-outbox-task", "api/outbox/__missing__", "404"),
        };
    }

    // 단일 GET 요청을 수행하고 기대 HTTP 상태 범주와 대조한다.
    private static ApiResult RunCheck(HttpClient client, ApiCheck check)
    {
        try
        {
            using var response = client.GetAsync(check.Path).GetAwaiter().GetResult();
            var statusCode = (int)response.StatusCode;
            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var verdict = Matches(statusCode, check.Expectation) ? "PASS" : "FAIL";
            return new ApiResult(statusCode, Excerpt(body), "", verdict);
        }
        catch (Exception error)
        {
            return new ApiResult(null, "", error.Message, "FAIL");
        }
    }

    // 기대값(200, 404, 4xx)을 실제 HTTP status code와 비교한다.
    private static bool Matches(int statusCode, string expectation)
    {
        return expectation switch
        {
            "200" => statusCode == 200,
            "404" => statusCode == 404,
            "4xx" => statusCode >= 400 && statusCode < 500,
            _ => false,
        };
    }

    // 리포트가 과도하게 커지지 않도록 응답 본문을 짧게 자른다.
    private static string Excerpt(string body)
    {
        if (string.IsNullOrEmpty(body))
            return "";

        var compact = body.Replace("\r", "").Replace("\n", " ");
        return compact.Length <= 180 ? compact : compact[..180] + "...";
    }

    private sealed record ApiCheck(string Name, string Path, string Expectation);

    private sealed record ApiResult(int? StatusCode, string BodyExcerpt, string Error, string Verdict);
}
