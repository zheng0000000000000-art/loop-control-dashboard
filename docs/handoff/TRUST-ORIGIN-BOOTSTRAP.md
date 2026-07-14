# Trust Origin Bootstrap

## Scope

`BOOTSTRAP_TRUST_ORIGIN` is infrastructure for starting trust epoch 1 from a fixed baseline commit. It is not a state transition, not a human approval receipt, and not proof that legacy history was exactly replay verified.

The production repository has not declared a Trust Origin in this checkpoint.

## Commands

- `trust-origin inspect`: read-only eligibility and current evidence summary.
- `trust-origin evidence --out <file>`: writes a draft machine-readable evidence file. It does not run the gates and does not mark them PASS.
- `trust-origin declare --evidence <file>`: validates evidence and writes a Trust Origin record candidate only. It does not edit `WORKSTATE.json`, the applier log, git commits, tags, or readiness flags.
- `trust-origin verify`: validates a committed Trust Origin record, Git ancestry, baseline snapshots, immutable prefixes, and post-origin delta reconciliation.
- `trust-origin --self-test`: runs isolated temporary Git fixture coverage.

## Declaration Modes

`VERIFIED_CONSISTENT` means baseline reconciliation exits 0 and the declared legacy failure set is empty.

`DECLARED_LEGACY_GAP` means baseline reconciliation exits 1 and the actual failure set exactly equals the declared evidence set. It is not a wildcard override and does not convert legacy history into replay-verified history.

Declaration is rejected for malformed state/log input, conflicting success bindings, duplicate state IDs, pending ambiguity, direct-writer gates, high-risk bypasses, automatic launcher enablement, baseline snapshot mismatch, dirty worktrees, redeclaration, and self-referencing baseline records.

## Record Schema

Trust Origin records use `schemaVersion: 2` and live under `docs/handoff/trust-origins/`.

Required meanings:

- `baselineCommit`: clean implementation snapshot commit. It must not be the declaration commit.
- `baselineWorkstateSha256`: SHA-256 of `docs/handoff/WORKSTATE.json` at `baselineCommit`.
- `baselineApplierLogSha256`: SHA-256 of `docs/handoff/WORKSTATE.applier-log.jsonl` at `baselineCommit`.
- `declaredLegacyFailures`: exact canonical failure set for `DECLARED_LEGACY_GAP`.
- `declaredLegacyFailureSetSha256`: canonical hash over code, subject, and detail hash.
- `declaredLegacyWarnings`: baseline warning set for audit context.
- `baselineReconciliationReportSha256`: canonical hash over baseline failures and warnings.
- `integrationGateEvidenceSha256`: canonical hash of the integration gate evidence accepted by `declare`.
- `declarationStatus`: `HUMAN_DECLARED_NOT_CRYPTOGRAPHICALLY_VERIFIED`.
- `declaredBy.provenance`: `CLAIMED_NOT_VERIFIED`.

Forbidden meanings:

- `VERIFIED_HUMAN`
- `CRYPTOGRAPHICALLY_VERIFIED`
- `SERVER_APPROVED`
- `TRUSTED_RECEIPT`
- `repositoryCommit` as a self-referential declaration commit field

## Commit Split

Trust Origin uses two commits.

Commit A is the baseline commit. It contains the implementation, fixture, docs, and the baseline state/log bytes. It does not contain the Trust Origin record.

Commit B is the declaration commit. It adds only the Trust Origin record. The record points to Commit A through `baselineCommit`. Commit B is discovered by Git history as the first commit that added the record path.

An uncommitted record is inactive.

## Trust-aware Reconciliation

After a valid committed Trust Origin exists, verification does not reinterpret the legacy gap as replay verified. It checks:

1. The record is tracked and in current history.
2. `baselineCommit` is an ancestor of current `HEAD`.
3. The declaration commit is an ancestor of current `HEAD`.
4. Baseline state/log hashes match Git snapshots from Commit A.
5. Current `appliedTransitions` preserve the baseline prefix exactly.
6. Current applier log preserves the baseline log prefix exactly.
7. Only post-origin state/log suffixes are reconciled strictly.
8. Post-origin log suffixes must use complete v2 success bindings. If a post-origin state entry carries binding fields, the verifier compares `transitionId`, `transitionKind`, `requestSha256`, `preStateSha256`, `postStateSha256`, `effectiveAt`, and `transitionContractSha256` against the log binding.

`verify` does not trust that a record was created by the CLI. It independently validates schema version, trust epoch, declaration type, declaration status, claimed actor provenance, failure-set hash, reconciliation report hash, baseline Git snapshot, high-risk fail-closed state, automatic launcher disabled state, and direct-writer gate.

`declare` requires integration gate evidence bound to the current baseline commit and current state/log hashes. The evidence must report Release build PASS, reconciliation fixture PASS, 06C-1 PASS with 19 cases, 06C-2 PASS with 24 cases, 06H PASS with 8 cases, doc-integrity PASS, dev-pack violation count 0 with completed status, and legacy callsite count 0.

Valid delta reconciliation reports `reconciliationMode = TRUST_ORIGIN_DELTA`.

## Readiness

Readiness is derived by verification, not trusted from record booleans alone.

`TRUSTED_BASELINE=true` requires a valid committed Trust Origin, verified baseline snapshot hashes, Git ancestry, immutable baseline prefixes, strict delta reconciliation, build pass, and direct-writer gate pass.

Even after Trust Origin activation:

- `VERIFIED_HUMAN_APPROVAL_READY=false`
- `RECOVERY_APPLY_READY=false`
- `PHASE_CHANGE_READY=false`
- `REPLAY_READY=false`
- `AUTOMATED_EXECUTION_READY=false`

High-risk transitions and automatic execution stay fail-closed.
