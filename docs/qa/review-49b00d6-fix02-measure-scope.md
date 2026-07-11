# FIX-02 measure scope review

검수 대상: `49b00d6` / `FIX-02`
검수자: Codex
검수일: 2026-07-11

## 독립 재실행
- `dotnet build server -c Release`: 경고 0, 오류 0
- `dotnet run --project server -c Release --no-build -- verify-behavior`: `{"behaviorEqual":true,"snapshot":"docs/behavior-snapshot.json"}`
- `dotnet run --project server -c Release --no-build -- measure dev-pack`: exit 1, `{"projectId":"dev-pack","violationCount":3,"proposalId":"proposal-1783747843518","proposalLifecycle":"submitted","createdBy":{"provider":"ollama","model":"qwen3:8b"},"currentStage":"changeReview","overallStatus":"warning"}`

## 코드/증거 확인
- `server/DevPackMeasures.cs`의 `IsGeneratedOrRuntimePath`에 `/outbox/` 제외 조건이 존재한다.
- `dashboard/data/dev-pack/measurement.json`의 `maxFunctionLength` evidence는 `dashboard/app.js:852-1010`이다.
- `dashboard/data/dev-pack/measurement.json`에서 `outbox/task` evidence는 발견되지 않았다.

## 불변식
- 코어 3파일 outbox 도메인 무접촉: Y
  - `rg -in "outbox" server/Engine.cs server/Storage.cs server/Guardrails.cs`: 매치 없음
- 기준 파일 무수정: Y
  - `git diff --stat 9a43f54^..49b00d6 -- "**/blueprint.json" "**/workflow-definition.json"`: 빈 결과
- 영역 격리: Y
  - 변경 파일은 `server/DevPackMeasures.cs`, `docs/directives/FIX02-measure-scope.md`, `docs/handoff/WORKSTATE.json`, `docs/verification/fix02-measure-scope.md`
- 비밀 미포함: Y
  - `server/appsettings.json` diff 없음

## 검증문서 주장 vs 실측
- 불일치 없음.
- `violationCount`는 3으로 비악화이며, `maxFunctionLength` evidence가 stale outbox 사본에서 현행 `dashboard/app.js` 기준으로 이동했다는 주장이 실측과 일치한다.

## 판정
- PASS
