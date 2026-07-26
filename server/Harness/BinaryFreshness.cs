// 실행 중인 서버 바이너리와 서버 소스의 수정 시각을 비교한다.
// bin/obj 산출물을 제외한 최신 C# 입력 파일을 진단 정보와 함께 반환한다.
internal static class BinaryFreshness
{
    // 현재 프로세스 바이너리가 server 소스보다 낡았는지 잰다.
    internal static BinaryFreshnessResult Measure(string root)
    {
        var binaryPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(binaryPath) || !File.Exists(binaryPath))
            return new BinaryFreshnessResult(false, binaryPath ?? "", "", "", "");

        var binaryWrittenAt = File.GetLastWriteTimeUtc(binaryPath);
        var serverDir = Path.Combine(root, "server");
        var newestPath = "";
        var newestWrittenAt = DateTime.MinValue;

        foreach (var pattern in new[] { "*.cs", "*.csproj" })
        {
            foreach (var file in Directory.EnumerateFiles(serverDir, pattern, SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(serverDir, file).Replace('\\', '/');
                if (relative.StartsWith("bin/", StringComparison.OrdinalIgnoreCase)
                    || relative.StartsWith("obj/", StringComparison.OrdinalIgnoreCase))
                    continue;

                var writtenAt = File.GetLastWriteTimeUtc(file);
                if (writtenAt <= newestWrittenAt) continue;
                newestWrittenAt = writtenAt;
                newestPath = relative;
            }
        }

        return new BinaryFreshnessResult(
            newestWrittenAt > binaryWrittenAt,
            binaryPath,
            binaryWrittenAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            newestPath,
            newestWrittenAt == DateTime.MinValue ? "" : newestWrittenAt.ToString("yyyy-MM-ddTHH:mm:ssZ"));
    }
}

internal sealed record BinaryFreshnessResult(
    bool Stale,
    string BinaryPath,
    string BinaryWrittenAt,
    string NewestSource,
    string NewestSourceWrittenAt);
