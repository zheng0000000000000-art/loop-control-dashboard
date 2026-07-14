# RECOVERY Protocol

## Current Judgment

RECOVERY is fail-closed in the current repository state.

`recovery inspect` and `recovery evidence` may classify faults and create quarantine evidence. They do not repair `WORKSTATE.json`, do not append success logs, do not clean pending journals, and do not declare `TRUSTED_BASELINE`.

Actual in-place RECOVERY apply remains disabled until all of these exist:

- valid committed Trust Origin
- `TRUSTED_BASELINE=true` derived by verification
- verified human receipt infrastructure
- recovery-specific StateApplier contract

## Allowed Now

- Run `handoff-integrity` to classify state/log consistency.
- Run `recovery inspect` to classify recovery windows.
- Run `recovery evidence --out <dir>` to create quarantine evidence and draft recovery request files.
- Preserve current state/log bytes and report to `HUMAN-INBOX` when recovery is required.
- Restore only from a clearly trusted whole-repository snapshot under human control.

## Forbidden Now

- Manual edits to `docs/handoff/WORKSTATE.json`.
- Manual edits to `docs/handoff/WORKSTATE.applier-log.jsonl`.
- Adding missing success log entries by hand.
- Cleaning pending journals by hand in production.
- Treating `claimedActor` as a verified human receipt.
- Running `state-transition apply` with `RECOVERY`, `REPLAY`, or `PHASE_CHANGE` as a bypass.
- Declaring `TRUSTED_BASELINE=true`.
- Activating automatic recovery or automatic execution.

## Recovery Classes

| condition | class | current action |
| --- | --- | --- |
| pending + pre-state + no success log | L2 | stale pending is cleanup-capable only inside StateApplier; production recovery remains manual evidence only |
| pending + post-state + matching success log | L2 | completed pending cleanup candidate; do not hand-edit production |
| pending + post-state + no success log | L2+ | `pending-transition-recovery-required`; quarantine and human decision |
| pending with contradictory hashes | L3 | `pending-state-ambiguous`; quarantine and human decision |
| `state-transition-not-logged` | L2+ | quarantine, hard rollback evidence, human decision |
| `log-transition-missing-from-state` | L2 | quarantine, clean replay only after trust baseline |
| `transition-id-collision` | L3 | no automatic recovery |
| `duplicate-success-log-conflict` | L3 | no automatic recovery |
| `duplicate-in-state` | L3 | no automatic recovery |
| `legacy-idempotency-unverifiable` | L3 | no automatic idempotent retry |
| malformed state/log or checker execution error | FATAL | stop and preserve evidence |

## Quarantine Evidence

`recovery evidence --out <dir>` writes:

- `quarantine-manifest.json`
- `recovery-request.json`
- `hard-rollback.json`

These files are evidence only. `recovery-request.json` has `status=DRAFT_NOT_EXECUTABLE`, `requiresTrustedBaseline=true`, and `requiresVerifiedHumanReceipt=true`.

## Crash Windows

| window | diagnosis |
| --- | --- |
| before pending write | no recovery record exists |
| after pending write before state replace | `stale-pending-before-replace` |
| after state replace before success log | `pending-transition-recovery-required` |
| after success log before pending cleanup | `completed-pending-cleanup-available` |
| contradictory pending/current state/log | `pending-state-ambiguous` |

## Post-provenance Future

After Trust Origin and verified human receipt infrastructure exist, a separate recovery apply contract may allow receipt-backed RECOVERY transitions. That future work must still preserve:

- reconciliation before any apply decision
- full request/pre/post/contract hash binding
- no wildcard legacy gap override
- no high-risk transition without verified receipt
- no automatic execution readiness by default

This document does not authorize that future apply path.
