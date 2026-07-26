// 코덱스 배타 쓰기 영역(ADR-002)을 선언하는 한 곳.
//
// 이 상수가 조율자 쪽(server/ 루트)에 있는 이유: 영토의 경계를 정하는 것은 **경계를 지키는 쪽이
// 아니라 경계를 긋는 쪽**의 일이다. server/Harness/에 두면 코덱스가 자기 영토를 스스로 넓힐 수 있다.
//
// 값 변경은 사람 결재다(BASELINE-CHANGES.md). skills/는 2026-07-26 결재로 들어왔다.
//
// 읽는 쪽이 둘이다 — 쏘기 전에 요청을 거르는 CodexHarnessLauncherCli와, 조율자가 영토를 침범했는지
// 보는 territory-check(TERR-01). 목록을 두 벌 두면 한쪽만 고쳐질 때 두 검사가 다른 답을 낸다.
// RequiredGateCommands와 SelfTestGateCounts에서 이미 두 번 겪었다.
internal static class CodexTerritory
{
    internal static readonly string[] Roots = ["server/Harness/", "skills/", "docs/qa/"];

    // 경로가 코덱스 영토 안인지 본다. 구분자는 '/'로 맞춘 뒤 비교한다 — 윈도우 경로가 섞여 들어온다.
    internal static bool Contains(string path)
    {
        var normalized = path.Replace('\\', '/');
        return Roots.Any(root => normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase));
    }
}
