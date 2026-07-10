# DI-R-01 — CLI 분리 (Program.cs 해체 1/4)

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다. 작업 시작 전 그 파일을 먼저 읽어라.
유형: refactor(migration). WP: WP-REFACTOR-PROGRAM. 상세 계획: outputs/refactor-plan-program-cs.md 참조.

## 전제 조건

없음(순수 이동 리팩토링). 단 작업 트리에 다른 실행자의 미커밋 server/ 변경이 있으면 중단하고 보고(충돌 방지).

## 목표 (한 문장)

Program.cs의 CLI 명령 처리 8개를 `server/Cli/CliRouter.cs`로 이동한다 — **동작 변경 0**.

## 작업

1. **분리 전 기준선**: `dotnet run --project server -- snapshot-behavior`로 현재 동작 스냅샷을 저장한다(기존 하네스). measure dev-pack 위반 수도 기록.
2. main 상단의 CLI 분기(snapshot-behavior / verify-behavior / dispatch-executor / measure / simtest / simtune / refeedbacktest / tier2test)를 `CliRouter.TryRun(string[] args) : int?`로 옮긴다. Program.cs 최상단은 `if (CliRouter.TryRun(args) is int code) return code;` 한 줄로 대체.
3. CLI 전용 헬퍼(RunMeasureCli, RunSimTestCli, RunSimTuneCli, RunRefeedbackTestCli, BuildCliSummary, CliError, SimResultsEqual)를 CliRouter.cs로 함께 이동한다. 웹 라우트가 공유하는 함수(Measure, RunMeasureCore, ApplyMeasurementResult 등)는 **이동하지 않는다** — DI-R-04 몫. 공유 함수는 접근 가능하게 두되 이번엔 건드리지 않는다.
4. **동작 불변 증명**: 이동 후 `verify-behavior`가 1단계 스냅샷과 동일(behaviorEqual, exit 0)임을 확인한다. 각 CLI 명령을 1회씩 실제 실행해 이동 전과 출력/exit code가 같은지 표본 대조(measure·simtest·simtune 최소 3개).

## v9 최소 산출물 (WORKSTATE)

`docs/handoff/WORKSTATE.json`을 생성(없으면)하고 이 DI 상태를 기록한다(v9 §0.5 최소 계약: phaseId·wpId·diId·status·changedFiles+sha256·tests·nextActions·updatedBy). 이 DI의 status는 완료 시 `verifying`(반입 전이므로), changedFiles에 실제 파일과 hash.

## 구현 경계

- **순수 이동만.** 함수 본문 로직을 바꾸지 않는다(이름·시그니처 유지, 파일 위치만 이동). 리팩토링 중 발견한 개선은 별도 기록만, 이번에 적용 금지.
- 코어 3파일 무접촉. 기준 파일 무수정. 웹 라우트 정의 무접촉.
- outbox 경로로 제출(반입=사람 또는 한정 이양 상위 AI). 커밋·push 금지.

## 검수 기준 (검증 가능 문장 6개)

1. `server/Cli/CliRouter.cs`가 존재하고 8개 CLI 명령을 처리한다.
2. Program.cs 최상단 CLI 분기가 `CliRouter.TryRun` 호출 한 곳으로 대체되고, 파일 줄수가 기준선보다 감소했다(목표: 밴드 2661 이하 복귀).
3. `verify-behavior`가 분리 전 스냅샷과 동일(behaviorEqual=true, exit 0).
4. measure·simtest·simtune 각 1회 실행 결과가 이동 전과 동일(표본 대조표).
5. `dotnet build server` 경고 0·오류 0. `rg -in "budget|contextBytes" server/Engine.cs server/Storage.cs server/Guardrails.cs` 무관(코어 무접촉 확인).
6. measure dev-pack 위반 수가 기준선보다 증가하지 않음(가능하면 programCsLines 해소로 감소).

## 보고

`docs/verification/refactor-r01-cli.md`에 검수 기준 6개 실측(verify-behavior 결과·줄수 전후·표본 대조), WORKSTATE 발췌, 추측 진행, 경로. 지시서 원문은 `docs/directives/R01-cli-extract.md`로 보관. 후속: DI-R-02(InboxBuilder).
