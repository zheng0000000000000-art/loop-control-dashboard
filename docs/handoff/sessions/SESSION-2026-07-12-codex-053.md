# SESSION-2026-07-12-codex-053

- actor: codex
- task: P0-06 `scope-check` claim extension
- scope: `server/Harness/`, `docs/qa/`, `docs/handoff/sessions/`
- source claim ledger: `docs/handoff/FILE-CLAIMS.json` read only

## Work

- Extended `scope-check` instead of creating a new harness.
- Added optional `--actor <actor>` and `--claims <claimsPath>` arguments.
- Kept the existing directive allowlist check and added claim-based fields: `claimConflicts`, `staleClaims`, `unknownAllowlistClaims`.
- Conflict policy: changed files overlapping another actor's active claim exit 1 and report file, actor, taskId, pid, claimId.
- Stale policy: active claim with dead PID is warning-only. Human decides whether to mark it expired.
- Unknown allowlist policy: claim with `allowlistSource: null` is warning-only and is not treated as an empty safe allowlist, even when released.

## Harnesses

| Harness | Command | Exit | Result |
| --- | --- | ---: | --- |
| build | `dotnet build server -c Release` | 0 | warnings 0, errors 0 |
| scope-check conflict injection | `dotnet run --project server -c Release --no-build -- scope-check <temp allow-all directive> --claims <temp conflict claims> --actor codex` | 1 | `claimConflictCount: 1`, `server/Harness/ScopeCheckCli.cs` overlapped actor `sonnet` task `INJECT-CONFLICT` |
| scope-check stale injection | `dotnet run --project server -c Release --no-build -- scope-check <temp allow-all directive> --claims <temp stale claims> --actor codex` | 0 | `staleClaimCount: 1`, `unknownAllowlistClaimCount: 1` |
| scope-check normal current claims | `dotnet run --project server -c Release --no-build -- scope-check <temp allow-all directive> --actor codex` | 0 | default claim ledger, all released, conflict/stale counts 0, `unknownAllowlistClaimCount: 1` for released `SMOKE-01` |
| dev-pack gate | `dotnet run --project server -c Release -- measure dev-pack` | 0 | violationCount 0 |

{"gate":"dev-pack","violations":0,"attempt":1}

## 참조한 스킬

- `skills/common/verification.md`
- `skills/common/root-cause-diagnosis.md`
- `skills/common/hs-gate.md`
- `skills/common/executor-launch.md`
- `skills/common/directive-writing.md`

## 지표는 만족했으나 목적은 미달인 부분

- 자동 release/expire 정리는 하지 않았다. 이번 목적은 검출과 보고이며, 원장 변경은 발사 래퍼와 사람 판단 영역이다.
- `allowlistSource: null`인 claim은 충돌 없음으로 단정하지 않고 `unknownAllowlistClaims` 경고로 표면화한다. paths가 비어 있으면 겹침 판정 데이터가 없기 때문이다.
- 정상 경로 검증은 현재 dirty worktree 때문에 임시 allow-all 지시서를 사용했다. 기존 allowlist 대조 로직 자체는 그대로 유지되어 `outOfScopeCount`가 verdict에 계속 반영된다.
