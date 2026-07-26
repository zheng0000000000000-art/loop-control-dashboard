// self-test가 실제로 몇 케이스를 돌았는지 재는 한 곳.
// 표에 적힌 수를 표에서 읽은 수와 비교하면 항상 참이라 표류를 못 잡는다 — 실측값이 있어야 대조가 된다.
// 2026-07-27 실측: stateTransitionSelfTest·recoverySelfTest 두 항목이 생산자·검사자 모두 같은 표를
// 읽고 있어 대조가 공회전이었다. 케이스를 늘려도 줄여도 아무도 눈치채지 못했다.
internal static class SelfTestCensus
{
    // 임시 root에서 케이스를 돌리고 실제 개수를 준다. 세지 못하면 -1이다.
    // 0을 주면 "케이스가 없는 self-test"와 구분되지 않아 조용히 일치로 읽힐 수 있다.
    // fixture 실행이 stdout에 JSON을 쏟으므로 이 구간의 출력은 삼킨다 — 부르는 쪽의 출력은
    // JSON 하나여야 한다(2026-07-26에 trust-origin inspect가 JSON 4개를 냈다).
    internal static int Measure(string prefix, Func<string, int> countCases)
    {
        var root = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        var originalOut = Console.Out;
        using var swallowed = new StringWriter();
        Console.SetOut(swallowed);
        try
        {
            return countCases(root);
        }
        catch
        {
            return -1;
        }
        finally
        {
            Console.SetOut(originalOut);
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        }
    }
}
