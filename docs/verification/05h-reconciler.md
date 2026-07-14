# 05H 검증 - handoff-integrity reconciliation follow-up

## DI 유형 선언

- 선언한 유형: `harness`
- 목적: 05H reconciliation checker 보강을 고정한다.
- 비목적: 06C/06H 구현, WORKSTATE 수정, transition log 수정, trust-origin 생성, `TRUSTED_BASELINE=true` 선언.

## 주체

- 작성/수정: Codex
- 경로: 직접 허용된 docs/server harness 경로 수정
- 판정: 이 문서는 자체 최종 PASS 판정이 아니다. 별도 reviewer 또는 별도 read-only Codex 검수 세션이 최종 검수한다.

## 사용한 하네스

| 하네스 | 명령 | 기대 exit | 실제 exit | 결과 |
| --- | --- | --- | --- | --- |
| build Release | `dotnet build server -c Release` | 0 | 0 | PASS, warning 0 error 0 |
| handoff-integrity fixture-a | `dotnet run --project server -c Release --no-build -- handoff-integrity --workstate docs/qa/fixtures/reconciliation/fixture-a/workstate.json --applier-log docs/qa/fixtures/reconciliation/fixture-a/applier-log.jsonl --format json` | 1 | 1 | PASS |
| handoff-integrity fixture-b | `... fixture-b ...` | 1 | 1 | PASS |
| handoff-integrity fixture-c | `... fixture-c ...` | 0 | 0 | PASS |
| handoff-integrity fixture-d | `... fixture-d ...` | 1 | 1 | PASS |
| handoff-integrity fixture-e | `... fixture-e ...` | 1 | 1 | PASS |
| handoff-integrity fixture-f | `... fixture-f ...` | 1 | 1 | PASS |
| handoff-integrity fixture-malformed | `... fixture-malformed ...` | 2 | 2 | PASS |
| handoff-integrity fixture-v2 | `... fixture-v2 ...` | 0 | 0 | PASS, `logSchemaVersions.v2=2`, `lookupSuccess.V2-001` hash 4개 보존 |
| handoff-integrity fixture-v2-conflict | `... fixture-v2-conflict ...` | 1 | 1 | PASS, `duplicate-success-log-conflict`, `lookupSuccess={}` |
| handoff-integrity self-test | `dotnet run --project server -c Release --no-build -- handoff-integrity --self-test` | 0 | 0 | PASS, `casesRun=6` |
| doc-integrity | `dotnet run --project server -c Release --no-build -- doc-integrity` | 0 | 0 | PASS, `verdict=INTACT` |
| measure dev-pack | `dotnet run --project server -c Release --no-build -- measure dev-pack` | 0 | 0 | PASS, `violationCount=0`, `overallStatus=completed` |
| git diff --check | `git diff --check` | 0 | 0 | PASS |
| at-rest current repo | `dotnet run --project server -c Release --no-build -- handoff-integrity --workstate docs/handoff/WORKSTATE.json --applier-log docs/handoff/WORKSTATE.applier-log.jsonl --format json` | 1 | 1 | KNOWN BLOCKER, `DI0004-BLOCKED-CODEX state-transition-not-logged` |

## 유형별 필수 검증

| DI 유형 | 필수 검증 | 수행 결과 |
| --- | --- | --- |
| harness | positive, negative, deterministic fixture evidence | fixture-a/b/d/e/f/malformed negative, fixture-c/v2 positive, fixture-v2-conflict negative, self-test deterministic path 실행 |

## 참조한 스킬

- `skills/common/directive-writing.md`
- `skills/common/executor-launch.md`
- `skills/common/hs-gate.md`
- `skills/common/powershell-encoding.md`
- `skills/common/root-cause-diagnosis.md`
- `skills/common/verification.md`

## 변경 경로

- `server/Harness/HandoffIntegrityChecker.cs`
- `server/Harness/HandoffIntegrityCli.cs`
- `docs/qa/fixtures/reconciliation/fixture-v2/workstate.json`
- `docs/qa/fixtures/reconciliation/fixture-v2/applier-log.jsonl`
- `docs/qa/fixtures/reconciliation/fixture-v2-conflict/workstate.json`
- `docs/qa/fixtures/reconciliation/fixture-v2-conflict/applier-log.jsonl`
- `docs/verification/05h-reconciler.md`

## 변경 내용

| 파일 | 종류 | 요약 |
| --- | --- | --- |
| `server/Harness/HandoffIntegrityChecker.cs` | 수정 | v2 success lookup에서 `requestSha256`, `preStateSha256`, `postStateSha256`, `transitionContractSha256`를 원본 로그 값 그대로 보존 |
| `server/Harness/HandoffIntegrityChecker.cs` | 수정 | 같은 `transitionId`에 서로 다른 v2 success binding이 둘 이상 있으면 단일 lookup으로 축약하지 않고 `duplicate-success-log-conflict` 실패 처리 |
| `server/Harness/HandoffIntegrityChecker.cs` | 수정 | 같은 `transitionId`와 같은 전체 v2 binding의 중복 success는 conflict로 오판하지 않고 `duplicate-success-in-log` warning 유지 |
| `server/Harness/HandoffIntegrityCli.cs` | 수정 | reconciliation metrics에 관찰된 `logSchemaVersions`와 `lookupSuccess` 출력 |
| `docs/qa/fixtures/reconciliation/fixture-v2/*` | 신규 | v2 success binding positive fixture 추가. 동일 binding 중복을 warning으로 남기고 lookup hash 4개 보존 검증 |
| `docs/qa/fixtures/reconciliation/fixture-v2-conflict/*` | 신규 | 같은 transitionId의 conflicting v2 binding을 단일 lookup으로 축약하지 않는 negative fixture 추가 |
| `docs/verification/05h-reconciler.md` | 수정 | 현재 실측 기준으로 verification evidence 갱신 |

## v2 lookup 실측

`fixture-v2` 결과:

- exitCode: 0
- verdict: `PASS`
- `successfulLogEntryCount`: 2
- `successfulLogIdCount`: 1
- `duplicateSuccessLogCount`: 1
- `logSchemaVersions`: `{ "v2": 2 }`
- `lookupSuccess.V2-001.requestSha256`: `1111111111111111111111111111111111111111111111111111111111111111`
- `lookupSuccess.V2-001.preStateSha256`: `2222222222222222222222222222222222222222222222222222222222222222`
- `lookupSuccess.V2-001.postStateSha256`: `3333333333333333333333333333333333333333333333333333333333333333`
- `lookupSuccess.V2-001.transitionContractSha256`: `4444444444444444444444444444444444444444444444444444444444444444`
- warning: `duplicate-success-in-log`

`fixture-v2-conflict` 결과:

- exitCode: 1
- verdict: `FAIL`
- failure: `V2-CONFLICT duplicate-success-log-conflict`
- `successfulLogEntryCount`: 2
- `successfulLogIdCount`: 1
- `duplicateSuccessLogCount`: 1
- `logSchemaVersions`: `{ "v2": 2 }`
- `lookupSuccess`: `{}`

## 반증 시험

| # | 시험 | 기대 exit | 실제 exit | 판정 |
| --- | --- | --- | --- | --- |
| 1 | fixture-a: 성공 로그에는 있으나 state에 없는 transition | 1 | 1 | PASS |
| 2 | fixture-b: state 중복 transition | 1 | 1 | PASS |
| 3 | fixture-d: blocked 상태의 blockers 누락 | 1 | 1 | PASS |
| 4 | fixture-e: completed 상태의 active blockers | 1 | 1 | PASS |
| 5 | fixture-f: state에는 있으나 성공 로그가 없는 transition | 1 | 1 | PASS |
| 6 | fixture-malformed: malformed workstate | 2 | 2 | PASS |
| 7 | fixture-v2-conflict: 같은 ID의 서로 다른 v2 binding | 1 | 1 | PASS |

## 실패 분류와 실패 여부

- 실패 분류: `expected_rejection`
- 실패 사례 ID: 신규 실패 사례 없음
- 분류 근거: fixture 실패들은 모두 의도한 반증 입력이다.

## 게이트 기록

`{"gate":"dev-pack","violations":0,"attempt":1}`

## Dashboard 파생 데이터 처리

`measure dev-pack` 실행으로 `dashboard/data/dev-pack/measurement.json`, `run-log.json`, `workflow-state.json`이 갱신됐다. diff는 측정 시각, `measure.completed` 누적 로그, `lastUpdated` 기록으로 분류했다. 실패한 중간 측정에서는 `dashboard/data/dev-pack/patch-proposal.json`도 `functionsWithoutComment` 롤백 제안으로 갱신됐으나, 원인은 05H helper 삽입 중 함수 직전 주석 위치가 밀린 것이며 수정 후 `violationCount=0`으로 회복됐다.

위 dashboard 변경은 05H 코드 계약 변경에 따른 기준 snapshot 변경이 아니므로 checkpoint commit에 포함하지 않는다.

## DLL 잠금 사건

- 원인: `dotnet build server -c Release`와 `dotnet run --project server -c Release -- ...`가 Release 출력물을 동시에 접근했다.
- 증상: `CS2012`와 `VBCSCompiler` DLL 잠금.
- 해결: `dotnet build-server shutdown` 후 순차 실행.
- 분류: `TEST_ORCHESTRATION_CONTENTION`
- PRODUCT_DEFECT: `false`

## At-rest blocker

- `AT_REST_RECONCILIATION_PASS=false`
- `exitCode=1`
- `FAILURE=DI0004-BLOCKED-CODEX state-transition-not-logged`
- `INTRODUCED_BY_05H=false`
- 분류: `PRE_EXISTING_TRUST_BASELINE_GAP`

05H fixture 검증 성공은 현재 저장소 전체 trusted baseline 확립과 다르다.

## 상태 모델

- `BUILD_RELEASE_PASS=true`
- `ALL_RECONCILIATION_FIXTURES_PASS=true`
- `V2_LOOKUP_BINDING_VERIFIED=true`
- `V2_CONFLICT_REJECTED=true`
- `SELF_TEST_PASS=true`
- `DOC_INTEGRITY_PASS=true`
- `DEV_PACK_VIOLATION_COUNT=0`
- `DEV_PACK_OVERALL_STATUS=completed`
- `GIT_DIFF_CHECK_PASS=true`
- `AT_REST_RECONCILIATION_PASS=false`
- `STATE_HISTORY_CONSISTENT=false`
- `TRUSTED_BASELINE=false`
- `WP_STATE_INTEGRITY_LAND_READY=false`
- `CANONICAL_MERGE_READY=false`
- `PUSH_READY=false`

## 직접 경로 사용 사유

DI-05H-FINALIZE-CHECKPOINT allowlist가 `server/Harness/HandoffIntegrityChecker.cs`, `server/Harness/HandoffIntegrityCli.cs`, `docs/qa/fixtures/reconciliation/fixture-v2/**`, `docs/qa/fixtures/reconciliation/fixture-v2-conflict/**`, `docs/verification/05h-reconciler.md` 직접 수정을 허용했다.

## 잔여 위험

- at-rest `handoff-integrity`는 현재 저장소 상태에서 exit 1이다. 원인은 `DI0004-BLOCKED-CODEX`가 state에는 있으나 성공 로그에는 없는 기존 trust-origin 문제다.
- 06C-1/06C-2와 trust-origin 처리는 별도 승인된 세션 영역이다.
- 검증 명령이 tracked dashboard runtime output을 갱신하는 구조는 후속 `WP-VERIFICATION-OUTPUT-HYGIENE` 후보로 남긴다.

## 완료 판정

`PASS | FAIL | BLOCKED` 중 최종 판정은 별도 검수자가 수행한다.
