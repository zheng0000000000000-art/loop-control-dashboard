# 06C-2 Verification - Bootstrap Trust Origin Infra

## Baseline

- DI: `DI-06C2-BOOTSTRAP-TRUST-ORIGIN-INFRA`
- Baseline commit: `b0c86c9 feat(state-transition): implement v2 apply and idempotency core`
- Start checks: `git merge-base --is-ancestor b0c86c9 HEAD` passed, start worktree clean.
- Production Trust Origin declaration: not performed.
- Production `WORKSTATE.json` changed: false.
- Production applier log changed: false.

## Changed Files

- `server/TrustOriginCli.cs`
- `docs/handoff/TRUST-ORIGIN-BOOTSTRAP.md`
- `docs/verification/06c2-bootstrap-trust-origin.md`

## Referenced Skills

- `skills/common/directive-writing.md`
- `skills/common/executor-launch.md`
- `skills/common/hs-gate.md`
- `skills/common/powershell-encoding.md`
- `skills/common/root-cause-diagnosis.md`
- `skills/common/verification.md`

## Contract

- `trust-origin inspect` is read-only.
- `trust-origin declare --evidence <file>` is separate from StateApplier and does not mutate state/log, commit, tag, or push.
- `trust-origin verify` activates only committed records that satisfy ancestry, snapshot, prefix, and delta checks.
- `DECLARED_LEGACY_GAP` requires exact failure-set binding by code, subject, and detail hash.
- Baseline commit and declaration commit are separated to avoid self-reference.
- Uncommitted records are inactive.
- Baseline `appliedTransitions` and applier log prefixes are immutable after activation.
- Post-origin state/log suffixes are strictly reconciled.
- Redeclaration is blocked.
- High-risk transitions remain fail-closed.
- Automated execution remains disabled.

## Fixture Coverage

`trust-origin --self-test` runs isolated temporary Git repositories and covers:

- consistent baseline declaration
- known legacy gap declaration
- legacy failure-set mismatch
- conflict rejection
- malformed state/log rejection
- dirty worktree rejection
- baseline hash mismatch
- redeclaration rejection
- uncommitted record inactive
- declaration commit activation
- self-reference rejection
- baseline WORKSTATE prefix mutation
- baseline log prefix mutation
- post-origin normal delta
- post-origin state-only delta rejection
- post-origin log-only delta rejection
- high-risk fail-closed
- automatic execution false

## Verification Commands

Initial 06C-2 implementation verification:

| gate | command | result |
| --- | --- | --- |
| release build | `dotnet build server -c Release` | PASS, exit 0 |
| trust-origin fixtures | `dotnet run --project server -c Release --no-build -- trust-origin --self-test` | PASS, exit 0, `casesRun=18` |
| 06C-1 regression | `dotnet run --project server -c Release --no-build -- state-transition --self-test` | PASS, exit 0, `casesRun=19` |
| 05H self-test | `dotnet run --project server -c Release --no-build -- handoff-integrity --self-test` | PASS, exit 0, `casesRun=6` |
| 05H fixture regression | all `docs/qa/fixtures/reconciliation/*` fixtures | PASS, expected exit codes matched |
| doc-integrity | `dotnet run --project server -c Release --no-build -- doc-integrity` | PASS, exit 0, `brokenCount=0` |
| callsite gate | `dotnet run --project server -c Release --no-build -- state-transition-callsite-check` | PASS, exit 0, `legacyCallsiteCount=0` |
| dev-pack | `dotnet run --project server -c Release --no-build -- measure dev-pack` | PASS, exit 0, `violationCount=0`, `overallStatus=completed` |
| git diff check | `git diff --check` | PASS, exit 0 |

`measure dev-pack` updated dashboard runtime files with timestamp/run-log changes only; those generated changes were restored and are not part of this checkpoint.

## Dev-pack Gate

{"gate":"dev-pack","violations":0,"attempt":1}

## At-rest Separation

Known existing at-rest failure remains outside this checkpoint:

- failureCode: `state-transition-not-logged`
- subject: `DI0004-BLOCKED-CODEX`
- introducedBy06C2: false
- trustOriginDeclared: false

06C-2 implements the infrastructure that can later declare an exact legacy gap from a clean baseline. It does not perform the production declaration.

## Current State Model

- `06C2_IMPLEMENTATION_COMPLETE`: true
- `06C2_FIXTURE_VERIFIED`: true
- `TRUST_ORIGIN_DECLARED`: false
- `STATE_HISTORY_CONSISTENT`: false
- `TRUSTED_BASELINE`: false
- `NORMAL_TRANSITION_READY`: false
- `HIGH_RISK_TRANSITION_READY`: false
- `AUTOMATED_EXECUTION_READY`: false
- `WP_STATE_INTEGRITY_LAND_READY`: false
- `CANONICAL_MERGE_READY`: false
- `PUSH_READY`: false
