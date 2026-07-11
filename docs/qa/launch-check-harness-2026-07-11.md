# H-00 launch-check harness QA

- actor: codex
- target: H-00 `launch-check`
- latest commit checked: `f5615a4`
- owned area touched: `server/Harness/`, `docs/qa/`, `docs/handoff/`
- referenced skills/docs:
  - `docs/handoff/CODEX-AUTO-15min-routine.md`
  - `skills/common/root-cause-diagnosis.md`
  - `skills/common/hs-gate.md`
  - `skills/common/executor-launch.md`
  - `docs/handoff/CODEX-QUEUE.md`

## Data existence check

`launch-check` has real input data to judge: `FAIL-2026-013` records prompt truncation, `docs/handoff/SONNET-QUEUE.md` and `docs/handoff/SESSION-HANDOFF-2026-07-11-reviewer.md` require `ACK-<taskId>` echo-back, and `outputs/sonnet-*.out.log` contains actual launch output logs.

## Harness results

| Command | Exit code | Result |
| --- | ---: | --- |
| `dotnet run --project server -c Release -- hs-scan` | 1 | `failureCaseCount=14`; HS-GATE required |
| `dotnet build server -c Release` | 0 | warnings=0, errors=0 |
| `dotnet run --project server -c Release --no-build -- launch-check TEST-123 <temp-pass-log>` | 0 | `ackFound=true`, `firstLineExact=true` |
| `dotnet run --project server -c Release --no-build -- launch-check TEST-123 <temp-fail-log>` | 1 | `ackFound=false` |
| `dotnet run --project server -c Release --no-build -- launch-check ORCH-01 outputs/sonnet-ORCH01.out.log` | 1 | `ackFound=false`; current ORCH-01 output is not tied to launch ACK |
| `dotnet run --project server -c Release --no-build -- verify-behavior` | 0 | `behaviorEqual=true` |
| `dotnet run --project server -c Release --no-build -- measure dev-pack` | 1 | `violationCount=4` |
| `dotnet run --project server -c Release --no-build -- hs-scan` | 1 | candidates remain; recorded in `HS-CANDIDATES.md` |

## Notes

The harness judges only by process exit code. It does not parse success text with regex. Missing ACK is exit 1, usage/unexpected errors are exit 2, and ACK found is exit 0.
