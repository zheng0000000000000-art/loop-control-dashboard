// CLI 옵션 이름과 값 개수를 한 곳에서 검증한다.
// 모르는 옵션을 조용히 무시하면 오타가 "옵션 없음"이 되어 다른 것을 재고 성공으로 끝난다.
// 2026-07-26 실측: di-completion-check --manifestt 가 픽스처 대신 production을 재고 exit 0 PASS,
// state-transition apply --dry-run-flag 가 dry-run이 아니라 실제 상태 쓰기가 됐다.
internal static class CliOptions
{
    // startIndex부터의 인자에서 알려진 옵션만 허용한다. 문제가 있으면 사유를, 없으면 null을 준다.
    // valueOptions는 값을 하나 요구하고, switches는 값을 받지 않는다 — 옵션마다 개수가 달라
    // "모든 --는 값이 필요하다"로 뭉뚱그리면 스위치가 깨진다.
    internal static string? Validate(
        string[] args,
        int startIndex,
        IReadOnlyCollection<string> valueOptions,
        IReadOnlyCollection<string> switches)
    {
        for (var i = startIndex; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal)) continue;

            var name = args[i][2..];
            if (switches.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
            if (!valueOptions.Contains(name, StringComparer.OrdinalIgnoreCase))
                return $"unknown-option: --{name}";

            // 값 자리에 또 다른 옵션이 오거나 인자가 끝나면 값이 없는 것이다.
            // 여기서 넘어가면 그 옵션은 "지정되지 않음"으로 처리되어 기본 동작이 실행된다.
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                return $"missing-option-value: --{name}";

            i++;
        }

        return null;
    }
}
