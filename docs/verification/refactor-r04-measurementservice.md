# DI-R-04 검증 보고서 — MeasurementService 분리 (Program.cs 해체 4/4)

실행일: 2026-07-10  
실행자: claude-sonnet-4-6

## 검수 기준 실측

| # | 기준 | 결과 |
|---|------|------|
| 빌드 | 경고 0·오류 0 | ✓ |
| verify-behavior | behaviorEqual=true | ✓ |
| Program.cs 줄수 | 2321→2296 (목표 ≤2661) | ✓ |
| maxFunctionLength 위반 | server/Program.cs 해소 | ✓ |
| measure 위반 수 | 기준선 3 → 이동 후 3 (비악화) | ✓ |
| 코어 3파일 무접촉 | rg 출력 없음 | ✓ |

## 이동된 함수

`server/MeasurementService.cs` (신규, 65줄):
- `RunMeasureCore(Storage, string, JsonSerializerOptions, NtfyOptions) : MeasureOutcome`
- private `ResolveMeasurementTargetRoot(string, JsonObject) : string` (Program.cs에서 이동)
- 위임자 2개: `ApplyResult`, `PersistBundle`

## Program.cs 변경

- `CliRouter.MeasureCore = RunMeasureCore` 제거 (위임자 불필요)
- `MeasurementService.ApplyResult = ApplyMeasurementResult` 추가
- `MeasurementService.PersistBundle = (s,p,b,o,n) => { Persist(s,p,b,o,n); }` 추가
- `Measure()` 핸들러 → `MeasurementService.RunMeasureCore(...)` 호출
- `Approve()` 핸들러 내 `RunMeasureCore(...)` → `MeasurementService.RunMeasureCore(...)`
- `RunMeasureCore` 함수 블록 제거 (~30줄)
- `ResolveMeasurementTargetRoot` 함수 블록 제거 (~20줄)
- `ApplyMeasurementResult` (261줄) → 5개 서브함수로 분리:
  - `ApplyMeasurementResult` 프레임: 75줄 ✓
  - `ApplyMeasurementRegressionCase`: 45줄 ✓
  - `ApplyMeasurementTuningCase`: 62줄 ✓
  - `RunTuningRegenerationLoop`: 34줄 ✓
  - `ApplyMeasurementDevPackCase`: 50줄 ✓
  - `ApplyMeasurementCompliantCase`: 17줄 ✓

## Cli/CliRouter.cs 변경

- `MeasureCore` 위임자 필드 제거
- `RunMeasureCli` 내 `MeasureCore(...)` → `MeasurementService.RunMeasureCore(...)` 직접 호출

## 편차 기록

`ApplyMeasurementResult`의 의존 함수(EvaluateBlueprintChecks, DetectRegressions, SetTier1Details 등)는 Approve 핸들러 등 다른 코드에서도 공유하므로 MeasurementService로 이동하지 않고 Program.cs에 유지했다. 서브함수 분리로 maxFunctionLength 위반을 해소했다.

measure 후 `maxFunctionLength` 위반 증거가 `outbox/task-20260710070612000/files/server/Program.cs:790-1035`를 가리킨다. 이는 이전 outbox 사본 파일이며 현재 `server/Program.cs`의 위반은 해소됐다. 위반 수 총합은 3으로 비악화.

## 게이트 기록

`{"gate":"dev-pack","violations":3,"attempt":1}`

## 추측 진행 없음

## 참조한 스킬

없음

## 후속

WP-REFACTOR-PROGRAM 완료 (DI-R-01~04 전체 이행).
