# 지시서 #11-R — ContextBudget 재적용 + 반입 stale 가드

이 지시서는 docs/directives/_header.md의 불변 제약을 따른다. 작업 시작 전 그 파일을 먼저 읽어라.

## 전제 조건

미충족 시 중단하고 보고한다.

- outbox 두 건이 모두 rejected 상태여야 한다: `task-20260709133814794`(self-refactor, stale), `task-20260709201002395`(#11 A, lang 충돌).
- 하나라도 `import_pending`이면 작업하지 말고 그 사실만 보고한다. 거절은 사람 몫이다.

## 배경

#11 A의 outbox 제출물은 `08b648c`(AI 결재자) 커밋 이후 stale해져 거절됐다. 반입이 단순 파일 덮어쓰기라 같이 담긴 `ko/en.json` 스냅샷이 `08b648c`의 템플릿을 지우기 때문이다. 이번 작업은 1. `ContextBudget`을 현행 main 기준으로 다시 만들고 2. 같은 사고가 재발하지 않도록 반입에 stale 검사를 넣는다.

## 작업 A — ContextBudget 재적용

- 거절된 `outbox/task-20260709201002395/files/`의 `server/ContextBudget.cs`를 참조해 현행 main 위에 재적용한다. 신규 파일이므로 그대로 사용 가능하되 빌드로 확인한다.
- `OutboxManager`에 측정 훅 호출을 다시 넣는다. dispatch 사본 생성 시 `contextBytes` 실측, `estimatedContextTokens = bytes/4` 산정 방식 명기, `contextFileCount` 실측. run-log 합산 대상 제외로 결정론 유지.
- `context.budget` 이벤트의 ko/en 템플릿을 현행 lang 파일에 추가한다. 기존 키, 특히 `08b648c`의 approver 계열, 삭제·변경 금지.

## 작업 B — 반입 stale 가드

- dispatch가 outbox task를 만들 때 `meta.json`에 다음을 기록한다.
  - `baseCommit`: 사본을 뜬 시점의 워크스페이스 git HEAD(참고용)
  - `originalFileHashes`: `changedFiles` 각 파일의 사본 뜨기 전 원본 SHA-256. 원본에 없던 신규 파일은 값 `"absent"`.
- `ApproveImport`는 파일 복사 전에 검사한다. `changedFiles` 각 파일의 현재 워크스페이스 해시가 `originalFileHashes`와 하나라도 다르면, 신규 파일인데 현재 존재하는 경우 포함, 아무것도 덮어쓰지 않고 409 `dispatch.stale_base`를 반환한다. 병합 시도는 하지 않는다.
- `originalFileHashes`가 없는 과거 task는 검사 없이 기존 동작을 유지하되, 응답에 `staleCheck: "skipped_legacy"`를 남긴다.
- 409 사유가 대시보드 오류 표시에 사람이 읽을 수 있게 나오도록 ko/en 템플릿을 추가한다.

## 구현 경계

- 코어 3파일(`Engine.cs`, `Storage.cs`, `Guardrails.cs`) 무접촉.
- 기준 파일(`blueprint.json`, `workflow-definition.json`) 무수정.
- 해시·stale 로직은 가능하면 별도 파일 또는 `OutboxManager` 내 최소 추가. `Program.cs` 줄수 위반을 만들지 않는다.
- 서버 코드·lang은 outbox 경로로 제출한다. dispatch CLI 스텁 한계로 HTTP dispatch가 불가하면 #11과 같은 방식, 사본 작업 + outbox 항목 직접 구성, 을 쓰고 meta의 `executionNote`에 명기한다.
- 커밋·push는 하지 않는다.
- 이번 제출물 자체는 아직 stale 가드의 보호를 받지 못한다. 제출 후 즉시 보고하고, 보고에 "반입 전 main에 다른 커밋을 만들지 말 것"을 명시한다.
- 검증 문서(`docs/verification/context-budget-2.md`)와 지시서 원문 보관(`docs/directives/11R-contextbudget-stale-guard.md`)은 직접 경로 허용.

## 검수 기준

1. dispatch 1회 후 task meta에 `contextBytes`, `estimatedContextTokens`, `contextFileCount`가 0이 아닌 실측값으로 존재한다.
2. 동일 지시 재dispatch 시 세 값이 정확히 일치한다.
3. run-log에 `context.budget` 이벤트가 남고, ko/en 템플릿이 존재하며, 기존 approver 계열 템플릿 키가 그대로 보존되어 있다.
4. 새로 만든 task의 meta에 `baseCommit`과 `changedFiles` 전 파일의 `originalFileHashes`가 기록된다.
5. 사본 환경 실측: `changedFiles` 중 한 파일을 반입 전에 인위로 변경하면 `approve-import`가 409 `dispatch.stale_base`를 반환하고 워크스페이스 파일이 하나도 바뀌지 않는다. 변경을 되돌리면 같은 task가 정상 반입된다.
6. `rg -in "budget|contextBytes|stale" server/Engine.cs server/Storage.cs server/Guardrails.cs` 매치 0건.
7. `measure dev-pack` 위반 수가 작업 시작 시점보다 증가하지 않는다.

## 보고 형식

verification 문서에 검수 기준 7개 각각의 실측값(`meta.json`, 이벤트 JSON 발췌), 추측 진행 목록, 사용 경로(outbox/직접)와 예외 사유를 남긴다.
