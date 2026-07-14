# CODEX-DIRECTIVE-AUTHOR 검증 — 지시자 역할 설계

> **모든 DI의 검증 문서는 이 템플릿을 쓴다**(v9 §DI-00-02). 절을 지우지 마라 — 해당 없으면 "없음"이라고 쓴다.

## DI 유형 ※필수 (v9 §0.1)

- **선언한 유형**: documentation

## 주체 (actor) ※필수

- **누가**: 코덱스, 대화 세션
- **경로**: 대화 세션

## 사용한 하네스 ※필수

| 하네스 | 명령 | 기대 exit | 실제 exit | 결과(핵심 수치) |
| --- | --- | --- | --- | --- |
| measure | `dotnet run --project server -- measure dev-pack` | 0 | 0 | violations=0, overallStatus=completed |

## 유형별 필수 검증 ※필수 (v9 §0.1 — 선언한 유형의 행만 채운다)

| DI 유형 | 필수 검증 | 수행 결과 |
| --- | --- | --- |
| documentation | 링크·필수 항목·안정 section ID lint | 요구된 필수 절 10개를 모두 포함했다. 지정 문서와 공통 스킬을 읽고 역할 경계, 사람 게이트, 검수자 분리, TRUSTED_BASELINE 전/후 차이를 문서화했다. |

## 공통 완료 조건 ※필수 (v9 §0.1 — 전부 체크)

- [x] 선언한 DI 유형의 완료 프로필 충족
- [x] 관련 계약·스키마·문서 갱신
- [x] 발견된 실패·위험·미확정 사항 기록
- [x] `WORKSTATE.json` 갱신 — 해당 없음(상태 전이 아님)
- [x] 변경 범위 준수(allowlist) · 파일 claim 규칙 준수
- [x] 원본 저장소 무단 변경 없음(commit/push/결재/반입/발사는 사람 게이트)

## 실패 분류와 실패 사례 ※필수 (v9 §0.3)

- **실패 분류**: design_learning
- **실패 사례 ID 또는 기존 위키 링크**: 신규 실패 사례 없음

## 참조한 스킬 ※필수

- `skills/common/directive-writing.md`
- `skills/common/executor-launch.md`
- `skills/common/hs-gate.md`
- `skills/common/powershell-encoding.md`
- `skills/common/root-cause-diagnosis.md`
- `skills/common/verification.md`

## 읽은 파일

| 파일 | 확인 내용 |
| --- | --- |
| `AGENT-GUIDE.md` | API, 작업 수명주기, 금지선, dispatch/outbox 원칙 |
| `CLAUDE.md` | 현재 Phase, 게이트, 금지 사항, verification 의무 |
| `docs/directives/_header.md` | Context Pack, allowlist, 불변 제약, 완료 조건 규칙 |
| `docs/context/RUNTIME-INDEX.md` | 현재 P00/WP-00/DI-00-04 blocked, 자동 발사 금지 상태 |
| `docs/handoff/SESSION-BRIEF-2026-07-13.md` | WP-STATE-INTEGRITY, TRUSTED_BASELINE 전 금지, 현재 위험 |
| `docs/handoff/REVIEWER-HANDOFF.md` | 검수자 분리, 사람 게이트, 실패 사례 |
| `docs/handoff/ORCHESTRATOR-PROGRAM-VISION.md` | 장기 조율 프로그램화 방향 |
| `docs/handoff/decisions/ADR-002-harness-ownership-split.md` | 제작자와 검사자 분리 원칙 |
| `docs/handoff/decisions/ADR-009-event-driven-coordinator.md` | 사건 기반 조율, QUOTA_SIGNAL 배경 |
| `docs/handoff/decisions/ADR-015-harness-actor-substitution.md` | 코덱스 헤드리스 경로 문제와 sonnet 대행 경계 |
| `docs/plan/wp/CODEX-HARNESS-LAUNCHER-minimal-contract.md` | Codex launcher 최소 계약과 금지 권한 |
| `docs/plan/wp/CODEX-HEADLESS-DISPATCH-CLEANUP-plan.md` | `codex exec` 정정, fail-closed, TRUSTED_BASELINE 전 구현 금지 |
| `docs/handoff/SONNET-QUEUE.md` | sonnet 발사 규칙, ACK/범위/QUOTA_SIGNAL 처리 |
| `docs/handoff/CODEX-QUEUE.md` | Codex 소유 영역, ADR-015 이후 큐 재편 |

## 변경 내용

| 파일 | 종류(신규/수정) | 요약 |
| --- | --- | --- |
| `docs/plan/wp/CODEX-DIRECTIVE-AUTHOR-role-plan.md` | 신규 | Codex가 지시자 역할을 맡을 수 있는지와 역할 경계, 지시서 절차, 사람 게이트, 검수자 분리 규칙을 문서화했다. |
| `docs/verification/codex-directive-author-role-plan.md` | 신규 | 본 문서 작성 검증과 게이트 결과를 기록했다. |

## 반증 시험 (negative test) ※필수

| # | 시험 | 기대 exit | 실제 exit | 판정 |
| --- | --- | --- | --- | --- |
| 1 | 코드 구현 없이 문서만 추가한 뒤 `measure dev-pack` 실행 | 0 | 0 | PASS |

## 검수 기준 자가점검표

| # | 기준 | 결과 | 근거 |
| --- | --- | --- | --- |
| 1 | 필수 절 포함 | PASS | 현재 판단, 역할 경계, 지시자 입력, 지시서 출력 형식, 사람 게이트, 검수자 분리, 금지 사항, TRUSTED_BASELINE 전/후 차이, 다음 세션 프롬프트 예시, 잔여 위험 포함 |
| 2 | 지시자와 검수자 분리 명시 | PASS | 역할 계획서 `검수자 분리` 절 |
| 3 | 발사·approve/reject/import/push/merge 금지 | PASS | 역할 계획서 `금지 사항` 및 `sonnet 발사 전 사람 게이트` 절 |
| 4 | Claude가 이어받을 수 있음 | PASS | 역할 계획서 `다음 세션 프롬프트 예시`에 Claude/Codex 공통 프롬프트 포함 |

## 게이트 기록

`{"gate":"dev-pack","violations":0,"attempt":1}`

## 잔여 위험 · 미확정 사항 ※필수

- 이 문서 자체의 최종 PASS 판정은 별도 reviewer 세션 또는 별도 read-only Codex 검수 세션이 해야 한다.
- 손문서 큐 상태와 L0 상태가 어긋날 수 있으므로 다음 세션은 `docs/context/RUNTIME-INDEX.md`를 먼저 확인해야 한다.

## 직접 경로 사용 사유 (썼다면)

docs/ 계획 문서와 verification 문서 자체 작성은 저장소 관례상 직접 경로 예외에 해당한다. 코드 구현, 발사, 결재, 반입, push, merge는 하지 않았다.

## 지표는 만족했으나 목적은 미달인 부분 (ADR-005 — 필수, 없으면 "없음"이라고 쓴다)

- 지표는 통과했지만 이 문서는 설계 초안이다. "Codex가 지시자 역할을 맡을 수 있다"는 결론은 문서 접근성과 역할 경계 기준의 판단이며, 실제 다음 sonnet 지시서 품질은 별도 검수 주체가 판정해야 한다.

## 완료 판정

`PASS | FAIL | BLOCKED` — **생산자가 아니라 검수자가 적는다.**
