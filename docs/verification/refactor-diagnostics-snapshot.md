# 리팩토링 진단 게이트와 동일성 스냅샷 검증

## 참조한 스킬
- `skills/common/directive-writing.md`
- `skills/common/verification.md`
- `skills/domains/dev/file-navigation.md`
- `skills/domains/docs/README.md`

## 변경 경로
- `server/Program.cs`
- `server/DevPackMeasures.cs`
- `server/BehaviorSnapshotCli.cs`
- `dashboard/data/dev-pack/blueprint.json`
- `dashboard/data/dev-pack/measurement.json`
- `dashboard/data/dev-pack/patch-proposal.json`
- `dashboard/data/dev-pack/review-report.json`
- `dashboard/data/dev-pack/run-log.json`
- `dashboard/data/dev-pack/workflow-state.json`
- `docs/behavior-snapshot.json`
- `docs/verification/refactor-diagnostics-snapshot.md`

## 구현 확인
- O dev-pack 측정 지표 추가
  - `programCsLines`: `Program.cs` 줄 수.
  - `appJsLines`: `dashboard/app.js` 줄 수.
  - `maxFunctionLength`: 정규식 근사 최대 함수 길이.
- O blueprint 기준
  - `programCsLines` band: `[0,2661]`.
  - `appJsLines` band: `[0,2692]`.
  - `maxFunctionLength` band: `[0,80]`.
- O 동일성 스냅샷 CLI
  - `dotnet run --project server --no-build -- snapshot-behavior`
  - 결과: `docs/behavior-snapshot.json` 생성.
- O 동일성 검증 CLI
  - `dotnet run --project server --no-build -- verify-behavior`
  - 결과: `{"behaviorEqual":true,"snapshot":"docs/behavior-snapshot.json"}`.

## 측정 결과
- 명령: `dotnet run --project server --no-build -- measure dev-pack`
- 결과 요약:
```json
{"projectId":"dev-pack","violationCount":1,"proposalLifecycle":"submitted","currentStage":"changeReview","overallStatus":"warning"}
```
- 구조 위반:
  - `maxFunctionLength=235`
  - evidence: `server/Program.cs:687-921`
- 정상 범위:
  - `programCsLines=2418`
  - `appJsLines=2446`
- 결재 상태:
  - 최신 구조 위반 proposal이 `submitted` 상태로 생성되었다.
  - 승인·거절은 수행하지 않았다.

## 게이트 기록
```json
{"gate":"dev-pack","violations":1,"attempt":1}
```

## 판정
- O 이번 사이클에서 리팩토링 자체는 하지 않았다.
- O 리팩토링 입력으로 쓸 구조 위반 proposal이 생성되었다.
- O `snapshot-behavior`와 `verify-behavior`가 현재 기준선에서 동일성을 판정한다.
