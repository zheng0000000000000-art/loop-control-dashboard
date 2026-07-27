// 하네스들이 공유하는 JSON 직렬화 옵션 한 벌.
// 파일마다 따로 만들면 한쪽만 바뀐다 — 실제로 TypeInfoResolver 가 빠져서 .NET 8 에서
// hs-scan 이 exit 2 로 죽었다. 정의는 여기 하나뿐이다.
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

internal static class HarnessJson
{
    // .NET 8 은 리플렉션 기반 직렬화를 기본으로 켜주지 않는다. TypeInfoResolver 를 명시하지 않으면
    // 런타임에 NotSupportedException 이 난다. .NET 10 에서는 안 나서 로컬만 보면 안 잡힌다.
    internal static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };
}
