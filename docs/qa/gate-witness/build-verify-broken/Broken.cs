// 이 픽스처가 반대 결과를 내기 시작하면 검사가 죽은 것이다. 고치지 마라.
internal sealed class Broken
{
    private readonly MissingType value = new();
}
