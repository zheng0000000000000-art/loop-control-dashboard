# 06H Verification - Recovery Fault Infrastructure

## DI Type

- DI: `DI-06H-RECOVERY-FAULT-INFRA`
- Type: implementation + verification fixture
- Actor: Codex

## Baseline

- Prior pushed integration commit: `4c2b437 feat(trust-origin): implement bootstrap epoch and delta reconciliation`
- Branch: `wp/state-integrity`
- Production Trust Origin declaration: not performed
- Production `WORKSTATE.json` changed: false
- Production applier log changed: false

## Changed Files

- `server/RecoveryCli.cs`
- `server/Cli/CliRouter.cs`
- `docs/handoff/RECOVERY.md`
- `docs/qa/06h-recovery.md`
- `docs/verification/06h-recovery-fixture.md`

## Referenced Skills

- `skills/common/directive-writing.md`
- `skills/common/executor-launch.md`
- `skills/common/hs-gate.md`
- `skills/common/powershell-encoding.md`
- `skills/common/root-cause-diagnosis.md`
- `skills/common/verification.md`

## Contract

- `recovery inspect` classifies state/log/pending conditions.
- `recovery evidence --out <dir>` writes quarantine evidence only.
- RECOVERY apply is not enabled.
- High-risk transitions remain fail-closed.
- Automatic execution remains disabled.
- Draft recovery requests are explicitly not executable.
- Production state/log/trust-origin records are not changed.

## Fixture Results

Final sequential checkpoint verification:

| gate | command | result |
| --- | --- | --- |
| release build | `dotnet build server -c Release` | PASS, exit 0 |
| recovery fixtures | `dotnet run --project server -c Release --no-build -- recovery --self-test` | PASS, exit 0, `casesRun=8` |
| 06C-1 regression | `dotnet run --project server -c Release --no-build -- state-transition --self-test` | PASS, exit 0, `casesRun=19` |
| 06C-2 regression | `dotnet run --project server -c Release --no-build -- trust-origin --self-test` | PASS, exit 0, `casesRun=18` |
| 05H self-test | `dotnet run --project server -c Release --no-build -- handoff-integrity --self-test` | PASS, exit 0, `casesRun=6` |
| 05H fixtures | all `docs/qa/fixtures/reconciliation/*` with expected exit codes | PASS, exit 0 |
| doc-integrity | `dotnet run --project server -c Release --no-build -- doc-integrity` | PASS, exit 0, `brokenCount=0` |
| callsite gate | `dotnet run --project server -c Release --no-build -- state-transition-callsite-check` | PASS, exit 0, `legacyCallsiteCount=0` |
| dev-pack | `dotnet run --project server -c Release --no-build -- measure dev-pack` | PASS, exit 0, `violationCount=0`, `overallStatus=completed` |
| git diff check | `git diff --check` | PASS, exit 0 |

06H self-test cases:

- `pending-pre-no-success`
- `pending-post-success`
- `pending-post-no-success`
- `pending-ambiguous`
- `state-only-gap`
- `conflicting-success`
- `evidence-package`
- `high-risk-stays-closed`

## Dev-pack Gate

`{"gate":"dev-pack","violations":0,"attempt":1}`

`measure dev-pack` updated dashboard runtime files with timestamp and accumulated run-log changes. Those generated files were inspected and restored. They are not part of the 06H checkpoint.

## At-rest Separation

Known existing failure remains outside 06H:

- failureCode: `state-transition-not-logged`
- subject: `DI0004-BLOCKED-CODEX`
- introducedBy06H: false

## State Model

- `06H_IMPLEMENTATION_COMPLETE`: true
- `06H_FIXTURE_VERIFIED`: true
- `RECOVERY_FAULT_INFRA_READY`: true
- `RECOVERY_EVIDENCE_ONLY`: true
- `RECOVERY_APPLY_READY`: false
- `TRUST_ORIGIN_DECLARED`: false
- `STATE_HISTORY_CONSISTENT`: false
- `TRUSTED_BASELINE`: false
- `NORMAL_TRANSITION_READY`: false
- `HIGH_RISK_TRANSITION_READY`: false
- `AUTOMATED_EXECUTION_READY`: false
- `WP_STATE_INTEGRITY_LAND_READY`: false
- `CANONICAL_MERGE_READY`: false
- `PUSH_READY`: false
