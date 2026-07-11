# H-01 build-verify harness QA

- actor: codex
- target: H-01 `build-verify`
- latest commit checked: `f5615a4`
- owned area touched: `server/Harness/`, `docs/qa/`, `docs/handoff/`
- referenced skills/docs:
  - `docs/handoff/CODEX-AUTO-15min-routine.md`
  - `skills/common/root-cause-diagnosis.md`
  - `skills/common/hs-gate.md`
  - `docs/handoff/CODEX-QUEUE.md`
  - `docs/handoff/HS-CANDIDATES.md`

## Data existence check

`build-verify` has real data to judge: `dotnet build` returns an exit code, and this cycle reproduced the exact class of failure H-01 targets. Direct `dotnet build server -c Release` exited 1 because a file under `server/obj/Release/net8.0` was locked by another process. A copied temp-source Release build exited 0.

## Harness results

| Command | Exit code | Result |
| --- | ---: | --- |
| `dotnet run --project server -c Release -- hs-scan` | 1 | `executor-orchestration(6)` candidate remains |
| `dotnet build server -c Release` | 1 | external `server/obj/...rjsmrazor.dswa.cache.json` lock reproduced |
| temp source copy build, excluding `bin/obj` | 0 | warnings=0, errors=0 |
| `dotnet <temp-built-dll> build-verify` | 0 | `verdict=PASS`, `exitCode=0`, `sourceCopied=true` |
| `dotnet <temp-built-dll> build-verify server/NoSuchProject` | 2 | usage/path error path verified |
| `dotnet run --project server -c Release --no-build -- verify-behavior` | 0 | `behaviorEqual=true` |
| `dotnet run --project server -c Release --no-build -- measure dev-pack` | 1 | `violationCount=3` |
| `dotnet run --project server -c Release --no-build -- hs-scan` | 1 | candidate remains; recorded in `HS-CANDIDATES.md` |

## Notes

The harness decides PASS/FAIL from the child `dotnet build` exit code only. It copies source to a temp directory and skips `bin/obj`, so running servers and stale generated files in the repository cannot be mistaken for code failure.
