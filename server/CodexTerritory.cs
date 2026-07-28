// 코덱스 배타 쓰기 영역(ADR-002)을 선언하는 한 곳.
using System.Text.Json.Nodes;
//
// 이 파일이 조율자 쪽(server/ 루트)에 있는 이유: 영토의 경계를 정하는 것은 **경계를 지키는 쪽이
// 아니라 경계를 긋는 쪽**의 일이다. server/Harness/에 두면 코덱스가 자기 영토를 스스로 넓힐 수 있다.
//
// 읽는 쪽이 둘이다 — 쏘기 전에 요청을 거르는 CodexHarnessLauncherCli와, 조율자가 영토를 침범했는지
// 보는 territory-check(TERR-01). 목록을 두 벌 두면 한쪽만 고쳐질 때 두 검사가 다른 답을 낸다.
// RequiredGateCommands와 SelfTestGateCounts에서 이미 두 번 겪었다.
//
// 2026-07-27 — 요청이 영토를 선언할 수 있게 열었다(사람 결재, BASELINE-CHANGES).
// **이것은 완화다.** 융합으로 이 실행자를 다른 저장소에 겨누려면 `server/Harness/`류의 고정 목록이
// 성립하지 않는다(대상 저장소의 구조가 다르다). 대신 최소한의 이가 남는다 — §RootsRejection.
//
// 2026-07-28(ADR-023 §2-1 첫 조각, 사람 승인 "a로 해줘") — 영토 정의의 원본을 team-loop으로
// 옮기는 첫 배선. **이 커밋은 행동을 바꾸지 않는다**: override 파일이 없으면 지금까지와 똑같이
// 하드코딩 값을 쓴다. override가 있어도 무효(빈 목록·저장소 전체·절대경로 등)면 하드코딩으로
// 떨어진다(fail-closed) — 있는 파일을 못 읽어서 영토가 넓어지는 일은 없다.
internal static class CodexTerritory
{
    // 이 저장소의 기본 영토. 요청이 선언하지 않으면 이것을 쓴다. 값 변경은 사람 결재다.
    // skills/는 2026-07-26 결재로 들어왔다.
    internal static readonly string[] Roots = ["server/Harness/", "skills/", "docs/qa/"];

    // team-loop이 주는 영토 정의를 놓을 자리(대상 저장소 기준 상대경로). 아직 아무도 이 파일을
    // 채우지 않는다 — 채우는 것은 이 배선과 별개의 사람 결재 대상이다.
    internal const string OverrideRelPath = "docs/context/team-loop-contract/territory-roots.json";

    // 대상 저장소에 유효한 override가 있으면 그것을, 없거나 무효하면 하드코딩 기본값을 돌려준다.
    // 판정 두 곳(CodexHarnessLauncherCli·territory-check)이 반드시 이 메서드 하나를 통해야 한다 —
    // 각자 override를 따로 읽으면 한쪽만 갱신됐을 때 쏘는 문과 잡는 문이 다른 답을 낸다.
    internal static IReadOnlyList<string> EffectiveRoots(string repoRoot)
    {
        var path = Path.Combine(repoRoot, OverrideRelPath);
        if (!File.Exists(path)) return Roots;

        JsonObject? doc;
        try { doc = JsonNode.Parse(File.ReadAllText(path))?.AsObject(); }
        catch { return Roots; }

        var declared = (doc?["roots"] as JsonArray ?? [])
            .Select(node => node?.ToString() ?? "")
            .ToArray();
        if (declared.Length == 0) return Roots;
        return RootsRejection(declared) is null ? declared : Roots;
    }

    // 경로가 기본 영토 안인지 본다. territory-check가 이것을 쓴다.
    internal static bool Contains(string path) => Contains(path, Roots);

    // 경로가 주어진 영토 안인지 본다. 구분자는 '/'로 맞춘 뒤 비교한다 — 윈도우 경로가 섞여 들어온다.
    internal static bool Contains(string path, IReadOnlyList<string> roots)
    {
        var normalized = path.Replace('\\', '/');
        return roots.Any(root => normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase));
    }

    // 요청이 선언한 영토가 쓸 만한지 본다. 문제가 있으면 사유를, 없으면 null.
    //
    // 완화를 받았다고 무제한이 되는 것은 아니다. **저장소 전체를 영토로 선언하는 것**과
    // **밖으로 나가는 것**은 막는다. 그 둘을 허용하면 영토 검사가 있으나 마나가 된다.
    internal static string? RootsRejection(IReadOnlyList<string> roots)
    {
        if (roots.Count == 0) return "territory-roots-empty";
        foreach (var raw in roots)
        {
            var root = raw.Replace('\\', '/');
            if (root.Length == 0) return "territory-root-empty";
            // 저장소 전체 선언 금지. "."·"/"·"./"·"**" 는 영토를 없애는 것과 같다.
            if (root is "." or "/" or "./" or "**" or "**/") return "territory-root-is-whole-repo";
            // 상위로 나가거나 절대 경로면 대상 저장소 밖을 가리킬 수 있다.
            if (root.StartsWith("..", StringComparison.Ordinal) || root.Contains("../", StringComparison.Ordinal))
                return "territory-root-escapes-repo";
            if (root.StartsWith('/') || (root.Length > 1 && root[1] == ':')) return "territory-root-is-absolute";
            // 끝에 구분자가 없으면 형제 접두사가 걸린다(server/ 와 server-old/).
            if (!root.EndsWith('/')) return "territory-root-must-end-with-slash";
        }
        return null;
    }
}
