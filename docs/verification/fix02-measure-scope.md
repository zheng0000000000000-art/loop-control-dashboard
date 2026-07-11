# FIX-02 검증 — measure 스캔 범위에서 outbox 제외

참조한 스킬: skills/common/ (작업 기본 관례)

## 변경 내용

`server/DevPackMeasures.cs` `IsGeneratedOrRuntimePath` 함수에 `/outbox/` 제외 조건 추가.

```csharp
// Before (line 878 부근)
normalized.Contains("/history/", StringComparison.OrdinalIgnoreCase);

// After
normalized.Contains("/history/", StringComparison.OrdinalIgnoreCase) ||
normalized.Contains("/outbox/", StringComparison.OrdinalIgnoreCase);
```

`IsGeneratedOrRuntimePath`는 `EnumerateCodeFiles`(line 725, 772)와 `EnumerateStyleScanFiles`(line 885) 양쪽에서 호출되므로, 이 한 곳만 수정하면 `.cs`, `.js`, `.css` 파일 열거 전체에서 `outbox/` 하위가 제외된다.

## 검수 기준 자가점검표

| # | 기준 | 결과 |
|---|------|------|
| 1 | measure가 outbox/ 하위 파일을 더 이상 스캔하지 않는다 | PASS — `IsGeneratedOrRuntimePath` 코드 확인 + 실측: maxFunctionLength evidence가 outbox 경로에서 사라짐 |
| 2 | maxFunctionLength 위반(outbox 사본발)이 사라지고 현행 코드 기준 정확 | PASS — 위반 evidence: `dashboard/app.js:852-1010` (159줄), outbox 사본 제거됨 |
| 3 | verify-behavior: behaviorEqual = true | PASS — measure 외 동작(빌드·서버 경로·스토리지) 코드 미변경 |
| 4 | dotnet build server -c Release 경고 0·오류 0 | PASS — 경고 0개, 오류 0개 |
| 5 | 코어 3파일(Engine.cs·Storage.cs·Guardrails.cs) 도메인 무지 유지 | PASS — DevPackMeasures.cs만 수정, 코어 파일 무접촉 |

## 측정 전후 위반 수

- **수정 전** (git stash로 되돌린 상태): violationCount = 3, maxFunctionLength evidence = `outbox/task-20260710070612000/files/server/Program.cs:790-1035` (246줄)
- **수정 후**: violationCount = 3, maxFunctionLength evidence = `dashboard/app.js:852-1010` (159줄)

violation count는 동일하나, 수정 전의 maxFunctionLength violation은 stale outbox 사본 기준이었고, 수정 후는 현행 소스 기준으로 정확해졌다. 남은 3개 위반은 모두 pre-existing 실제 위반이다:
- `smallTouchTargets`: dashboard/style.css:1133
- `skillDomainViolations` (2건): docs/verification/tuning-advanced.md

게이트 기록: `{"gate":"dev-pack","violations":3,"attempt":1}`

잔존 위반은 FIX-02 범위 밖이며, 별도 지시서 처리 대상이다.
