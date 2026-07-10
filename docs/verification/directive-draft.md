# Directive Draft CLI — 실행 검증

검증일: 2026-07-10

목적: 지시서 #12에 따라 `docs/directives/_template.md` 템플릿과 `directive-draft` CLI 초안 렌더러를 만든다. 서버 코드는 outbox로 제출하고, 템플릿·지시서 원문·검증 문서는 직접 경로로 기록한다.

참조한 스킬: `skills/common/directive-writing.md`, `skills/common/verification.md`, `skills/domains/dev/file-navigation.md`, `skills/domains/docs/README.md`. `design`·`game` 도메인은 변경 경로와 맞지 않아 열지 않았다.

## 전제 조건

- `docs/directives/_header.md`를 먼저 읽었다.
- `server/ContextBudget.cs` 존재 확인: `Test-Path server/ContextBudget.cs` → `True`.
- #11-R outbox `outbox/task-20260710063111000/meta.json`: `status="imported"`, `importedAt="2026-07-10T06:31:59.9252897+09:00"`.
- 시작 기준선: `dashboard/data/dev-pack/measurement.json`의 위반 지표 3건(`skillDomainViolations=2`, `programCsLines=2684`, `maxFunctionLength=246`). `dotnet run --project server -- measure dev-pack`는 실행 중 서버 프로세스가 `bin/Debug` exe를 잠가 빌드 단계에서 실패했으나, `dotnet run --no-build --project server -- measure dev-pack`로 위반 3건을 확인했다.

## 사용 경로

- outbox 경로: `outbox/task-20260710070612000/files/server/DirectiveDraftCli.cs`, `outbox/task-20260710070612000/files/server/Program.cs`.
- 직접 경로: `docs/directives/_template.md`, `docs/directives/12-directive-template.md`, `docs/verification/directive-draft.md`.
- 예외 사유: 지시서가 템플릿·docs는 직접 경로를 허용했다. 서버 코드는 반입을 사람이 결정하도록 outbox에만 제출했다.
- HTTP dispatch 미사용 사유: 현재 `DispatchExecutorCli`는 README 한 줄 추가와 self-refactor 템플릿만 수행하는 결정론 스텁이라 본 지시의 신규 CLI 구현을 만들 수 없다. 임시 사본에서 구현·검증 후 outbox 항목을 직접 구성했고, meta의 `executionNote`에 남겼다.

## 추측 진행

1. CLI는 `measurement.json`과 `blueprint.json`을 결합해 "최신 measure 결과의 위반"을 계산한다. 지시서가 "최신 measure 결과"라고 했고, 저장 위치를 필요 파일로 명시했기 때문에 CLI 실행 때마다 새 측정을 수행하지 않았다.
2. 산출 초안에는 생성 시각 필드를 넣지 않았다. 결정론 검수 기준에서 "생성 시각 필드 제외"라고 했지만, 1단계 렌더러에는 시각 자체가 필요 없다고 판단했다.
3. `docs/directives/_template.md` 자체가 최신 지시서로 측정될 수 있어, 템플릿의 검수 기준 섹션에도 번호 있는 플레이스홀더 3개를 남겼다. 이로써 템플릿 파일만 최신이어도 `directiveAcceptanceCriteria` 새 위반을 만들지 않는다.

## 검수 기준 실측

1. `directive-draft dev-pack --title "테스트"` 실행 시 초안 md가 생성되고 전제 조건 섹션에 현재 `import_pending` task 목록 또는 "대기 반입 없음"이 들어 있다.
   - 명령: `dotnet run --project server -- directive-draft dev-pack --title "테스트"`.
   - 산출: `docs/directives/drafts/draft-dev-pack-테스트.md`.
   - 전제 조건 섹션: `- 대기 반입 없음`(검증 사본에는 outbox 대기 항목 없음).

2. 구현 경계 섹션에 최신 measure 위반이 지표명·실측값·밴드와 함께 자동 기입되어 있다.
   - 초안 발췌: `maxFunctionLength 246 / 밴드 [0,80]`, `programCsLines 2684 / 밴드 [0,2661]`, `skillDomainViolations 2 / 목표 0`.

3. 검수 기준 섹션에 자동 2개와 플레이스홀더가 남아 있다.
   - 자동 항목: `measure dev-pack` 위반 수 비악화, `rg -in "directive|draft|template" ...` 코어 청결.
   - 플레이스홀더: `3. {{검수기준}}`, `4. {{검수기준}}`, `5. {{검수기준}}`.

4. `--from-violation` 성공/실패 경로를 확인했다.
   - 성공: `dotnet run --no-build --project server -- directive-draft dev-pack --from-violation maxFunctionLength`.
   - 산출: `docs/directives/drafts/draft-dev-pack-maxfunctionlength-개선.md`.
   - 배경 발췌: `현재 maxFunctionLength 지표가 246로 밴드 [0,80]를 벗어났다. 근거: server/Program.cs:785-1030`.
   - 실패: `dotnet run --no-build --project server -- directive-draft dev-pack --from-violation appJsLines` → exit 1, stderr `지정한 지표는 현재 위반이 아니다: appJsLines`, `현재 위반: maxFunctionLength, programCsLines, skillDomainViolations`.

5. 동일 시스템 상태에서 2회 실행한 산출이 동일하다.
   - 명령: `dotnet run --no-build --project server -- directive-draft dev-pack --title "테스트"` 2회.
   - stdout 동일: `true`.
   - 파일 SHA-256 1회: `8DDE69D510830214C88C6961160C45C2A38D6E9FF581AE412238903522DDACAA`.
   - 파일 SHA-256 2회: `8DDE69D510830214C88C6961160C45C2A38D6E9FF581AE412238903522DDACAA`.

6. 코어 3파일에 금지 문자열이 없다.
   - 명령: `rg -in "directive|draft|template" server/Engine.cs server/Storage.cs server/Guardrails.cs`.
   - 결과: 매치 0건.

7. `measure dev-pack` 위반 수가 작업 시작 시점보다 증가하지 않는다.
   - 시작 기준선: 3건.
   - 변경 적용 사본: `{"projectId":"dev-pack","violationCount":3,"proposalId":"proposal-1783633123813","proposalLifecycle":"submitted","createdBy":{"provider":"ollama","model":"qwen3:8b"},"currentStage":"changeReview","overallStatus":"warning"}`.
   - 최종 워크스페이스: 아래 게이트 기록처럼 3건. 증가 없음.

## 생성된 초안 전문

```md
# 지시서 {{번호}} — 테스트

이 지시서는 docs/directives/_header.md의 불변 제약을 따른다. 작업 시작 전 그 파일을 먼저 읽어라.

## 전제 조건

- 대기 반입 없음

{{전제조건}}

## 배경

{{배경}}

## 작업

{{작업}}

## 필요 파일

- `docs/directives/_header.md`
- `docs/directives/_template.md`
- `server/Program.cs`
- `server/DirectiveDraftCli.cs`
- `server/OutboxManager.cs`
- `dashboard/data/dev-pack/measurement.json`

## 구현 경계

- CLI + 템플릿 범위로 제한한다. 서버 HTTP 라우트는 추가하지 않는다.
- 코어 3파일(`Engine.cs`, `Storage.cs`, `Guardrails.cs`) 무접촉. 기준 파일(`blueprint.json`, `workflow-definition.json`) 무수정.
- 서버 코드는 outbox 경로로 제출한다. 템플릿·docs는 직접 경로 허용. 커밋·push 금지.
- 최신 measure 위반 — 악화 금지:
  - maxFunctionLength 246 / 밴드 [0,80] — server/Program.cs:785-1030
  - programCsLines 2684 / 밴드 [0,2661] — server/Program.cs:2684 lines
  - skillDomainViolations 2 / 목표 0 — docs/verification/tuning-advanced.md -> skills/domains/dev/file-navigation.md, docs/verification/tuning-advanced.md -> skills/domains/game/balance-tuning.md

{{구현경계}}

## 검수 기준

1. `dotnet run --project server -- measure dev-pack` 위반 수가 작업 시작 시점보다 증가하지 않는다.
2. `rg -in "directive|draft|template" server/Engine.cs server/Storage.cs server/Guardrails.cs` 매치 0건.

3. {{검수기준}}
4. {{검수기준}}
5. {{검수기준}}

## 보고 형식

docs/verification/{{검증문서}}.md에 검수 기준 실측, 생성된 초안 전문 1건, 추측 진행 목록, 사용 경로와 예외 사유를 남긴다.
```

## outbox meta 발췌

`outbox/task-20260710070612000/meta.json`

```json
{"status":"import_pending","baseCommit":"b3e9d2a003b7d72bcc30989c5af942b83f1b877d","changedFiles":["server/DirectiveDraftCli.cs","server/Program.cs"],"originalFileHashes":{"server/DirectiveDraftCli.cs":"absent","server/Program.cs":"34ab529350c669239a44d1835579721f79717d22d2c73c318ac5a578ae98abc6"}}
```

## 게이트 기록

`dotnet run --no-build --project server -- measure dev-pack`

`{"gate":"dev-pack","violations":3,"attempt":1}`

남은 위반 수 3건은 작업 시작 시점과 동일하다. 지시서 검수 기준의 "증가하지 않음"은 충족하지만, AGENTS.md의 일반 품질 게이트 "위반 0"은 기존 기준선 때문에 미충족이다. 남은 위반 목록은 기존 `skillDomainViolations`, `programCsLines`, `maxFunctionLength` 계열이다.
