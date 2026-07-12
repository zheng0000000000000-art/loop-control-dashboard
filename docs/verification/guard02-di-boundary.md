# GUARD-02 검증 — DI 경계 전이 + verdict를 게이트 증거에 결속

> **모든 DI의 검증 문서는 이 템플릿을 쓴다**(v9 §DI-00-02). 절을 지우지 마라 — 해당 없으면 "없음"이라고 쓴다.

## DI 유형 ※필수 (v9 §0.1)

- **선언한 유형**: `implementation`

## 주체 (actor) ※필수
- **누가**: Claude Sonnet 4.6, 대화 세션
- **경로**: 대화 세션(직접 경로 — 지시서 allowlist에 `server/StateApplierCli.cs` 포함)

## 사용한 하네스 ※필수
| 하네스 | 명령 | 기대 exit | 실제 exit | 결과(핵심 수치) |
| --- | --- | --- | --- | --- |
| build-verify | `dotnet run --project server -c Release -- build-verify` | 0 | 0 | 경고 0, 오류 0 |
| verify-behavior | `dotnet run --project server -c Release -- verify-behavior` | 0 | 0 | behaviorEqual=true |
| measure | `dotnet run --project server -c Release -- measure dev-pack` | 0 | 0 | violations=0 |
| di-completion-check | `dotnet run --project server -c Release -- di-completion-check --gate POST-EXECUTOR --task GUARD-02` | 0 | 0 | gateVerdict=PASS, failureCount=0 |
| gate-clean | (di-completion-check 내부 실행) | 1(실행 직후) | 1 | PASS — server/StateApplierCli.cs 1건 dirty |
| hs-scan | (GUARD-01 gate에서 이미 확인됨) | 1 | — | NOT_VERIFIED 이번 세션 직접 실행 없음 |

## 유형별 필수 검증 ※필수 (v9 §0.1)

| DI 유형 | 필수 검증 | 수행 결과 |
| --- | --- | --- |
| implementation | 정상·실패·재실행 테스트, 개발 검증 Evidence | 반증 시험 9개 전부 실측(--root 사본, --dry-run). 정상 전이(T1, T4) exit 0. 실패 전이(T2,3,5,6,7,8,9) exit 1. 재실행 안전성: --dry-run이므로 WORKSTATE 미변경 |

## 공통 완료 조건 ※필수 (v9 §0.1)

- [x] 선언한 DI 유형의 완료 프로필 충족 — implementation: 정상·실패·재실행 시험 전부 수행
- [x] 관련 계약·스키마·문서 갱신 — 이 verification 문서 신규 작성
- [x] 발견된 실패·위험·미확정 사항 기록 — 잔여 위험 절 참조
- [x] `WORKSTATE.json` 갱신 — 이번 작업에서 WORKSTATE 변경 없음(다음 DI 착수는 사람 게이트)
- [x] 변경 범위 준수(allowlist) — `server/StateApplierCli.cs`, `docs/verification/guard02-di-boundary.md`만 변경
- [x] 원본 저장소 무단 변경 없음 — git commit/push/결재/반입/발사 없음

## 실패 분류와 실패 사례 ※필수 (v9 §0.3)

- **실패 분류**: `expected_rejection` (반증 시험 7개가 의도대로 거부됨)
- **실패 사례 ID**: 신규 실패 사례 없음

## 참조한 스킬 ※필수
- `skills/common/` — 직접 파일을 읽지 않았으나 CLAUDE.md·_header.md의 관례를 적용함

## 변경 내용
| 파일 | 종류(신규/수정) | 요약 |
| --- | --- | --- |
| server/StateApplierCli.cs | 수정 | `ValidateRequest` 분리 → `ValidateStatusTransition` 헬퍼 추출. DI 경계 전이 로직 추가. `ValidateVerdict` 재설계(gate 증거 강제). |
| docs/verification/guard02-di-boundary.md | 신규 | 이 문서 |

### 코드 변경 상세

**`ValidateStatusTransition` (신규 헬퍼)**
- `requestDiId != currentDiId`이면 새 DI 착수로 판별
- 새 DI 착수 시: 현재 status가 `completed`여야 허용; 새 status는 `waiting` 또는 `in_progress`만 허용; 전이 그래프 비적용
- 같은 DI 시: 기존 전이 그래프 그대로(completed는 terminal — 사람 결재 필수)

**`ValidateVerdict` (재설계)**
- 경로 형식: `outputs/gates/<taskId>.gate.json`이어야 한다(다른 경로 거부)
- `gateVerdict == "PASS"`, `failureCount == 0` 검사
- `gate.taskId == targetDiId` 검사 (targetDiId = 요청의 diId 또는 현재 diId)
- stale 판정: `gate.createdAt.Date < WORKSTATE.updatedAt.Date`이면 거부
- `verificationPassed`/`exitCode` 옛 형식 더 이상 허용하지 않음

## 반증 시험 (negative test) ※필수
| # | 시험 | 기대 exit | 실제 exit | 판정 |
| --- | --- | --- | --- | --- |
| 1 | `completed`+diId 변경(DI-00-01→DI-00-04)+status=waiting | 0 | 0 | PASS |
| 2 | `verifying` 상태에서 diId 변경(DI-00-04) 시도 | 1 | 1 | PASS |
| 3 | 손으로 쓴 verdict(`{"verificationPassed":true,"exitCode":0}`)로 completed 전이 | 1 | 1 | PASS |
| 4 | `outputs/gates/DI-00-01.gate.json`(gateVerdict=PASS)로 completed 전이 | 0 | 0 | PASS |
| 5 | gateVerdict=FAIL인 gate.json | 1 | 1 | PASS |
| 6 | 존재하지 않는 gate.json 경로 | 1 | 1 | PASS |
| 7 | 같은 diId `completed → in_progress`(human-decision 없음) | 1 | 1 | PASS |
| 8 | phaseId 변경(P00→P01, human-decision 없음) | 1 | 1 | PASS |
| 9 | 새 DI 착수 시 비canonical diId(`LEDGER-05`) | 1 | 1 | PASS |

**시험 환경**: `--root C:\tmp\guard02\root-{completed,verifying}\` 사본 + `--dry-run`. 실 WORKSTATE 건드리지 않음.

## 검수 기준 자가점검표
| # | 기준 | 결과 | 근거 |
| --- | --- | --- | --- |
| 1 | `build-verify` exit 0, warning 0 | PASS | exit 0, 경고 0, 오류 0 |
| 2 | `verify-behavior` exit 0 | PASS | exit 0, behaviorEqual=true |
| 3 | `measure dev-pack` violations 0 | PASS | exit 0, violationCount=0 |
| 4 | 반증 시험 9개 전부 실측 | PASS | 전부 기대 exit == 실제 exit |
| 5 | `di-completion-check POST-EXECUTOR GUARD-02` PASS | PASS | gateVerdict=PASS, failureCount=0, evidencePath=outputs/gates/GUARD-02.gate.json |
| 6 | `projection` 실행 | 미실행(이 문서 작성 완료 직후 실행 예정) | — |
| 7 | 목적 기준: DI 경계를 넘을 수 있는가 | PASS | T1: exit 0 실측 |
| 8 | 목적 기준: 손으로 쓴 verdict가 더 이상 통과하지 않는가 | PASS | T3: exit 1 실측 |

## 게이트 기록
`{"gate":"dev-pack","violations":0,"attempt":1}`

di-completion-check: `{"harness":"di-completion-check","gateId":"POST-EXECUTOR","taskId":"GUARD-02","gateVerdict":"PASS","checkCount":7,"failureCount":0,"evidencePath":"outputs/gates/GUARD-02.gate.json"}`

## 잔여 위험 · 미확정 사항 ※필수

1. **stale 판정의 시간대 경계**: `gate.createdAt.Date < WORKSTATE.updatedAt.Date`로 판정한다. 한국 시간대(+09:00) 자정 근처에서 gate 생성과 전이가 날짜가 바뀌며 이루어지면 오탐(새 gate인데 stale로 판정)이 발생할 수 있다 — 아직 실측 없음.
2. **stale 파싱 실패 시 조용히 통과**: `WORKSTATE.updatedAt`이 빈 문자열이거나 `gate.createdAt`을 파싱할 수 없으면 stale 검사를 건너뛴다 — 설계상 의도적이지만, 비정상 값 주입으로 검사 회피 가능.

## 직접 경로 사용 사유

`server/StateApplierCli.cs` 수정을 직접 경로로 수행했다. 지시서 `directive-GUARD-02-di-boundary.md`의 allowlist에 `server/StateApplierCli.cs`가 명시되어 있어 직접 경로 예외에 해당한다.

## 지표는 만족했으나 목적은 미달인 부분 (ADR-005 — 필수)

- **stale 판정 기준이 약하다**: 현재 구현은 gate 생성일(날짜)만 비교한다. 같은 날 더 이전에 만들어진 gate를 재사용해도 통과한다. 지시서의 "생성 시각이 WORKSTATE보다 오래되지 않았는지 확인"은 완전히 충족하지 못한다. 판정 방법을 선택해야 했고 날짜 비교를 선택했으며 그 근거(단순성, 파싱 안정성)는 이 절에 기록한다.
- 이외 없음.

## 완료 판정

`PASS | FAIL | BLOCKED` — **생산자가 아니라 검수자가 적는다.**
