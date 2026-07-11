// Launch output arrival check harness.
// Fails when an executor log does not echo ACK-<taskId>, proving the launch prompt did not arrive intact.
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class LaunchCheckCli
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // launch-check entry. exit 0=ACK found, 1=ACK missing/log missing, 2=usage or unexpected error.
    internal static int Run(string[] args)
    {
        try
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("{\"error\":\"usage: launch-check <taskId> <logPath>\"}");
                return 2;
            }

            var taskId = args[1].Trim();
            var logPath = args[2].Trim();
            if (string.IsNullOrWhiteSpace(taskId) || string.IsNullOrWhiteSpace(logPath))
            {
                Console.Error.WriteLine("{\"error\":\"taskId and logPath are required\"}");
                return 2;
            }

            var root = GitTools.FindRepoRoot();
            var fullPath = Path.GetFullPath(Path.IsPathRooted(logPath) ? logPath : Path.Combine(root, logPath));
            var expectedAck = $"ACK-{taskId}";
            var exists = File.Exists(fullPath);
            var text = exists ? File.ReadAllText(fullPath) : "";
            var firstNonEmptyLine = text
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n')
                .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))
                ?.Trim() ?? "";

            var ackFound = text.Contains(expectedAck, StringComparison.Ordinal);
            var firstLineExact = string.Equals(firstNonEmptyLine, expectedAck, StringComparison.Ordinal);

            var report = new JsonObject
            {
                ["harness"] = "launch-check",
                ["taskId"] = taskId,
                ["logPath"] = Path.GetRelativePath(root, fullPath).Replace('\\', '/'),
                ["expectedAck"] = expectedAck,
                ["logExists"] = exists,
                ["ackFound"] = ackFound,
                ["firstLineExact"] = firstLineExact,
                ["firstNonEmptyLine"] = firstNonEmptyLine,
                ["verdict"] = exists && ackFound ? "PASS" : "FAIL",
                ["note"] = "ACK absence means the executor output is not tied to the launch prompt and must be discarded.",
            };

            Console.WriteLine(report.ToJsonString(JsonOptions));
            return exists && ackFound ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{{\"error\":\"launch-check failed: {ex.Message}\"}}");
            return 2;
        }
    }
}
