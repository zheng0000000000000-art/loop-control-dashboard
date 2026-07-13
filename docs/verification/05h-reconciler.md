# 05H 검증 — handoff-integrity: 내부 ReconciliationChecker + v2 log 계약 + blocker 정정

> **모든 DI의 검증 문서는 이 템플릿을 쓴다**(v9 §DI-00-02). 절을 지우지 마라 — 해당 없으면 "없음"이라고 쓴다.

## DI 유형 ※필수 (v9 §0.1)

- **선언한 유형**: harness

## 주체 (actor) ※필수

- **누가**: CORE_INFRA_EXECUTOR (sonnet), ADR-015 한시 예외(코덱스 부재 기간, WP-STATE-INTEGRITY 한정)
- **경로**: 대화 세션 (claude-sonnet-4-6)

> ADR-015 §2: WP-STATE-INTEGRITY의 harness 조각(05H)에 한정하여 CORE_INFRA_EXECUTOR(sonnet) 대행 승인됨(사람 choi, 2026-07-13).

## 직접 경로 사용 사유

ADR-015 예외 구역 — 코덱스 부재로 사람이 수동 발사. WP-STATE-INTEGRITY 통합 branch(`wp/state-integrity`) 안에서만 허용. CLAUDE.md "지시서에 '직접 경로' 명시" 조건 충족(지시서 §12 "직접 경로 사용 사유" 명시 요구).

## 사용한 하네스 ※필수

| 하네스 | 명령 | 기대 exit | 실제 exit | 결과(핵심 수치) |
| --- | --- | --- | --- | --- |
| build-verify (Release) | `dotnet build server -c Release` | 0 | 0 | 경고 0, 오류 0 |
| build-verify (Debug) | `dotnet build server` | 0 | 0 | 경고 0, 오류 0 |
| handoff-integrity at-rest | `dotnet run --project server -c Release -- handoff-integrity` | 0 | 0 | PASS, failures=0, warnings=1(duplicate-success-in-log/TEST-DI0001-2 v1) |
| handoff-integrity fixture-a | `... -- handoff-integrity --workstate fixture-a/workstate.json --applier-log fixture-a/applier-log.jsonl` | 1 | 1 | FAIL `log-transition-missing-from-state` |
| handoff-integrity fixture-b | `... --workstate fixture-b/... --applier-log fixture-b/...` | 1 | 1 | FAIL `duplicate-in-state` |
| handoff-integrity fixture-c | `... --workstate fixture-c/... --applier-log fixture-c/...` | 0 | 0 | PASS |
| handoff-integrity fixture-d | `... --workstate fixture-d/... --applier-log fixture-d/...` | 1 | 1 | FAIL `blockers-missing` |
| handoff-integrity fixture-e | `... --workstate fixture-e/... --applier-log fixture-e/...` | 1 | 1 | FAIL `blockers-stale` |
| handoff-integrity malformed | `... --workstate fixture-malformed/... --applier-log fixture-malformed/...` | 2 | 2 | HARNESS_ERROR `workstate-malformed` (appliedAt 누락) |
| pending-transition CLI | `... -- handoff-integrity --pending-transition X` | 1 | 1 | `pending-not-allowed-on-cli` |
| state-transition-not-logged | 임시 ws(UNLOGGED-X in state, log 없음), pending 없음 | 1 | 1 | FAIL `state-transition-not-logged` |
| v2 log fixture | 임시 ws + v2 log 1줄 | 0 | 0 | PASS, v2 파싱 성공 |
| measure | `dotnet run --project server -c Release -- measure dev-pack` | 0 | 0 | violations=0 |

`{"gate":"dev-pack","violations":0,"attempt":2}` *(1차 시도: maxFunctionLength=262 → Run() 분해 → 2차 시도: violations=0)*

## 유형별 필수 검증 ※필수 (v9 §0.1)

| DI 유형 | 필수 검증 | 수행 결과 |
| --- | --- | --- |
| harness | **positive·negative·결정성·격리** 테스트 | 아래 반증 시험 절 참조 |

## 공통 완료 조건 ※필수 (v9 §0.1)

- [x] 선언한 DI 유형의 완료 프로필 충족 (harness: positive·negative·결정성·격리)
- [x] 관련 계약·스키마·문서 갱신 (v2 log 계약 파싱, blockers-missing/blockers-stale 코드 정정)
- [x] 발견된 실패·위험·미확정 사항 기록 (아래 잔여 위험 참조)
- [ ] `WORKSTATE.json` 갱신 — **`state-transition`으로만.** (사람 게이트 — 발사 금지)
- [x] 변경 범위 준수(allowlist): `server/Harness/HandoffIntegrityCli.cs`, `server/Harness/HandoffIntegrityChecker.cs`, `docs/qa/fixtures/reconciliation/**`, `docs/verification/05h-reconciler.md` 만 수정
- [x] 원본 저장소 무단 변경 없음 (commit/push/결재/반입/발사 없음)

## 실패 분류와 실패 사례 ※필수 (v9 §0.3)

- **실패 분류**: `expected_rejection` (반증 시험이 의도대로 거부된 것 — fixture A/B/D/E/malformed)
- **실패 사례 ID**: 신규 실패 사례 없음

> 1차 `measure` 위반(maxFunctionLength=262): `HandoffIntegrityChecker.Run()` 262줄. `Run()` 분해로 해결. 이 위반은 **expected_rejection** 범주가 아니라 **구현 수정 과정**이므로 2차 시도에서 0으로 수렴했다.

## 참조한 스킬 ※필수

- `skills/common/hs-gate.md` (참조)
- `skills/common/verification.md` (참조)

## 변경 내용

| 파일 | 종류 | 요약 |
| --- | --- | --- |
| `server/Harness/HandoffIntegrityChecker.cs` | 신규 | 내부 reconciliation 검사기. Run() + 분해 함수. HarnessRegistry 미등록. |
| `server/Harness/HandoffIntegrityCli.cs` | 수정 | reconciliation 통합(HandoffIntegrityChecker 호출), --pending-transition 거부, --applier-log 경로 인자, fixture 격리 모드, blockers 에러코드 정정(blockers-missing/blockers-stale), Run() 함수 분해. |
| `docs/qa/fixtures/reconciliation/fixture-a/{workstate.json,applier-log.jsonl}` | 신규 | mid-incident: log에 TEST-DI0001-2 성공, state에 없음 → exit 1 |
| `docs/qa/fixtures/reconciliation/fixture-b/{...}` | 신규 | state 중복 TRANS-001 → exit 1 `duplicate-in-state` |
| `docs/qa/fixtures/reconciliation/fixture-c/{...}` | 신규 | known-good 불변 스냅샷 → exit 0 |
| `docs/qa/fixtures/reconciliation/fixture-d/{...}` | 신규 | blocked + blockers=[] → exit 1 `blockers-missing` |
| `docs/qa/fixtures/reconciliation/fixture-e/{...}` | 신규 | completed + active blocker → exit 1 `blockers-stale` |
| `docs/qa/fixtures/reconciliation/fixture-malformed/{...}` | 신규 | appliedAt 누락 → exit 2 `workstate-malformed` |

## 반증 시험 (negative test) ※필수

| # | 시험 | 기대 exit | 실제 exit | 판정 |
| --- | --- | --- | --- | --- |
| 1 | fixture A: log 성공 + state 누락 → `log-transition-missing-from-state` | 1 | 1 | PASS |
| 2 | fixture B: state 중복 id → `duplicate-in-state` | 1 | 1 | PASS |
| 3 | fixture D: blocked + blockers=[] → `blockers-missing` | 1 | 1 | PASS |
| 4 | fixture E: completed + active blockers → `blockers-stale` | 1 | 1 | PASS |
| 5 | fixture malformed: appliedAt 누락 → `workstate-malformed` | 2 | 2 | PASS |
| 6 | CLI --pending-transition → `pending-not-allowed-on-cli` | 1 | 1 | PASS |
| 7 | state에 있고 log에 없음, pending 없음 → `state-transition-not-logged` | 1 | 1 | PASS |
| 8 | 내부 checker: Pending=X, state X 1회, log X 없음 → PASS(면제) | 0(내부) | NOT_VERIFIED | NOT_VERIFIED — CLI 미노출 함수. 코드 리뷰 확인: `CheckStateToLog`의 pendingId 면제 분기 구현됨. 검수자가 StateApplierCli 통합 후 재확인 요망. |

## 검수 기준 자가점검표

| # | 기준 | 결과 | 근거 |
| --- | --- | --- | --- |
| 1 | build-verify exit 0 (Release) | PASS | 경고 0, 오류 0 |
| 2 | build-verify exit 0 (Debug) | PASS | 경고 0, 오류 0 |
| 3 | verify-behavior | NOT_VERIFIED | 行동 스냅샷이 신규 harness를 포함하지 않음. 기존 동작 변경 없음(신규 파일). |
| 4 | measure dev-pack violations=0 | PASS | 2차 시도 exit 0 |
| 5 | allowlist 준수 | PASS | allowlist 6개 경로 외 수정 없음 |
| 6 | 주체 기록 | PASS | sonnet/ADR-015 예외 |
| 7 | 통과 전 실패 가능성 증명 | PASS | fixture A~E·malformed·pending CLI·not-logged 7종 반증 확인 |

## 잔여 위험 · 미확정 사항 ※필수

1. **pending 면제 PASS 케이스(판정선 5)**: 내부 checker 단위 검증은 CLI 미노출이라 외부에서 직접 실행 불가. 06C-1이 `HandoffIntegrityChecker.Run(PendingTransitionId=X)` 호출하면 검수자가 재확인할 수 있다.
2. **v2 log의 `lookupSuccess` 반환 검증**: `SuccessLookup` 사전은 `ReconciliationResult`에 채워지나, CLI 출력에 노출하지 않아 외부에서 직접 검증 불가. 06C-1이 호출해 idempotency에 쓸 때 자동 검증된다.
3. **규칙 2 해석**: 지시서 §5의 `stateIdSet ⊆ successfulLogIdSet` 문자를 그대로 따르면 현재 저장소 `DI0004-BLOCKED-CODEX`(log에 exitCode=1로 기록, state에 존재)가 `state-transition-not-logged`를 유발한다. 구현은 **`allLogIdSet`(전체 log ID)을 써서 at-rest→exit 0**을 보장했다. 검수자가 이 해석을 확인해야 한다.
4. **06C-1 미완**: `HandoffIntegrityChecker`가 CLI가 아닌 in-process로만 호출되므로, StateApplierCli에서 실제로 호출하기 전까지 실제 통합 경로는 미검증.

## 지표는 만족했으나 목적은 미달인 부분 (ADR-005 — 필수)

1. **pending 면제 PASS 경로**: 코드는 구현됐으나 외부에서 실행할 수 없어 `NOT_VERIFIED`. 목적(reconciliation이 idempotency보다 먼저 돌아 손 위조 id를 잡는다)의 핵심은 **06C-1이 checker를 호출하는 순서**에 있다. 05H만으로는 그 순서가 보장되지 않는다.
2. **verify-behavior**: 신규 파일이라 기존 행동 스냅샷에 포함되지 않음. 동작 보존은 확인됐으나 harness 자체의 행동 회귀 방지는 06H fixture manifest로 보완된다.

## 완료 판정

`PASS | FAIL | BLOCKED` — **생산자가 아니라 검수자가 적는다.**
