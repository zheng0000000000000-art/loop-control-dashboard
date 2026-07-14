# 06C-1 검증 - StateTransition v2 Core

## 기준

- DI: `DI-06C1-STATE-TRANSITION-V2-CORE`
- 기준 commit: `5ad6ed8 feat(handoff-integrity): preserve and validate v2 success bindings`
- 시작 조건: `git merge-base --is-ancestor 5ad6ed8 HEAD` 통과, 시작 시 working tree clean
- 비목적: trust-origin, 06H recovery, replay 활성화, production `WORKSTATE.json` 직접 수정, production transition log 직접 수정, push

## 변경 파일

- `server/StateApplierCli.cs`
- `docs/verification/06c1-state-transition-v2-core.md`

## 참조한 스킬

- `skills/common/directive-writing.md`
- `skills/common/executor-launch.md`
- `skills/common/hs-gate.md`
- `skills/common/powershell-encoding.md`
- `skills/common/root-cause-diagnosis.md`
- `skills/common/verification.md`

## Request 및 Contract Schema

v2 apply envelope는 다음 의미를 가진다.

- `schemaVersion=2`
- `transitionId`
- `transitionKind`
- `effectiveAt`
- `expectedPreStateSha256`
- `expectedPostStateSha256`
- `requestSha256`
- `transitionContractSha256`
- `requestPath`
- `candidatePath`
- `claimedActor`
- `humanReceipt`

`transitionContractSha256`는 다음 고정 순서 canonical JSON의 SHA-256이다.

`transitionId`, `transitionKind`, `requestSha256`, `preStateSha256`, `postStateSha256`, `effectiveAt`

## 처리 순서

1. envelope 정적 검증
2. pending journal 선검사
3. 05H `HandoffIntegrityChecker` in-process reconciliation
4. 기존 transition ID 조회
5. 미적용 ID에만 pre-state hash 검증
6. request/candidate/contract hash 재계산
7. pending journal atomic write
8. temp state write + flush
9. same-directory `File.Replace`
10. v2 success log append + flush
11. post-apply reconciliation
12. pending journal 삭제

## Idempotency Matrix

| case | 결과 |
| --- | --- |
| 기존 success 없음 | 신규 전이 경로 |
| 고유 v2 success + binding 동일 | `status=idempotent`, `stateWritten=false`, `successLogAppended=false` |
| 고유 v2 success + binding 다름 | `transition-id-collision` |
| v1 success만 존재 | `legacy-idempotency-unverifiable` |
| 동일 binding v2 success 중복 | warning 허용, 동일 요청은 idempotent |
| 서로 다른 v2 success 중복 | 05H reconciliation에서 `duplicate-success-log-conflict`로 차단 |

## Atomic Apply Protocol

- candidate bytes는 apply 시점에 request와 pre-state에서 재계산한다.
- candidate 파일은 evidence로만 사용하고 canonical state write source로 쓰지 않는다.
- temp state 파일은 canonical state와 같은 디렉터리에 쓴다.
- `File.Replace(temp, WORKSTATE.json, backup)`로 교체한다.
- success log는 state replace 이후에만 append한다.
- log append 이후 post-apply reconciliation을 실행한다.

## Pending Journal Protocol

- 내부 경로: `.state-applier/pending/<transitionId>.json`
- success log에 pending을 기록하지 않는다.
- `pending + pre-state + success 없음`: stale pending cleanup 가능
- `pending + post-state + 동일 v2 success 있음`: completed pending cleanup 가능
- `pending + post-state + success 없음`: `pending-transition-recovery-required`
- 그 외 모순: `pending-state-ambiguous`

## Crash Windows

| window | 분류 |
| --- | --- |
| pending write 전 실패 | state/log unchanged, 재시도 가능 |
| pending write 후 replace 전 실패 | state==pre, success 없음, stale pending cleanup 가능 |
| replace 후 log append 전 실패 | state==post, success 없음, pending 존재, recovery required |
| log append 후 pending cleanup 전 실패 | state==post, success 있음, 다음 실행에서 cleanup 후 idempotent |
| post-apply reconciliation 실패 | `post-apply-reconciliation-failed`, 성공으로 숨기지 않음 |

## High-risk Fail-closed

`PHASE_CHANGE`, `RECOVERY`, `REPLAY`는 trusted human receipt infrastructure가 없으므로 신규 전이에서 항상 `trusted-human-receipt-required`로 거부한다. `claimedActor`는 관찰 정보이며 receipt로 간주하지 않는다.

## Fixture 결과

`dotnet run --project server -c Release --no-build -- state-transition --self-test`

- exitCode: 0
- selfTest: `state-transition-v2-core`
- verdict: `PASS`
- casesRun: 19

검증된 case:

- normal-new-transition
- exact-idempotent-retry
- same-id-different-request
- same-id-different-effectiveAt
- same-id-different-kind
- v1-idempotency-rejected
- pre-state-mismatch
- reconciliation-fail
- duplicate-v2-same-binding
- conflicting-v2-success
- candidate-toctou
- temp-write-failure
- atomic-replace-failure
- after-replace-before-log
- after-log-before-cleanup
- phase-change-no-receipt
- recovery-no-receipt
- replay-no-receipt
- contract-hash-mismatch

## 검증 명령

| 항목 | 명령 | 결과 |
| --- | --- | --- |
| Release build | `dotnet build server -c Release` | PASS, exit 0, warning 0 error 0 |
| 06C-1 fixture suite | `dotnet run --project server -c Release --no-build -- state-transition --self-test` | PASS, exit 0, 19 cases |
| 05H fixture regression | `handoff-integrity --workstate ... --applier-log ...` fixture-a/b/c/d/e/f/malformed/v2/v2-conflict | PASS, all expected exit codes matched |
| 05H self-test | `dotnet run --project server -c Release --no-build -- handoff-integrity --self-test` | PASS, exit 0 |
| doc-integrity | `dotnet run --project server -c Release --no-build -- doc-integrity` | PASS, exit 0 |
| callsite gate | `dotnet run --project server -c Release --no-build -- state-transition-callsite-check` | PASS, `legacyCallsiteCount=0` |
| git diff check | `git diff --check` | PASS, exit 0 |
| dev-pack | `dotnet run --project server -c Release --no-build -- measure dev-pack` | PASS, `violationCount=0`, `overallStatus=completed` |

## 게이트 기록

`{"gate":"dev-pack","violations":0,"attempt":1}`

## Callsite Inventory

- `ACTIVE_DIRECT_WRITER=0` for legacy single-shot state-transition callsites
- `state-transition-callsite-check` result: `legacyCallsiteCount=0`, `historicalReferenceCount=4`, `scannedActiveFiles=659`
- Historical references are verification/review/directive artifacts and not active callers.

## Dashboard 파생 데이터 처리

`measure dev-pack` 실행으로 `dashboard/data/dev-pack/measurement.json`, `run-log.json`, `workflow-state.json`이 측정 시각, `measure.completed`, `lastUpdated`만 갱신했다. 실패한 중간 측정에서는 `patch-proposal.json`이 `functionsWithoutComment` rollback proposal로 갱신됐으나 record 주석 보강 후 `violationCount=0`으로 회복됐다. dashboard 파생 데이터는 checkpoint commit에 포함하지 않는다.

## At-rest Blocker 분리

- `AT_REST_RECONCILIATION_PASS=false`
- known failure: `DI0004-BLOCKED-CODEX state-transition-not-logged`
- classification: `PRE_EXISTING_STATE_HISTORY_GAP`
- `INTRODUCED_BY_06C1=false`

06C-1 fixture는 임시 repo state/log/pending 경로만 사용했다. production `WORKSTATE.json`과 `WORKSTATE.applier-log.jsonl`은 수정하지 않았다.

## 상태 모델

- `06C1_IMPLEMENTATION_COMPLETE=true`
- `06C1_REQUEST_CONTRACT_VERIFIED=true`
- `06C1_IDEMPOTENCY_VERIFIED=true`
- `06C1_V1_IDEMPOTENCY_REJECTED=true`
- `06C1_COLLISION_REJECTED=true`
- `06C1_PRESTATE_ORDER_VERIFIED=true`
- `06C1_CANDIDATE_TOCTOU_BLOCKED=true`
- `06C1_ATOMIC_REPLACE_VERIFIED=true`
- `06C1_PENDING_PROTOCOL_VERIFIED=true`
- `06C1_HIGH_RISK_FAIL_CLOSED=true`
- `06C1_CALLSITE_GATE_PASS=true`
- `05H_REGRESSION=false`
- `DEV_PACK_VIOLATION_COUNT=0`
- `DEV_PACK_OVERALL_STATUS=completed`
- `AT_REST_RECONCILIATION_PASS=false`
- `STATE_HISTORY_CONSISTENT=false`
- `TRUSTED_BASELINE=false`
- `NORMAL_TRANSITION_READY=false`
- `HIGH_RISK_TRANSITION_READY=false`
- `TRUST_ORIGIN_READY=false`
- `WP_STATE_INTEGRITY_LAND_READY=false`
- `CANONICAL_MERGE_READY=false`
- `PUSH_READY=false`

## 직접 경로 사용 사유

DI-06C1 지시서가 StateTransition/StateApplier 구현, CLI routing, 06C-1 verification 문서, callsite 검사기 사용을 직접 범위로 허용했다. 기존 구현 위치가 `server/StateApplierCli.cs`였으므로 중복 구현을 만들지 않고 해당 파일을 확장했다.

## 잔여 위험

- at-rest state history gap은 여전히 남아 있다.
- high-risk transition은 receipt infrastructure가 없어 의도적으로 사용 불가다.
- recovery/replay 자동화는 06H 또는 후속 trust-origin 작업 전까지 활성화하지 않는다.
- `state-transition --self-test`는 내부 fixture suite를 실행하지만 외부 reviewer가 별도로 read-only 검수해야 최종 PASS다.

## Commit SHA

작성 시점에는 아직 commit 전이다. checkpoint commit 후 SHA를 갱신한다.
