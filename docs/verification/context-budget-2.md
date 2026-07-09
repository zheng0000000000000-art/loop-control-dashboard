# ContextBudget 재적용 + 반입 stale 가드 — 실행 검증

검증일: 2026-07-10

목적: 지시서 #11-R에 따라 `ContextBudget` 계측을 현행 main 기준으로 다시 제출하고, outbox 반입 전에 원본 파일 해시가 달라졌는지 검사하는 stale guard를 추가한다.

참조한 스킬: `skills/common/directive-writing.md`, `skills/common/verification.md`, `skills/domains/dev/file-navigation.md`, `skills/domains/docs/README.md`. `design`·`game` 도메인은 변경 경로와 맞지 않아 열지 않았다.

## 전제 조건

- `outbox/task-20260709133814794/meta.json`: `status="rejected"`, `rejectReason="stale: superseded"`.
- `outbox/task-20260709201002395/meta.json`: `status="rejected"`, `rejectReason="stale: lang conflict, reapply via 11-R"`.
- 시작 기준선: `dotnet run --project server -- measure dev-pack` → `{"projectId":"dev-pack","violationCount":3,"proposalId":"proposal-1783631806759","proposalLifecycle":"submitted","createdBy":{"provider":"ollama","model":"qwen3:8b"},"currentStage":"changeReview","overallStatus":"warning"}`.

## 사용 경로

- outbox 경로: `outbox/task-20260710063111000/files/server/ContextBudget.cs`, `server/OutboxManager.cs`, `dashboard/data/lang/ko.json`, `dashboard/data/lang/en.json`.
- 직접 경로: `docs/directives/11R-contextbudget-stale-guard.md`, `docs/verification/context-budget-2.md`.
- 예외 사유: 지시서가 검증 문서와 지시서 원문 보관은 직접 경로를 허용했다. 서버 코드와 lang 변경은 반입을 사람이 결정하도록 outbox에만 제출했다.
- HTTP dispatch 미사용 사유: 현재 `DispatchExecutorCli`는 README 한 줄 추가와 self-refactor 템플릿만 수행하는 결정론 스텁이라 이 지시의 서버 코드·lang 변경을 만들 수 없다. 임시 사본에서 구현·검증 후 outbox 항목을 직접 구성했고, meta의 `executionNote`에 남겼다.

## 추측 진행

1. `contextBytes` 합산에서 `dashboard/data/*/run-log.json`을 제외했다. `ContextBudget.Attach`가 `context.budget` 이벤트를 run-log에 추가하므로 포함하면 재dispatch 결정론 기준과 충돌한다. #11 검증 문서의 추측 진행 1을 그대로 따랐다.
2. 409 오류의 대시보드 표시는 서버 `reason`을 그대로 alert에 띄우는 현재 구조를 유지했다. 따라서 `DispatchHttpException`의 메시지를 사람이 읽을 수 있게 쓰고, lang에는 `dispatch.stale_base` 이벤트 템플릿을 추가했다.
3. `baseCommit`은 `git rev-parse HEAD` 결과를 참고용으로 기록한다. 임시 검증 사본은 `.git` 없이 만들었으므로 검증 사본의 dispatch meta에서는 빈 문자열이었고, 실제 제출 outbox meta에는 현재 워크스페이스 HEAD `9c2c2c01f4b946b67b8444bebff850f45c4c8b17`을 기록했다.

## 검수 기준 실측

1. dispatch 1회 후 task meta에 세 값이 0이 아닌 실측값으로 존재한다.
   - 사본 서버 `POST /api/projects/dev-pack/actions/dispatch`, task `task-20260709212058152`.
   - meta 발췌: `contextBytes=2784569`, `estimatedContextTokens=696142`, `contextFileCount=131`, `contextTokensEstimation="contextBytes/4"`.

2. 동일 지시 재dispatch 시 세 값이 정확히 일치한다.
   - 두 번째 task `task-20260709212125375`.
   - meta 발췌: `contextBytes=2784569`, `estimatedContextTokens=696142`, `contextFileCount=131`, `contextTokensEstimation="contextBytes/4"`.
   - 세 값 모두 task `task-20260709212058152`와 일치.

3. run-log에 `context.budget` 이벤트가 남고, ko/en 템플릿이 존재하며, 기존 approver 계열 템플릿 키가 보존되어 있다.
   - run-log 이벤트 발췌:

```json
{"event":"context.budget","params":{"taskId":"task-20260709212058152","contextBytes":2784569,"estimatedContextTokens":696142,"contextFileCount":131,"estimation":"contextBytes/4"},"level":"info"}
```

```json
{"event":"context.budget","params":{"taskId":"task-20260709212125375","contextBytes":2784569,"estimatedContextTokens":696142,"contextFileCount":131,"estimation":"contextBytes/4"},"level":"info"}
```

   - `rg -n "context\\.budget|dispatch\\.stale_base|review\\.approved|alreadyDecided" dashboard/data/lang/ko.json dashboard/data/lang/en.json` 확인: ko/en 모두 `context.budget`, `dispatch.stale_base`, `review.approved`, `alreadyDecided` 존재.

4. 새로 만든 task의 meta에 `baseCommit`과 `changedFiles` 전 파일의 `originalFileHashes`가 기록된다.
   - 제출 task: `outbox/task-20260710063111000/meta.json`.

```json
{"baseCommit":"9c2c2c01f4b946b67b8444bebff850f45c4c8b17","changedFiles":["dashboard/data/lang/en.json","dashboard/data/lang/ko.json","server/ContextBudget.cs","server/OutboxManager.cs"],"originalFileHashes":{"dashboard/data/lang/en.json":"e11ce25077d673e4caa7d1c198b20e4d78b3cfe3a3cc228b676ee5537229bd75","dashboard/data/lang/ko.json":"6029ad64990e4de2e86a6a5c3cdbc53184ad2db9e4e98b5c24ce5b5f46970e43","server/ContextBudget.cs":"absent","server/OutboxManager.cs":"c3ef645be7703d01614bf531cc81557dafb68ffacf268726b36b249ea04a0ba7"}}
```

5. 사본 환경에서 stale 변경을 주입하면 `approve-import`가 409 `dispatch.stale_base`를 반환하고, 되돌리면 같은 task가 정상 반입된다.
   - stale 주입 대상: `README.md`, task `task-20260709212058152`.
   - 차단 실측: `blockedStatus=409`, body `{"reasonCode":"dispatch.stale_base","reason":"Import blocked because workspace files changed after dispatch: README.md"}`.
   - 파일 보호 실측: stale 주입 후 해시 `1C46A6E7CDDD30E5F715DAD91145025E93047DB115DE83D3A91F78E2DFC294A2`, 409 이후 해시 동일, `unchangedWhileBlocked=true`.
   - 변경 되돌림 후 같은 task 정상 반입: `importStatus="imported"`, `staleCheck="passed"`, `README.md`에 `Dispatch verification line` 반입됨.
   - legacy 호환 실측: `originalFileHashes`를 제거한 `task-legacy-11r` 반입 결과 `legacyStatus="imported"`, `legacyStaleCheck="skipped_legacy"`.

6. 코어 3파일에 금지 문자열이 없다.
   - 명령: `rg -in "budget|contextBytes|stale" server/Engine.cs server/Storage.cs server/Guardrails.cs`.
   - 결과: 매치 0건.

7. `measure dev-pack` 위반 수가 작업 시작 시점보다 증가하지 않는다.
   - 시작 기준선: 위반 3건.
   - 변경 적용 사본: `{"projectId":"dev-pack","violationCount":3,"proposalId":"proposal-1783632175112","proposalLifecycle":"submitted","createdBy":{"provider":"ollama","model":"qwen3:8b"},"currentStage":"changeReview","overallStatus":"warning"}`.
   - 최종 워크스페이스: 아래 게이트 기록과 같이 위반 3건. 증가 없음.

## 추가 확인

- 빌드: 임시 사본 `dotnet build server` → 경고 0, 오류 0.
- 기준 파일 무수정: `blueprint.json`, `workflow-definition.json`은 수정하지 않았다.
- `Program.cs` 무수정. stale 로직은 `OutboxManager` 내부 보조 함수로만 추가했다.
- 이번 제출물 자체는 아직 stale guard의 보호를 받지 못한다. 반입 전 main에 다른 커밋을 만들지 말 것.

## 게이트 기록

`dotnet run --project server -- measure dev-pack`

`{"gate":"dev-pack","violations":3,"attempt":1}`

남은 위반 수 3건은 작업 시작 시점과 동일하다. 지시서 검수 기준의 "증가하지 않음"은 충족하지만, AGENTS.md의 일반 품질 게이트 "위반 0"은 기존 기준선 때문에 미충족이다. 남은 위반 목록은 기존 `skillDomainViolations`, `programCsLines`, `maxFunctionLength` 계열로 이번 변경 범위 밖이다.
