# 05H-R1 검증 — reconciliation 규칙 2 복원 + fixture-f 신설

## DI 유형 ※필수 (v9 §0.1)

- **선언한 유형**: harness

## 주체 (actor) ※필수
- **누가**: CORE_INFRA_EXECUTOR (sonnet, claude-sonnet-4-6) — ADR-015 예외 구역(코덱스 헤드리스 진입점 부재)
- **경로**: 대화 세션 (브랜치 `wp/state-integrity`)

## 사용한 하네스 ※필수

| 하네스 | 명령 | 기대 exit | 실제 exit | 결과(핵심 수치) |
| --- | --- | --- | --- | --- |
| build-verify (Release) | `dotnet build server -c Release` | 0 | 0 | 경고 0 오류 0 |
| build-verify (Debug) | `dotnet build server` | 0 | 0 | 경고 0 오류 0 |
| handoff-integrity fixture-f | `handoff-integrity --workstate docs/qa/fixtures/reconciliation/fixture-f/workstate.json --applier-log ...` | 1 | 1 | failures=[state-transition-not-logged] ★신규 |
| handoff-integrity fixture-a | 같은 패턴 | 1 | 1 | failures=[log-transition-missing-from-state] |
| handoff-integrity fixture-b | 같은 패턴 | 1 | 1 | failures=[duplicate-in-state] |
| handoff-integrity fixture-c | 같은 패턴 | 0 | 0 | verdict=PASS |
| handoff-integrity fixture-d | 같은 패턴 | 1 | 1 | failures=[blockers-missing] |
| handoff-integrity fixture-e | 같은 패턴 | 1 | 1 | failures=[blockers-stale] |
| handoff-integrity fixture-malformed | 같은 패턴 | 2 | 2 | harness error |
| handoff-integrity CLI --pending-transition | `handoff-integrity --pending-transition` | 1 | 1 | code=pending-not-allowed-on-cli |
| handoff-integrity at-rest | `handoff-integrity` (기본 WORKSTATE) | **1** ★ | 1 | failures=[state-transition-not-logged x1] — **설계된 참 양성** |
| measure | `dotnet run --project server -c Release -- measure dev-pack` | 0 | 0 | violationCount=0 |

## 유형별 필수 검증 ※필수 (harness)

| DI 유형 | 필수 검증 | 수행 결과 |
| --- | --- | --- |
| harness | positive 테스트 | fixture-c → exit 0 ✓ |
| harness | negative 테스트 | fixture-f → exit 1 `state-transition-not-logged` ✓ (반증 시험 F) |
| harness | 결정성 | 같은 fixture로 복수 실행 시 동일 결과 — 코드 구조상 결정론적 (NOT_VERIFIED: 반복 실행 자동화 없음) |
| harness | 격리 | `--workstate`+`--applier-log` fixture 모드로 실파일과 완전 격리 ✓ |

## 공통 완료 조건 ※필수

- [x] 선언한 DI 유형(harness)의 완료 프로필 충족
- [x] 관련 계약·스키마·문서 갱신 (주석 갱신, fixture-f 신설)
- [x] 발견된 실패·위험·미확정 사항 기록 (아래 잔여 위험)
- [ ] `WORKSTATE.json` 갱신 — **커밋/push/state-transition은 사람 게이트**. 실행자는 미수행.
- [x] 변경 범위 준수 — allowlist 내 파일만 수정 (아래 변경 내용 참조)
- [x] 원본 저장소 무단 변경 없음 — commit/push/결재/반입/발사 미수행

## 실패 분류와 실패 사례 ※필수 (v9 §0.3)

- **실패 분류**: `expected_rejection` (fixture-f의 exit 1은 반증 시험이 의도대로 거부한 것 — 설계된 참 양성)
- **실패 사례**: 신규 실패 사례 없음

## 참조한 스킬 ※필수
- `docs/handoff/queue/directive-05H-R1-reconciler-rework.md`
- `docs/handoff/queue/directive-05H-reconciler.md`
- `docs/directives/_header.md`

## 변경 내용

| 파일 | 종류 | 요약 |
| --- | --- | --- |
| `server/Harness/HandoffIntegrityChecker.cs` | 수정 | 규칙 2 복원: `CheckStateToLog` 호출 인자 `allLogIdSet` → `successfulLogIdSet`; 메서드 주석·파라미터명 동기화 |
| `docs/qa/fixtures/reconciliation/fixture-f/workstate.json` | 신규 | fixture-f: state에 SNAP-001(성공) + FAILED-BUT-APPLIED(로그 exitCode=1) |
| `docs/qa/fixtures/reconciliation/fixture-f/applier-log.jsonl` | 신규 | fixture-f 대응 log: SNAP-001 ok/0, FAILED-BUT-APPLIED exitCode=1 |
| `docs/verification/05h-r1-reconciler-rework.md` | 신규 | 이 파일 |

## 반증 시험 (negative test) ※필수

| # | 시험 | 기대 exit | 실제 exit | 판정 |
| --- | --- | --- | --- | --- |
| F | fixture-f: state에 있으나 log엔 exitCode=1로만 기록된 전이 | 1 `state-transition-not-logged` | 1 | PASS ✓ |
| — | fixture-a (log→state 방향 실패) | 1 `log-transition-missing-from-state` | 1 | PASS ✓ |
| — | fixture-b (state 중복) | 1 `duplicate-in-state` | 1 | PASS ✓ |
| — | fixture-malformed (형식 오류) | 2 | 2 | PASS ✓ |
| — | CLI `--pending-transition` 차단 | 1 | 1 | PASS ✓ |
| — | at-rest 현재 저장소 (DI0004-BLOCKED-CODEX 오염) | 1 | 1 | PASS ✓ — **설계된 참 양성** |
| — | 내부 checker Pending 면제 (Pending=X, state에 1회) | NOT_VERIFIED | — | 단위 자동화 없음; CLI 경로만 검증됨 |

## 검수 기준 자가점검표

| # | 기준 | 결과 | 근거 |
| --- | --- | --- | --- |
| 1 | `dotnet build server -c Release` → 0 | ✓ | 실행 exit 0, 경고 0 |
| 2 | fixture-f → exit 1 `state-transition-not-logged` | ✓ | 실행 결과 위 |
| 3 | fixture A→1·B→1·C→0·D→1·E→1·malformed→2 (회귀 없음) | ✓ | 전수 실행 위 |
| 4 | CLI `--pending-transition` → 1 `pending-not-allowed-on-cli` | ✓ | 실행 결과 위 |
| 5 | at-rest → 1, failures = `state-transition-not-logged` 1건 (`DI0004-BLOCKED-CODEX`) | ✓ | 실행 결과 위 |
| 6 | `measure dev-pack` → 0, violationCount=0 | ✓ | 실행 결과 위 |
| 7 | `allLogIdSet` 삭제하지 않음 (Metrics·malformed에 여전히 사용) | ✓ | 코드 검토 — `BuildSets` 내 유지됨 |
| 8 | `PendingTransitionId` 면제 로직 무손 | ✓ | 코드 검토 — `CheckStateToLog` 내 unchanged |
| 9 | `HarnessRegistry` 미등재 | ✓ | 코드 검토 — 신규 등재 없음 |
| 10 | `WORKSTATE.json`·`WORKSTATE.applier-log.jsonl` 무수정 | ✓ | 변경 파일 목록 상 없음 |

## 게이트 기록

`{"gate":"dev-pack","violations":0,"attempt":1}`

## 잔여 위험 · 미확정 사항 ※필수

1. **at-rest exit 1 (설계된 참 양성)**: `DI0004-BLOCKED-CODEX`가 state에 남아 있고 log엔 exitCode=1로만 존재. 이것은 WP-STATE-INTEGRITY 근본결함 #2의 실측 흔적. 해소 경로는 06C-2 trust-origin 부트스트랩(사람 결재). `handoff-integrity`를 호출하는 다른 게이트가 잠길 수 있음 — 잠기면 잠긴 대로 06C-2에 전달.
2. **내부 checker Pending 면제 단위 테스트 없음**: CLI로는 `--pending-transition`이 차단되어 격리 불가. `ReconciliationOptions.PendingTransitionId`를 직접 넣는 단위 픽스처가 없다 — `NOT_VERIFIED`. 06H 또는 별도 작업에서 보강 권고.
3. **결정성 반복 실행 미자동화**: 동일 fixture를 N회 돌리는 자동 루프 없음. 코드 구조상 결정론적으로 판단.

## 직접 경로 사용 사유

ADR-015 예외: 코덱스 헤드리스 진입점 부재. Sonnet이 CORE_INFRA_EXECUTOR 역할로 직접 경로 사용. 검수자 지시서에 명시된 한시 예외.

## 지표는 만족했으나 목적은 미달인 부분 (ADR-005)

없음. 규칙 2를 `successfulLogIdSet`으로 복원했고, fixture-f가 반증 시험 F를 통과한다. 우회·예외·allowlist 추가 없음. at-rest exit 1은 오염 탐지의 참 양성이며 초록으로 만들지 않았다.

## 완료 판정

`PASS | FAIL | BLOCKED` — **검수자가 적는다.**
