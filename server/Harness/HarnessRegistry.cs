// 하네스 이름→핸들러 표를 한 곳에서 관리한다. 이후 하네스 추가는 이 파일과 새 하네스 파일만으로 가능하다.

internal static class HarnessRegistry
{
    private static readonly Dictionary<string, Func<string[], int>> Handlers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["e2e-usage"] = E2EUsageCli.Run,
        ["gate-clean"] = GateCleanCli.Run,
        ["hs-scan"] = HsScanCli.Run,
        ["claim-check"] = ClaimCheckCli.Run,
        ["doc-integrity"] = DocIntegrityCli.Run,
        ["launch-check"] = LaunchCheckCli.Run,
        ["scope-check"] = ScopeCheckCli.Run,
        ["build-verify"] = BuildVerifyCli.Run,
        ["path-guard-check"] = PathGuardCheckCli.Run,
        ["call-integrity-check"] = CallIntegrityCheckCli.Run,
        ["template-sync-check"] = TemplateSyncCheckCli.Run,
        ["project-api-edge-check"] = ProjectApiEdgeCheckCli.Run,
        ["handoff-integrity"] = HandoffIntegrityCli.Run,
        ["context-pack-integrity"] = ContextPackIntegrityCli.Run,
    };

    // 이름이 표에 있으면 해당 하네스를 실행하고 exit code를 반환한다. 없으면 null을 반환한다.
    internal static int? TryRun(string[] args)
    {
        var name = args.Length > 0 ? args[0] : "";
        return Handlers.TryGetValue(name, out var handler) ? handler(args) : null;
    }
}
