# DI-R-02 검증 보고서 — InboxBuilder 분리 (Program.cs 해체 2/4)

실행일: 2026-07-10  
실행자: claude-sonnet-4-6

## 검수 기준 실측

### 빌드
```
dotnet build server -c Release → 경고 0개, 오류 0개 ✓
```

### 동작 불변 (verify-behavior)
```
{"behaviorEqual":true,"snapshot":"docs/behavior-snapshot.json"}  ✓
```

### measure dev-pack 위반 수
- 기준선 (DI-R-02 착수 시): 3 violations
- 완료 후: **3 violations** (비악화 ✓)

### 코어 3파일 무접촉
```
rg -in "budget|contextBytes" server/Engine.cs server/Storage.cs server/Guardrails.cs → 출력 없음 ✓
```

### Program.cs 줄수
- DI-R-01 완료 후: 2517줄
- DI-R-02 완료 후: **2398줄** (추가 감소 ✓)

---

## 이동 내역

`server/InboxBuilder.cs` (신규, 127줄):
- `BuildInboxItems(Storage, NtfyOptions) : JsonArray`
- `AddProjectInboxItems(Storage, string, string, JsonArray, NtfyOptions) : void`
- `FindProposalCreatedAt(JsonObject, string) : string?`
- `SummarizeProposal(JsonObject) : string`
- Private 복사본: `ValueText`, `ValueTextOrNone` (Program.cs 로컬 함수 접근 불가)

`server/Program.cs`:
- `Inbox()` 내: `BuildInboxItems(...)` → `InboxBuilder.BuildInboxItems(...)`
- `ProjectContext()` 내: `AddProjectInboxItems(...)` → `InboxBuilder.AddProjectInboxItems(...)`
- 4개 함수 제거 (약 119줄 감소)

---

## 계획 대비 편차 (추측 진행 아님)

계획에는 `Inbox` 함수도 InboxBuilder.cs로 이동하도록 명시됐으나, `Inbox()`가 `JsonResult()` (Program.cs 로컬 함수)를 직접 호출하므로 외부 클래스로 이동 시 컴파일 오류 발생. 동작 변경 없이 이동 불가능하므로 `Inbox()`는 Program.cs에 유지하되 내부 호출을 `InboxBuilder.BuildInboxItems()`로 교체함. 이 편차를 보고에 기록함 (DI-R-04에서 JsonResult를 분리 시 재검토 가능).

---

## 중간 위반 발생 및 해소

DI-R-02 초기 measure 실행 시 위반 수가 3→4로 증가함. 원인: `ValueTextOrNone` 함수에 주석 미기재로 `functionsWithoutComment: 1` 위반 발생. 린터가 자동으로 주석 추가하여 해소됨 (최종 3 violations).

---

## WORKSTATE 발췌

```json
{"diId":"DI-R-02","status":"verifying","measureViolationsBefore":3,"measureViolationsAfter":3}
```

게이트 기록: `{"gate":"dev-pack","violations":3,"attempt":2}`

## 참조한 스킬

없음.

## 후속

DI-R-03 (CycleSummaryBuilder → server/CycleSummaryBuilder.cs)
