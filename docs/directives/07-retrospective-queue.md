# 지시서 #7 — 회고 큐 (retrospect CLI)

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다. 작업 시작 전 그 파일을 먼저 읽어라.

## 전제 조건

없음 (읽기 전용 CLI — 다른 트랙과 독립. 단 #13이 반입돼 있으면 회고 실행자도 사다리를 쓴다).

## 배경

Axis 2(Knowledge)의 유휴 훈련 1세대. 회차가 끝난 뒤 로컬 모델(비용 0)이 run-log·incident·검증 문서를 되돌아보고 "다음에 더 잘할 것"을 제안으로 만든다. 원칙: **성찰은 검토의 입력이지 면제가 아니다** — 회고 산출물은 곧바로 스킬·체크리스트가 되지 않고 인박스 제안(승급=사람)으로만 들어간다.

## 작업

1. CLI `retrospect <projectId> [--cycles N]` (기본 최근 3회차):
   - 입력: run-log 이벤트, incident 문서, 최근 검증 문서.
   - 실행자: 로컬 ollama(14b — **다른 신원 원칙**: 회고자는 그 회차의 생성자(8b)와 달라야 한다. run-log에서 생성자를 확인해 겹치면 상위 모델이 아니라 14b/8b 중 다른 쪽을 쓴다).
   - 산출: `docs/retrospectives/<날짜>-<projectId>.md` — ①잘된 것 ②반복된 마찰 ③제안(각 제안에 근거 이벤트 키·시각 인용 필수).
2. **환각 방어**: 산출물의 모든 근거 인용(이벤트 키+시각)을 run-log에서 실존 대조한다. 실존하지 않는 인용이 하나라도 있으면 그 제안을 통째로 폐기하고 `retrospect.hallucination_dropped` 이벤트를 남긴다.
3. 살아남은 제안은 기존 `POST /api/contributions`(kind: `checklist_suggestion` 또는 `skill_draft`)로 제출해 인박스에 쌓는다 — 승급은 사람.
4. **수동 실행만**. 자동 스케줄·루프 훅 금지(그건 실적이 쌓인 뒤 별도 이양 안건).

## 필요 파일

`server/Program.cs`(CLI 분기 참조), `server/OllamaExecutor.cs`·`OllamaReviewer.cs`(호출 패턴 재사용), `server/ContributionStore.cs`, `dashboard/data/*/run-log.json`, `docs/incidents/`

## 구현 경계

- 신규 파일(예: `server/RetrospectCli.cs`). Program.cs 악화 금지. 코어 3파일 무접촉. 기준 파일 무수정.
- run-log·incident·기존 문서를 **읽기만** 한다. 유일한 쓰기 = retrospectives 문서 + contributions 제출 + 이벤트.
- 서버 코드는 outbox 경로 제출. 커밋·push 금지.

## 검수 기준 (검증 가능 문장 6개)

1. `retrospect ruined-lab` 실행 시 docs/retrospectives/에 문서가 생성되고, 모든 제안에 이벤트 키·시각 인용이 붙어 있다.
2. 인용 전수가 run-log에 실존함을 검증 문서에 대조표로 남긴다.
3. 가짜 인용을 강제 주입한 mock 회고에서 해당 제안이 폐기되고 `retrospect.hallucination_dropped` 이벤트가 남는다.
4. 살아남은 제안이 contributions에 `pending`으로 쌓이고 인박스에 뜬다(승급 대행 금지).
5. 회고자 신원이 그 회차 생성자와 다름이 이벤트(모델명)로 확인된다.
6. `measure dev-pack` 위반 수 비악화 + 코어 3파일 rg(`retrospect`) 매치 0건.

## 보고 형식

`docs/verification/retrospective-queue.md`에 실측, 추측 진행, 사용 경로. 지시서 원문은 `docs/directives/07-retrospective-queue.md`로 보관.
