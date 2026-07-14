# 06H Recovery Fault QA

## Purpose

06H verifies recovery and fault-injection infrastructure without activating recovery.

The core rule is:

`Generator != Executor != Verifier`, and recovery diagnosis is not recovery approval.

## Fixture Coverage

`dotnet run --project server -c Release --no-build -- recovery --self-test`

Expected:

- exit code: 0
- `selfTest=recovery-fault-infra`
- `verdict=PASS`
- `casesRun=8`

Cases:

- `pending-pre-no-success`
- `pending-post-success`
- `pending-post-no-success`
- `pending-ambiguous`
- `state-only-gap`
- `conflicting-success`
- `evidence-package`
- `high-risk-stays-closed`

## Evidence Contract

`recovery evidence --out <dir>` creates only quarantine evidence:

- `quarantine-manifest.json`
- `recovery-request.json`
- `hard-rollback.json`

It must not mutate WORKSTATE or applier-log bytes.

## Current Production State

Production at-rest reconciliation is still expected to fail on the existing legacy gap:

- `DI0004-BLOCKED-CODEX`
- `state-transition-not-logged`

This is not introduced by 06H.

## Not Verified By 06H

- actual RECOVERY apply
- verified human receipt issuance
- Trust Origin declaration
- `TRUSTED_BASELINE=true`
- automatic launcher readiness
