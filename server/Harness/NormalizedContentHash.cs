// 하네스들이 파일 표현 차이를 같은 방식으로 제거해 내용 해시를 계산한다.
using System.Security.Cryptography;
using System.Text;

internal static class NormalizedContentHash
{
    // BOM 제거, 줄바꿈 통일, 줄 후행공백 제거, 끝 개행 통일 후 SHA-256을 계산한다.
    internal static string Compute(byte[] raw)
    {
        var bytes = HasBom(raw) ? raw[3..] : raw;
        var text = new UTF8Encoding(false).GetString(bytes).Replace("\r\n", "\n").Replace('\r', '\n');
        var normalized = string.Join("\n", text.Split('\n').Select(line => line.TrimEnd(' ', '\t')))
            .TrimEnd('\n') + "\n";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    // UTF-8 BOM이 있는지 확인한다.
    internal static bool HasBom(byte[] bytes)
        => bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
}
