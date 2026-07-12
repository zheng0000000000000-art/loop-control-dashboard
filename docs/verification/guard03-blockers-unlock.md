# GUARD-03 검증 — handoff-integrity blockers[] 수정

## DI 유형 ※필수 (v9 §0.1)

- **선언한 유형**: `harness`

## 주체 (actor) ※필수
- **누가**: sonnet (claude-sonnet-4-6), 대화 세션
- **경로**: 대화 세션 (직접 경로 — ADR-014에 의한 ADR-002 1회 예외)

## 사용한 하네스 ※필수
| 하네스 | 명령 | 기대 exit | 실제 exit | 결과(핵심 수치) |
| --- | --- | --- | --- | --- |
| build-verify | `dotnet run --project server -c Release -- build-verify` | 0 | 0 | 경고 0개, 오류 0개 |
| verify-behavior | `dotnet run --project server -c Release -- verify-behavior` | 0 | 0 | behaviorEqual=true |
| measure | `dotnet run --project server -c Release -- measure dev-pack` | 0 | 0 | violations=0 |
| handoff-integrity (실 저장소) | `dotnet run --project server -c Release -- handoff-integrity` | 0 | 0 | failureCount=0, verdict=PASS |
| di-completion-check | `dotnet run --project server -c Release -- di-completion-check --gate POST-EXECUTOR --task GUARD-03` | 0 | 0 | gateVerdict=PASS |
| gate-clean | `dotnet run --project server -c Release -- gate-clean server` | 1(실행 직후) | 1 | PASS(기대 exit=1, HandoffIntegrityCli.cs 변경 중) |
| projection | `dotnet run --project server -c Release -- projection` | 0 | 0 | stamped=4, missingFiles=0 |

> **주의**: `di-completion-check`는 내부적으로 `dotnet run --no-build`(Debug 기본 설정)로 서브프로세스를 실행한다. 수정 후 Debug 구성 재빌드(`dotnet build server`)를 하지 않으면 OLD 바이너리를 사용해 handoff-integrity가 exit 1을 낸다. 이 이슈를 발견해 Debug 빌드 후 재실행했으며 gateVerdict PASS 확인.

## 유형별 필수 검증 ※필수 (v9 §0.1)

| DI 유형 | 필수 검증 | 수행 결과 |
| --- | --- | --- |
| harness | **positive·negative·결정성·격리** 테스트 | 아래 반증 시험 표 참조 — 7개 전부 실측 |

## 공통 완료 조건 ※필수 (v9 §0.1)

- [x] 선언한 DI 유형의 완료 프로필 충족 (harness: positive·negative·결정성·격리 전부)
- [x] 관련 계약·스키마·문서 갱신 — ADR-014 인용, 본 검증 문서 신규
- [x] 발견된 실패·위험·미확정 사항 기록 — 아래 기술
- [x] `WORKSTATE.json` 갱신 — 해당 없음 (상태 전이 불필요, 코드 수정만)
- [x] 변경 범위 준수(allowlist) — `server/Harness/HandoffIntegrityCli.cs`, `docs/verification/guard03-blockers-unlock.md`만 수정
- [x] 원본 저장소 무단 변경 없음 (commit/push/결재/반입/발사 없음)

## 실패 분류와 실패 사례 ※필수 (v9 §0.3)

- **실패 분류**: `design_learning`
- **근거**: 단수 `blocker` 필드가 WORKSTATE에 실제로 존재한 적 없음에도 하네스 코드에 "한 번도 발화 적 없는 죽은 경로"로 존재했다(STATE-01 검수 기록). v9 계약(`blockers[]` 배열)과 하네스 구현이 발산한 채 방치됐다가, 처음으로 `status=blocked` 상태가 되자 발화. ADR-014에 설계 학습으로 기록됨. 별도 위키 등록은 ADR-014가 충당한다고 판단하며, 신규 FAIL-ID 생성은 보류.

## 참조한 스킬 ※필수
- (없음 — 이번 작업에서 skills/ 문서를 명시적으로 라우팅하지 않았다. allowlist에 skills/가 없고, 단순 함수 수정이라 스킬 없이 진행 가능했다.)

## 변경 내용
| 파일 | 종류 | 요약 |
| --- | --- | --- |
| `server/Harness/HandoffIntegrityCli.cs` | 수정 | `CheckBlockerConsistency`: 단수 `blocker` 읽기 → 복수 `blockers[]` 배열 읽기로 교체. 하위 호환 없음. |
| `docs/verification/guard03-blockers-unlock.md` | 신규 | 본 검증 문서 |

## 반증 시험 (negative test) ※필수

사본 WORKSTATE 파일은 `C:\Users\1\AppData\Local\Temp\guard03-tests\`(bash: `/c/Users/1/AppData/Local/Temp/guard03-tests/`)에 생성해 실행했다. changedFiles는 실제 저장소 파일과 일치하는 해시를 사용해 changedFiles 검사가 통과하도록 구성했다.

| # | 시험 | 기대 exit | 실제 exit | 판정 |
| --- | --- | --- | --- | --- |
| 1 | 사본: `blocked` + `blockers` 1건 → `handoff-integrity` | 0 | 0 | PASS (positive) |
| 2 | 사본: `blocked` + `blockers` 빈 배열 → `handoff-integrity` | 1 | 1 | PASS (negative) — 실패 메시지: `blocked status requires a non-empty blockers array` |
| 3 | 사본: `completed` + `blockers` 1건 → `handoff-integrity` | 1 | 1 | PASS (negative stale) — 실패 코드: `stale` |
| 4 | 사본: `blocked` + `blockers` 필드 자체 없음 → `handoff-integrity` | 1 | 1 | PASS (negative) — 실패 코드: `missing` |
| 5 | ws-test1.json 2회 연속 실행 | 동일 exit/출력 | 동일 exit/출력 | PASS (결정성) |
| 6 | 하네스 실행 전후 `git status --porcelain` 비교 | 동일 | 동일 | PASS (격리, 읽기 전용) |
| 7 | **실 저장소** (`blocked` + `blockers` 1건) → `handoff-integrity` | 0 | 0 | PASS (목적 — 게이트 해제 확인) |

## 검수 기준 자가점검표
| # | 기준 | 결과 | 근거 |
| --- | --- | --- | --- |
| 1 | `build-verify` exit 0, warning 0 | PASS | 경고 0개, 오류 0개 |
| 2 | `verify-behavior` exit 0, behaviorEqual=true | PASS | behaviorEqual=true |
| 3 | `measure dev-pack` violationCount 0 | PASS | violations=0 |
| 4 | 반증 시험 7개 전부 실측 | PASS | 코드 검토로 갈음 없음 — 전부 실행 |
| 5 | `handoff-integrity` 실 저장소 exit 0 | PASS | failureCount=0, verdict=PASS |
| 6 | `di-completion-check --gate POST-EXECUTOR --task GUARD-03` gateVerdict PASS | PASS | gateVerdict=PASS (Debug 재빌드 후) |
| 7 | `projection` exit 0 | PASS | stamped=4, missingFiles=0 |

## 게이트 기록
`{"gate":"dev-pack","violations":0,"attempt":1}`

## 잔여 위험 · 미확정 사항 ※필수

1. **`di-completion-check` Debug/Release 불일치**: `di-completion-check`가 서브프로세스를 `dotnet run --no-build`(Debug 기본)로 실행한다. 코드를 수정하면 Debug 구성도 재빌드(`dotnet build server`)해야 `di-completion-check`가 신규 코드를 사용한다. 현재 run-book에 이 단계가 없으면 다음 실행자가 같은 함정에 빠진다. **CODEX-GATE-02 범위 내에서 run-book 또는 `di-completion-check` 개선이 필요하다** — 이번 작업 범위 밖이라 기록만 한다.
2. **Test 3에서 추가 실패 발생**: `completed` + `blockers` 1건 시험에서 blocker stale 실패 외에 `queue-status-mismatch`(DI-00-04가 WORKSTATE에선 completed이나 큐에선 open)도 함께 발생했다. 이는 테스트 픽스처의 status=completed 때문이며, 실 저장소에는 해당 없다.

## 직접 경로 사용 사유

ADR-002는 `server/Harness/**`를 코덱스 배타 영역으로 지정하지만, **ADR-014**가 `HandoffIntegrityCli.cs`의 `CheckBlockerConsistency` 함수 1개에 한해 1회 예외를 사람이 승인했다. 이 예외에 따라 직접 경로로 수정했다. 예외 범위(파일 1개, 함수 1개) 준수 확인.

## 지표는 만족했으나 목적은 미달인 부분 (ADR-005)

- **`di-completion-check` Debug 빌드 요건**: `di-completion-check`가 `dotnet run --no-build`(Debug)로 서브프로세스를 실행하는 구조적 이슈를 발견했으나, `DiCompletionCheckCli.cs`는 이번 allowlist 밖이라 수정하지 않았다. 이 구조가 다음 실행자에게도 같은 혼란을 줄 수 있다. 잔여 위험에 기록하고 CODEX-GATE-02에 전달한다.
- 그 외 없음.

## 완료 판정

`PASS | FAIL | BLOCKED` — **생산자가 아니라 검수자가 적는다.**
