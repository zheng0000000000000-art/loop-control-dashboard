# 06C2A Verification - Trust Origin Verification Hardening

## DI

- DI: `DI-06C2A-TRUST-ORIGIN-VERIFICATION-HARDENING`
- Type: implementation hardening
- Actor: Codex
- Start baseline: `ce85707 feat(recovery): add fail-closed fault inspection fixtures`
- Production Trust Origin declaration: not performed
- Production `WORKSTATE.json` changed: false
- Production applier log changed: false
- Production trust-origin record created: false

## Changed Files

- `server/TrustOriginCli.cs`
- `docs/handoff/TRUST-ORIGIN-BOOTSTRAP.md`
- `docs/verification/06c2a-trust-origin-verification-hardening.md`

## Referenced Skills

- `skills/common/directive-writing.md`
- `skills/common/executor-launch.md`
- `skills/common/hs-gate.md`
- `skills/common/powershell-encoding.md`
- `skills/common/root-cause-diagnosis.md`
- `skills/common/verification.md`

## Hardening Contract

- `declare` requires integration gate evidence bound to current HEAD and current state/log hashes.
- Record stores `integrationGateEvidenceSha256`.
- Record stores `baselineReconciliationReportSha256`.
- Record stores `declaredLegacyWarnings`.
- `verify` independently checks schema version, epoch, declaration type, declaration status, claimed actor provenance, failure-set hash, reconciliation report hash, baseline snapshots, direct-writer gate, high-risk fail-closed, and automatic launcher disabled.
- Post-origin log delta must use complete v2 success bindings.
- Post-origin state binding fields, when present, must match log binding fields.
- `trust-origin evidence --out <file>` creates only a draft evidence file and does not mark gates PASS.

## Fixture Additions

`trust-origin --self-test` now covers 24 cases, including:

- manual record with invalid `trustEpoch`
- manual record with invalid `declarationStatus`
- tampered `declaredLegacyFailureSetSha256`
- evidence missing build PASS
- post-origin state/log binding mismatch
- baseline warning report hash binding

## Verification Commands

Final sequential verification:

| gate | command | result |
| --- | --- | --- |
| release build | `dotnet build server -c Release` | PASS, exit 0 |
| trust-origin fixtures | `dotnet run --project server -c Release --no-build -- trust-origin --self-test` | PASS, exit 0, `casesRun=24` |
| 05H fixture regression | all `docs/qa/fixtures/reconciliation/*` with expected exit codes | PASS, exit 0 |
| 06C-1 regression | `dotnet run --project server -c Release --no-build -- state-transition --self-test` | PASS, exit 0, `casesRun=19` |
| 06H regression | `dotnet run --project server -c Release --no-build -- recovery --self-test` | PASS, exit 0, `casesRun=8` |
| 05H self-test | `dotnet run --project server -c Release --no-build -- handoff-integrity --self-test` | PASS, exit 0, `casesRun=6` |
| doc-integrity | `dotnet run --project server -c Release --no-build -- doc-integrity` | PASS, exit 0, `brokenCount=0` |
| callsite gate | `dotnet run --project server -c Release --no-build -- state-transition-callsite-check` | PASS, exit 0, `legacyCallsiteCount=0` |
| dev-pack | `dotnet run --project server -c Release --no-build -- measure dev-pack` | PASS, exit 0, `violationCount=0`, `overallStatus=completed` |
| git diff check | `git diff --check` | PASS, exit 0 |

`measure dev-pack` updated dashboard runtime files with timestamp and accumulated run-log changes. Those generated files were inspected and restored.

## Dev-pack Gate

`{"gate":"dev-pack","violations":0,"attempt":1}`

## At-rest Separation

Known existing at-rest gap remains outside this checkpoint:

- failureCode: `state-transition-not-logged`
- subject: `DI0004-BLOCKED-CODEX`
- introducedBy06C2A: false

## State Model

- `06C2A_IMPLEMENTATION_COMPLETE`: true
- `TRUST_ORIGIN_INFRA_COMPLETE`: true
- `TRUST_ORIGIN_DECLARE_READY`: false
- `TRUST_ORIGIN_DECLARED`: false
- `STATE_HISTORY_CONSISTENT`: false
- `TRUSTED_BASELINE`: false
- `NORMAL_TRANSITION_READY`: false
- `HIGH_RISK_TRANSITION_READY`: false
- `AUTOMATED_EXECUTION_READY`: false
- `WP_STATE_INTEGRITY_LAND_READY`: false
- `CANONICAL_MERGE_READY`: false
