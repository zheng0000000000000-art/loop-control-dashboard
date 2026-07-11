# P0-05 context-pack-integrity data gate

- actor: codex
- target: P0-05 `context-pack-integrity`
- checked commit: `532b0d7`
- decision: blocked before harness creation

## Data Gate

`skills/common/hs-gate.md` section 2 asks whether the harness has real data to inspect. For `context-pack-integrity`, the required data is a machine-readable Context Pack or directive field like:

```json
{
  "requiredInputs": [
    { "path": "docs/example.md", "sha256": "...", "sectionIds": ["CONTRACT-001"] }
  ],
  "readOrder": ["docs/example.md"]
}
```

Current repository search found no implemented `requiredInputs` data in active directives or context packs. Occurrences are plan/queue text only, not an executable contract.

## Commands

| command | exit code | result |
| --- | ---: | --- |
| `dotnet run --project server -c Release --no-build -- hs-scan` | 1 | `executor-orchestration(6)` trigger |
| `rg -n "requiredInputs\|sha256\|Context Pack\|context pack\|required inputs" docs server skills` | 0 | plan/queue references found; no active `requiredInputs` contract found |
| `dotnet build server -c Release` | 0 | 0 warnings / 0 errors |
| `dotnet run --project server -c Release --no-build -- build-verify` | 0 | `verdict=PASS` |
| `dotnet run --project server -c Release --no-build -- handoff-integrity` | 1 | final re-run: `failureCount=1`; `P0-04` is `done` in WORKSTATE but open in `docs/handoff/SONNET-QUEUE.md` |
| `dotnet run --project server -c Release --no-build -- measure dev-pack` | 1 | `violationCount=2`; `functionsWithoutComment=0`; `maxFunctionLength=99` at `server/ProjectionCli.cs:168-266` |
| `dotnet run --project server -c Release --no-build -- doc-integrity` | 0 | `brokenCount=0` |

## Result

No `context-pack-integrity` harness was created this cycle. Creating it before `requiredInputs` exists would repeat the gate-audit mistake: the harness would inspect a proxy or invented convention instead of real contract data.

Separate observation: `handoff-integrity` is now working as a live gate. After P0-04 updated `WORKSTATE`, it reported a queue/status mismatch rather than a hash issue.

Required predecessor: add a minimal machine-readable Context Pack/directive schema with `requiredInputs` path and hash fields. Once that exists in at least one active directive, Codex can implement P0-05 as the second and final new Phase 0 harness.

## Referenced Skills

- `skills/common/root-cause-diagnosis.md`
- `skills/common/hs-gate.md`
