# H-0 scope-check harness QA

- actor: codex
- target: H-0 `scope-check`
- latest commit checked: `f5615a4`
- owned area touched: `server/Harness/`, `docs/qa/`, `docs/handoff/`
- referenced skills/docs:
  - `docs/handoff/CODEX-AUTO-15min-routine.md`
  - `skills/common/root-cause-diagnosis.md`
  - `skills/common/hs-gate.md`
  - `docs/handoff/CODEX-QUEUE.md`
  - `docs/directives/_header.md`

## Data existence check

`scope-check` has real input data to judge: directives now contain `## 허용 파일 (allowlist)` sections, and `git status --porcelain` provides the actual changed file set. This avoids the earlier gate-audit mistake of scoring a harness before the data existed.

## Harness results

| Command | Exit code | Result |
| --- | ---: | --- |
| `dotnet run --project server -c Release -- hs-scan` | 1 | `executor-orchestration(6)` candidate remains |
| `dotnet build server -c Release` | 0 | warnings=0, errors=0 |
| `dotnet run --project server -c Release --no-build -- scope-check <temp-allow-all>` | 0 | `outOfScopeCount=0` |
| `dotnet run --project server -c Release --no-build -- scope-check docs/handoff/queue/directive-ORCH01-observer.md` | 1 | `outOfScopeCount=17` |
| `dotnet run --project server -c Release --no-build -- scope-check docs/handoff/queue/directive-HOOK01-harness-registry.md` | 1 | `outOfScopeCount=15` |
| `dotnet run --project server -c Release --no-build -- verify-behavior` | 0 | `behaviorEqual=true` |
| `dotnet run --project server -c Release --no-build -- measure dev-pack` | 1 | `violationCount=4` |
| `dotnet run --project server -c Release --no-build -- hs-scan` | 1 | candidate remains; recorded in `HS-CANDIDATES.md` |

## Notes

The harness is read-only. It reports out-of-scope files and exits 1, but does not revert or modify another executor's output.
