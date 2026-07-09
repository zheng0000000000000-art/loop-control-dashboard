// ntfy로 사람이 반드시 필요한 순간에만 푸시 알림을 보낸다.
// 발송은 항상 비동기·베스트에포트이며 실패해도 루프를 막지 않는다.
using System.Text;
using System.Text.Json.Nodes;

public static class Notifier
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(10) };

    // 가드레일 정지를 알린다.
    public static void NotifyGuardrailHalted(NtfyOptions options, string projectName, string text)
    {
        Send(options, "가드레일 정지", $"{projectName}: {text}", urgent: true);
    }

    // 체크포인트 확인 대기를 알린다.
    public static void NotifyCheckpointPaused(NtfyOptions options, string projectName, string checkpointId)
    {
        Send(options, "체크포인트 확인 필요", $"{projectName}: {checkpointId}", urgent: false);
    }

    // 1층 검토 완료 후 결재 대기 도달을 알린다.
    public static void NotifyReviewPending(NtfyOptions options, string projectName, string proposalTitle, string verdict)
    {
        Send(options, "결재함 도착", $"{projectName}: {proposalTitle} (1층 판정: {verdict})", urgent: false);
    }

    // 손상 데이터 자동 복원을 알린다.
    public static void NotifyRestored(NtfyOptions options, string projectName)
    {
        Send(options, "데이터 자동 복원", $"{projectName}: 파손된 파일을 최근 복원 지점으로 되돌렸다.", urgent: false);
    }

    // ntfy JSON publish API로 알림을 비동기 발송한다. 실패는 콘솔에만 남긴다.
    private static void Send(NtfyOptions options, string title, string message, bool urgent)
    {
        if (!options.Enabled || string.IsNullOrWhiteSpace(options.Topic))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var payload = new JsonObject
                {
                    ["topic"] = options.Topic,
                    ["title"] = title,
                    ["message"] = message,
                    ["priority"] = urgent ? 4 : 3,
                };
                using var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
                using var response = await Client.PostAsync(options.Server.TrimEnd('/') + "/", content);

                if (!response.IsSuccessStatusCode)
                {
                    Console.Error.WriteLine($"[ntfy] send failed: {(int)response.StatusCode}");
                }
            }
            catch (Exception error)
            {
                Console.Error.WriteLine($"[ntfy] send error: {error.Message}");
            }
        });
    }
}

public sealed record NtfyOptions(bool Enabled, string Server, string Topic);
