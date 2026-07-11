# FEAT-02 e2e-usage harness review

검수 대상: `a87e484` / FEAT-02
검수자: Codex
검수일: 2026-07-11

## 독립 재실행
- `dotnet build server -c Release`: 경고 0, 오류 0
- `dotnet run --project server -c Release --no-build -- verify-behavior`: `{"behaviorEqual":true,"snapshot":"docs/behavior-snapshot.json"}`
- `dotnet run --project server -c Release --no-build -- e2e-usage`: exit 0, 6개 시나리오 모두 `pass`, `failCount:0`
- `dotnet run --project server -c Release --no-build -- e2e-usage dev-pack`: exit 0, 6개 시나리오 모두 `pass`, `failCount:0`
- `dotnet run --project server -c Release --no-build -- e2e-usage nonexistent-project-xyz-e2e`: 비정상 projectId를 fail 시나리오로 보고, `failCount:4`
- `dotnet run --project server -c Release --no-build -- measure dev-pack`: exit 1, `violationCount:5`, `overallStatus:"warning"`

## 상태 변경 확인
- `e2e-usage` 실행 전후 다음 파일의 SHA-256을 비교했다.
  - `dashboard/data/dev-pack/measurement.json`
  - `dashboard/data/dev-pack/workflow-state.json`
  - `dashboard/data/dev-pack/run-log.json`
  - `dashboard/data/dev-pack/patch-proposal.json`
  - `dashboard/data/dev-pack/review-report.json`
- 결과: 변경 없음.

## 코드/라우팅 확인
- `server/E2EUsageCli.cs` 신규 파일 존재.
- `server/Cli/CliRouter.cs`에 `e2e-usage` 분기 존재.
- `server/Engine.cs`, `server/Storage.cs`, `server/Guardrails.cs`에서 `e2e`, `E2EUsage`, `CliRouter` 도메인 매치 없음.

## 불변식
- build 0/0: Y
- behaviorEqual true: Y
- e2e-usage 6개 시나리오 JSON 출력: Y
- 상태 무변경: Y
- 기준 파일 무수정: Y
  - `git diff --stat a87e484^..c9e1448 -- "**/blueprint.json" "**/workflow-definition.json" server/appsettings.json`: 빈 결과
- 코어 파일 도메인 오염 없음: Y

## 검증문서 주장 vs 실측
- 일치:
  - build 0/0
  - behaviorEqual true
  - `e2e-usage` 6개 시나리오 pass, `failCount:0`
  - 상태 무변경
  - 현재 `measure dev-pack` 위반 5
- 보완 필요:
  - `docs/verification/feat02-e2e-harness.md`가 워킹트리에는 있으나 현재 `git status` 기준 untracked다. 검증 문서 산출물은 다음 조율자 커밋에 포함되어야 한다.

## 판정
- 조건부 PASS
- 사유: 하네스 기능과 불변식은 통과했다. 다만 검증 문서가 아직 커밋 추적 상태가 아니므로 산출물 반영 보완이 필요하다.
