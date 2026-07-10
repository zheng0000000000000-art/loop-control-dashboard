# DI-R-03 검증 보고서 — CycleSummaryBuilder 분리 (Program.cs 해체 3/4)

실행일: 2026-07-10  
실행자: claude-sonnet-4-6

## 검수 기준 실측

| # | 기준 | 결과 |
|---|------|------|
| 빌드 | 경고 0·오류 0 | ✓ |
| verify-behavior | behaviorEqual=true | ✓ |
| Program.cs 줄수 | 2398→2321 (목표 ≤2661) | ✓ |
| measure 위반 수 | 기준선 3 → 이동 후 3 (비악화) | ✓ |
| 코어 3파일 무접촉 | rg 출력 없음 | ✓ |

## 이동된 함수

`server/CycleSummaryBuilder.cs` (신규, 92줄):
- `BuildCycleSummary(JsonObject state, JsonObject runLog, JsonObject proposal) : JsonObject`
- private `SumEventDuration(List<JsonObject>, string) : long`
- private `CalculateHumanWaitingMs(List<JsonObject>, JsonObject) : long`
- private `Number(JsonNode?, int) : int` (복사본)

## Program.cs 변경

호출 교체 3곳:
- line 228 `CycleSummary()` 핸들러 → `CycleSummaryBuilder.BuildCycleSummary(...)`
- line 254 `ProjectContext()` 핸들러 → `CycleSummaryBuilder.BuildCycleSummary(...)`
- line 2069 측정 결과 직렬화 → `CycleSummaryBuilder.BuildCycleSummary(...)`

함수 블록 제거: `BuildCycleSummary`·`SumEventDuration`·`CalculateHumanWaitingMs` (약 77줄)

## 편차 기록

계획에 `CycleSummary` 핸들러도 이동 목록에 포함됐으나, `JsonResult()` 로컬 함수 의존으로 Program.cs에 유지 (DI-R-01·02와 동일 패턴).

`Number()`는 Program.cs 로컬 함수 접근 불가로 private 복사본 사용 (CliRouter.cs와 동일 패턴).

## 게이트 기록

`{"gate":"dev-pack","violations":3,"attempt":1}`

## 추측 진행 없음

## 참조한 스킬

없음

## 후속

DI-R-04 (MeasurementService → server/MeasurementService.cs) — 중간 난이도, ApplyMeasurementResult(246줄) maxFunctionLength 위반도 함께 해소
