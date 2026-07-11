# P0-03 handoff-integrity harness

- actor: codex
- target: P0-03 `handoff-integrity`
- checked commit: `07430b3` latest visible HEAD at start of cycle
- scope: `server/Harness/HandoffIntegrityCli.cs`, `server/Harness/HarnessRegistry.cs`, `docs/qa/`, `docs/handoff/sessions/`, `docs/handoff/HS-CANDIDATES.md`

## Data gate

`skills/common/hs-gate.md` section 2 requires checking that the harness has real data to inspect before building it.

- `docs/handoff/WORKSTATE.json`: exists and parses.
- `changedFiles[].path`: exists for current `FIX-07` entries.
- `changedFiles[].sha256/hash`: absent for all 3 current entries, which is a real machine-verification gap.
- completion artifact: `docs/verification/fix07-appjs-long-functions.md` exists.
- queue status rows: queue documents exist; no blocking mismatch was observed by the new harness before hash failures.

## Commands

| command | exit code | result |
| --- | ---: | --- |
| `dotnet run --project server -c Release --no-build -- hs-scan` | 1 | `executor-orchestration(6)` trigger; HS-GATE record updated |
| `dotnet build server -c Release` | 0 | 0 warnings / 0 errors |
| `dotnet run --project server -c Release --no-build -- build-verify` | 0 | `verdict=PASS`, `locked=false` |
| `dotnet run --project server -c Release --no-build -- handoff-integrity` | 1 | `failureCount=3`; all current `changedFiles` lack `sha256/hash` |
| `dotnet run --project server -c Release --no-build -- measure dev-pack` | 1 | `violationCount=2` |
| `dotnet run --project server -c Release --no-build -- doc-integrity` | 0 | `brokenCount=0`, `verdict=INTACT` |

## Result

Implemented P0-03. The harness currently fails the repository handoff, as intended, because `WORKSTATE.json` cannot provide machine-verifiable hashes for the current `changedFiles`.

This is not a sonnet code regression claim. It is a handoff contract gap: future WORKSTATE updates need `sha256` or `hash` per changed file if the plan requires hash-backed handoff verification.

## Referenced Skills

- `skills/common/root-cause-diagnosis.md`
- `skills/common/hs-gate.md`

