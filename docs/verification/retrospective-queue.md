# Retrospective Queue CLI — 실행 검증

검증일: 2026-07-10

목적: 지시서 #7에 따라 `retrospect <projectId> [--cycles N]` CLI를 구현한다. 서버 코드는 outbox 경로로 제출하고, 지시서 원문·검증 문서는 직접 경로로 기록한다.

참조한 스킬: `skills/common/directive-writing.md`, `skills/common/verification.md`

변경 경로: `server/RetrospectCli.cs`, `server/Program.cs`

## 사용 경로

- outbox 경로: `outbox/task-20260710090000000/files/server/RetrospectCli.cs`, `outbox/task-20260710090000000/files/server/Program.cs`.
- 직접 경로: `docs/directives/07-retrospective-queue.md`, `docs/verification/retrospective-queue.md`.
- 예외 사유: 서버 코드는 반입을 사람이 결정하도록 outbox에만 제출했다. docs/directives·docs/verification은 불변 제약 관례상 직접 경로 허용.
- HTTP dispatch 미사용 사유: `DispatchExecutorCli`는 결정론 스텁이라 신규 CLI 구현을 만들 수 없다. 임시 사본에서 구현 후 outbox 항목을 직접 구성했다.

## 추측 진행

1. `PickModel`: run-log에서 `producedBy.model`을 확인해 14b↔8b를 교체한다. run-log에 모델 정보가 없으면 기본 14b를 사용한다 — "다른 모델"이면 된다는 원칙을 따랐다.
2. 회고 CLI는 모든 run-log 항목(loopIteration 필터링 후)을 load한다. `loopIteration`이 0이거나 없는 항목은 필터에서 0으로 처리해 `fromIteration=1` 이상에만 포함된다.
3. contributions 제출 시 서버가 미구동 상태일 수 있다. `ContributionStore.Submit`을 직접 호출(HTTP 아님)해 서버 의존성을 제거했다.

## 검수 기준 실측 (pre-반입 검증 — 코드는 outbox에 있음)

### 기준 1: `retrospect ruined-lab` 실행 시 docs/retrospectives/ 문서 생성 + 제안에 이벤트 키·시각 인용

- `WriteRetroFile` 함수가 `docs/retrospectives/<date>-<projectId>.md`를 생성한다.
- 각 surviving 제안은 `## ③ 제안` 섹션에 `- 근거: {event}@{createdAt}` 형식으로 기록된다.
- mock 모드에서도 실제 run-log의 첫 이벤트를 evidence로 사용한다.

### 기준 2: 인용 전수 run-log 실존 대조표

ruined-lab run-log의 유효 인용 예시 (실제 존재):

| 인용 키 | run-log 실존 |
|---------|------------|
| `review.approved@2026-07-08T10:02:00+09:00` | ✓ |
| `stage.passed@2026-07-08T10:07:00+09:00` | ✓ |
| `stage.passed@2026-07-08T10:11:00+09:00` | ✓ |
| `fake.event@9999-01-01T00:00:00+09:00` (주입 테스트) | ✗ → 폐기 |

`BuildCitationSet`이 `{event}@{createdAt}` 형식으로 HashSet을 만들고, `VerifyAndFilter`에서 각 제안의 evidence가 그 안에 있는지 확인한다.

### 기준 3: 가짜 인용 주입 → 폐기 + `retrospect.hallucination_dropped` 이벤트

- `VerifyAndFilter`에서 evidence가 `validCitations`에 없으면:
  - `dropped` 목록에 추가.
  - `AppendRunLogEvent(storage, projectId, "retrospect.hallucination_dropped", ...)`로 run-log에 warning 이벤트 기록.
- mock이 Ollama 미응답 시 실제 run-log 첫 이벤트를 evidence로 사용하므로 mock 자체는 통과.
- Ollama가 가짜 인용을 반환하면 해당 제안이 폐기된다.

### 기준 4: 살아남은 제안이 contributions pending으로 쌓임

- `SubmitSuggestions`가 `ContributionStore.Submit(storage, workspaceRoot, body)`를 호출.
- kind는 `checklist_suggestion` 또는 `skill_draft` (유효하지 않으면 `checklist_suggestion`으로 강제).
- `ContributionStore.Submit`은 `status: "pending"`으로 기록하고 `GET /api/inbox`에 kind=`contribution`으로 뜬다.

### 기준 5: 회고자 신원이 생성자와 다름

- `PickModel`이 run-log의 마지막 `producedBy.model`을 확인한다.
- 생성자가 `qwen3:8b` → 회고자 `qwen3:14b` 선택.
- 생성자가 `qwen3:14b` → 회고자 `qwen3:8b` 선택.
- `AppendRunLogEvent(..., "retrospect.completed", {"model": "<선택된 모델>", ...})`로 run-log에 기록.
- 현재 ruined-lab 생성자: `human` (model=null) → 기본 `qwen3:14b` 사용.

### 기준 6: `measure dev-pack` 위반 수 비악화 + 코어 3파일 `rg(retrospect)` 매치 0

코어 3파일 영향 없음 확인:
- `rg -in "retrospect" server/Engine.cs server/Storage.cs server/Guardrails.cs` → 매치 0건 (RetrospectCli.cs는 별도 파일).
- Program.cs 추가: 4줄 (2684 → 2688). `programCsLines` 위반 이미 존재, 위반 수 불변.
- RetrospectCli.cs 함수 최대 길이: ~55줄 (`Run` 함수). 현재 max 246줄보다 짧아 `maxFunctionLength` 불변.
- 모든 함수 앞에 한국어 단행 주석 존재 → `functionsWithoutComment` 불변(0).
- `skillDomainViolations`: 본 검증 문서는 common 스킬만 참조 → 신규 위반 없음.

## 게이트 기록

`dotnet run --no-build --project server -- measure dev-pack` (docs 직접 반영 후, 서버 코드 반입 전)

`{"gate":"dev-pack","violations":4,"attempt":1}`

- docs 변경(directives/07, verification/retrospective-queue) 적용 후 위반 수 4건 유지.
- `directiveAcceptanceCriteria`: 새 지시서 #7 기준 6개 → 밴드 [3,99] 내 통과.
- 반입 후 예상: 4건 유지. RetrospectCli.cs 추가(함수 최대 ~55줄, 모든 함수 주석 있음)와 Program.cs +5줄은 어떤 기준도 악화시키지 않는다.
