# STATUS — 진행 스냅샷 (세션 이어받기용)

> 갱신: 2026-07-10. 이 파일 + docs/handoff/ + docs/verification/ 만 읽으면 어떤 세션이든 이어받는다.
> **새 세션 진입 순서**: ①이 파일 ②docs/handoff/WORKSTATE.json(현재 DI) ③docs/handoff/CODEX-QUEUE.md ④outputs/review-log.md(최근 검수) → 자동 루프 상태 확인 → 다음 작업.

## 한 줄 정의 / 북극성

AI가 만들고(sonnet), AI가 검토하고(코덱스+검수자), AI가 결재를 배우는 — 사람은 기준만 정하는 로컬 자율 런타임. 고정점: **결재·반입·기준변경·이양결정은 항상 사람.**

## 현재 위치 (2026-07-10)

**리팩토링 WP-REFACTOR-PROGRAM** (Program.cs 스파게티 해체):
- DI-R-01 CliRouter 분리 — 완료·커밋(88ea409), verify-behavior=true
- DI-R-02 InboxBuilder 분리 — 완료·커밋(b2e355a)
- DI-R-03 CycleSummaryBuilder 분리 — 완료·커밋(4675c4d), Program.cs 2321
- **DI-R-04 MeasurementService 분리 + maxFunctionLength 해소 — 진행 중** (sonnet 헤드리스, 조율자가 검수·커밋 예정). 이게 WP 마지막.

## 자동 루프 (세션 종료 후에도 계속 돎, Cowork 앱 켜져 있는 동안)

| 루프 | 주기 | 역할 |
| --- | --- | --- |
| sonnet 헤드리스 | 발사식(claude -p CLI) | server/ 구현·리팩토링. 다음 DI를 검수자/새세션이 발사 |
| **조율자** (scheduled task `recursion1-result-check`) | 5분 | 단일 커미터 — sonnet(server)·코덱스(docs) 산출물 검수(VERIFY-PROTOCOL)·커밋. 결재 대행 안 함 |
| 코덱스 | 15분 | QA·버그헌팅. sonnet 작업 확인(git log·WORKSTATE·verification)→호출부 정합성·회귀 QA. CODEX-AUTO-15min 루틴 |

## 협업 인프라 (docs/handoff/)

- `COLLAB-STRUCTURE.md` — 3자 역할·영역 소유·핸드오프
- `CODEX-ROLE-bug-hunter.md` — 코덱스 상시 역할(QA)
- `CODEX-AUTO-15min-routine.md` — 코덱스 15분 자동 루틴
- `VERIFY-PROTOCOL-universal.md` — 보편 검수 프로토콜(누구든 검수)
- `CODEX-QUEUE.md` — 코덱스 작업 큐
- `WORKSTATE.json` — 단일 상태 원본(v9)

## 새 세션이 할 일

1. R-04 완료됐나 확인(server/MeasurementService.cs 존재 + WORKSTATE diId). 조율자가 이미 커밋했을 수 있음(git log).
2. R-04까지 끝났으면 WP-REFACTOR-PROGRAM 완결 → 다음: 코덱스 S-01/S-02 재현 결과 반영(경로검증 수정 지시), 또는 대기열의 #10(AI결재자 재작업, 단 Tier2 이미 활성)·한정 이양 구현.
3. 실행자 발사는 FAIL-2026-005 교훈대로: 프롬프트 인자 직접 전달 + RedirectStandardOutput + PID/산출물로 실행 확인("launched"≠실행).
4. 헤드리스는 **순차**(같은 server/ 영역 동시 금지 — FAIL-2026-004).

## 사람 대기 (결재 게이트)

- outbox 반입: #12(task-...070612000)는 stale로 **거절** 대상(리팩토링으로 base 바뀜), #7·#8 outbox는 검토.
- 미결 확인: stale 에러 UI 노출 개선, S-01/S-02(경로검증 sibling-prefix escape) 코덱스 재현 결과.

## 대기열 (리팩토링 후)

한정 이양(게이트 클린 반입을 상위 AI 승인, 감사·캡·DECISIONS 기록) → #10 AI결재자 정리 → #13 실행자 사다리·할당량 원장 → #14 Context Pack → #7 회고 → #8 규범스토어. 상세: outputs/directive-queue.md.
