# DI-R-02 검증 보고서 — InboxBuilder 분리 (Program.cs 해체 2/4)

실행일: 2026-07-10  
실행자: claude-sonnet-4-6

## 검수 기준 실측

| # | 기준 | 결과 |
|---|------|------|
| 빌드 | 경고 0·오류 0 | ✓ |
| verify-behavior | behaviorEqual=true | ✓ |
| Program.cs 줄수 | 2517→2398 (목표 ≤2661) | ✓ |
| measure 위반 수 | 기준선 3 → 이동 후 3 (비악화) | ✓ |
| 코어 3파일 무접촉 | rg 출력 없음 | ✓ |

## 이동된 함수

`server/InboxBuilder.cs` (신규, 127줄):
- `BuildInboxItems(Storage, NtfyOptions) : JsonArray`
- `AddProjectInboxItems(Storage, string, string, JsonArray, NtfyOptions) : void`
- `FindProposalCreatedAt(JsonObject, string) : string?`
- `SummarizeProposal(JsonObject) : string`
- private 복사본: `ValueText`, `ValueTextOrNone`

## Program.cs 변경

- `Inbox()` 내 `BuildInboxItems(...)` → `InboxBuilder.BuildInboxItems(...)`
- `ProjectContext()` 내 `AddProjectInboxItems(...)` → `InboxBuilder.AddProjectInboxItems(...)`
- 4개 함수 블록 제거 (346~464줄 → 119줄 절약)

## 편차 기록 (계획 대비)

계획에 `Inbox` 함수도 이동 목록에 포함됐으나, `Inbox()`는 `JsonResult()` (Program.cs 로컬 함수)를 호출하므로 이동 시 로직 변경 없이는 컴파일 불가. **Program.cs에 유지**하고 `InboxBuilder.BuildInboxItems`를 호출하는 어댑터로 남겼다. DI-R-04(MeasurementService) 작업 시 `JsonResult` 이동과 함께 재검토 가능.

`ValueText·ValueTextOrNone`는 Program.cs 로컬 함수이므로 InboxBuilder 내부에 private 복사본 사용 (DI-R-01의 `Number()` 동일 패턴).

초기 measure 시 `functionsWithoutComment: 1` 위반 발생 → `ValueTextOrNone` 주석 누락 확인 후 보완 → 재측정 3 violations (기준선 복귀).

## 게이트 기록

`{"gate":"dev-pack","violations":3,"attempt":2}`

## 추측 진행 없음

## 참조한 스킬

없음

## 후속

DI-R-03 (CycleSummaryBuilder → server/CycleSummaryBuilder.cs)
