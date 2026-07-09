// 서버 오케스트레이션 분리 지점을 표시한다.
// 후속 반입에서 Program.cs 라우트 조립 코드를 옮길 대상이다.
public static class Orchestrator
{
    // 오케스트레이션 분리 대상 파일이 준비됐는지 반환한다.
    public static bool IsReady()
    {
        return true;
    }
}
